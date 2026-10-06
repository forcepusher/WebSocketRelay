#if !UNITY_WEBGL || UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace BananaParty.WebSocketRelay.Tests
{
    /// <summary>
    /// TCP proxy in front of the relay server that stalls, delays, or resets connections like an unreliable network.
    /// </summary>
    public sealed class TcpFaultProxy : IDisposable
    {
        private const int BufferSize = 16 * 1024;
        private const int StallPollMilliseconds = 5;

        private readonly int _targetPort;
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _disposeTokenSource = new();
        private readonly List<ProxiedConnection> _connections = new();
        private readonly object _connectionsLock = new();

        private volatile bool _isStalled;
        private volatile int _latencyMilliseconds;
        private int _acceptedConnectionCount;

        public TcpFaultProxy(int targetPort)
        {
            _targetPort = targetPort;
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _ = AcceptLoopAsync();
        }

        public int Port { get; }

        public string ServerAddress => $"ws://127.0.0.1:{Port}";

        /// <summary>
        /// While stalled, nothing is forwarded in either direction. Connections stay open, and held data
        /// is delivered in order once the stall ends, like TCP retransmitting over a link that came back.
        /// </summary>
        public bool IsStalled
        {
            get => _isStalled;
            set => _isStalled = value;
        }

        /// <summary>
        /// Delay added to every forwarded chunk in each direction.
        /// </summary>
        public int LatencyMilliseconds
        {
            get => _latencyMilliseconds;
            set => _latencyMilliseconds = value;
        }

        public int AcceptedConnectionCount => Volatile.Read(ref _acceptedConnectionCount);

        /// <summary>
        /// Resets every proxied connection, like a network change.
        /// </summary>
        public void ResetAllConnections()
        {
            List<ProxiedConnection> connections;
            lock (_connectionsLock)
            {
                connections = new List<ProxiedConnection>(_connections);
                _connections.Clear();
            }

            foreach (ProxiedConnection connection in connections)
                connection.Reset();
        }

        public void Dispose()
        {
            _disposeTokenSource.Cancel();
            _listener.Stop();
            ResetAllConnections();
        }

        private async Task AcceptLoopAsync()
        {
            while (!_disposeTokenSource.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is ObjectDisposedException or SocketException or InvalidOperationException)
                {
                    return;
                }

                _ = ProxyAsync(client);
            }
        }

        private async Task ProxyAsync(TcpClient client)
        {
            Interlocked.Increment(ref _acceptedConnectionCount);
            client.NoDelay = true;

            TcpClient upstream = new() { NoDelay = true };
            try
            {
                await upstream.ConnectAsync(IPAddress.Loopback, _targetPort).ConfigureAwait(false);
            }
            catch (SocketException)
            {
                new ProxiedConnection(client, upstream).Reset();
                return;
            }

            ProxiedConnection connection = new(client, upstream);
            lock (_connectionsLock)
            {
                if (_disposeTokenSource.IsCancellationRequested)
                {
                    connection.Reset();
                    return;
                }

                _connections.Add(connection);
            }

            Task clientToServer = PumpAsync(client.GetStream(), upstream.GetStream());
            Task serverToClient = PumpAsync(upstream.GetStream(), client.GetStream());
            await Task.WhenAny(clientToServer, serverToClient).ConfigureAwait(false);

            lock (_connectionsLock)
                _connections.Remove(connection);

            connection.Reset();
        }

        private async Task PumpAsync(NetworkStream source, NetworkStream destination)
        {
            byte[] buffer = new byte[BufferSize];
            CancellationToken cancellationToken = _disposeTokenSource.Token;

            try
            {
                while (true)
                {
                    await WaitWhileStalledAsync(cancellationToken).ConfigureAwait(false);

                    int readCount = await source.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
                    if (readCount == 0)
                        return;

                    int latencyMilliseconds = _latencyMilliseconds;
                    if (latencyMilliseconds > 0)
                        await Task.Delay(latencyMilliseconds, cancellationToken).ConfigureAwait(false);

                    await WaitWhileStalledAsync(cancellationToken).ConfigureAwait(false);
                    await destination.WriteAsync(buffer, 0, readCount, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException or OperationCanceledException or SocketException)
            {
            }
        }

        private async Task WaitWhileStalledAsync(CancellationToken cancellationToken)
        {
            while (_isStalled)
                await Task.Delay(StallPollMilliseconds, cancellationToken).ConfigureAwait(false);
        }

        private sealed class ProxiedConnection
        {
            private readonly TcpClient _client;
            private readonly TcpClient _upstream;

            public ProxiedConnection(TcpClient client, TcpClient upstream)
            {
                _client = client;
                _upstream = upstream;
            }

            public void Reset()
            {
                Reset(_client);
                Reset(_upstream);
            }

            // Zero linger makes Close send RST instead of a graceful FIN.
            private static void Reset(TcpClient tcpClient)
            {
                try
                {
                    tcpClient.LingerState = new LingerOption(true, 0);
                }
                catch (Exception exception) when (exception is ObjectDisposedException or SocketException)
                {
                }

                tcpClient.Close();
            }
        }
    }
}
#endif
