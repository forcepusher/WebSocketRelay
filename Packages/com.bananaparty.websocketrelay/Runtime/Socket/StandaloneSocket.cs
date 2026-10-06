using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace BananaParty.WebSocketRelay
{
    /// <summary>
    /// <see cref="ISocket"/> over <see cref="ClientWebSocket"/> for every platform except WebGL.
    /// Connecting, receiving and sending run on the thread pool, so throughput does not depend on the frame rate.
    /// </summary>
    public class StandaloneSocket : ISocket
    {
        private const int ReceiveChunkSize = 64 * 1024;

        // While this much received data waits to be read, reading pauses. A client that stops polling, like a paused app,
        // then pushes back on the relay server, which disconnects it, instead of buffering without limit.
        private const int MaxUnreadPayloadBytes = 4 * 1024 * 1024;

        private static readonly TimeSpan CloseHandshakeTimeout = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan UnreadPayloadPollInterval = TimeSpan.FromMilliseconds(10);

        private readonly Uri _serverUri;
        private readonly ClientWebSocket _clientWebSocket = new();
        private readonly ConcurrentQueue<byte[]> _payloadQueue = new();

        // Never disposed, because background work may still read its token after the socket is disposed.
        // Without timers or linked tokens it holds no unmanaged resources.
        private readonly CancellationTokenSource _disconnectTokenSource = new();

        private Task _lastSend = Task.CompletedTask;
        private int _pendingSendBytes;
        private int _unreadPayloadBytes;
        private bool _isConnectStarted;
        private volatile bool _isClosed;
        private string _closeReason;

        public StandaloneSocket(string serverAddress)
        {
            _serverUri = new Uri(serverAddress);
        }

        public bool IsConnected => _clientWebSocket.State == WebSocketState.Open;

        public bool IsClosed => _isClosed;

        public string CloseReason => Volatile.Read(ref _closeReason);

        public int PendingSendBytes => Volatile.Read(ref _pendingSendBytes);

        public bool HasUnreadPayloadQueue => !_payloadQueue.IsEmpty;

        public byte[] ReadPayloadQueue()
        {
            if (!_payloadQueue.TryDequeue(out byte[] payloadBytes))
                throw new InvalidOperationException($"Trying to use {nameof(ReadPayloadQueue)} while {nameof(HasUnreadPayloadQueue)} is false.");

            Interlocked.Add(ref _unreadPayloadBytes, -payloadBytes.Length);
            return payloadBytes;
        }

        public void Connect()
        {
            if (_isConnectStarted)
                throw new InvalidOperationException($"{nameof(StandaloneSocket)} can only connect once. Create a new one to reconnect.");

            _isConnectStarted = true;

            // Started on the thread pool, so not even a slow host name lookup can stall the calling frame.
            Task.Run(RunAsync);
        }

        public void Send(byte[] payloadBytes)
        {
            if (!IsConnected)
                throw new InvalidOperationException($"Connection is not open. State = {_clientWebSocket.State}");

            Interlocked.Add(ref _pendingSendBytes, payloadBytes.Length);
            _lastSend = SendAfterAsync(_lastSend, payloadBytes);
        }

        public void Disconnect()
        {
            SetCloseReason("Disconnected locally");

            if (!_isConnectStarted)
            {
                _isConnectStarted = true;
                ReleaseResources();
                return;
            }

            _disconnectTokenSource.Cancel();
        }

        public void Dispose()
        {
            Disconnect();
        }

        private static bool IsConnectionException(Exception exception)
        {
            return exception is OperationCanceledException
                or ObjectDisposedException
                or WebSocketException
                or IOException
                or SocketException;
        }

        // ClientWebSocket forbids concurrent sends, so every send waits for the one before it.
        // Neither await resumes on the main thread, which would let only one message out per frame.
        private async Task SendAfterAsync(Task previousSend, byte[] payloadBytes)
        {
            try
            {
                await previousSend.ConfigureAwait(false);
                await _clientWebSocket.SendAsync(
                    new ArraySegment<byte>(payloadBytes),
                    WebSocketMessageType.Binary,
                    endOfMessage: true,
                    _disconnectTokenSource.Token).ConfigureAwait(false);
            }
            catch (Exception exception) when (IsConnectionException(exception))
            {
                // Expected once the connection is gone, which closes the socket.
                SetCloseReason($"Send failed: {exception.Message}");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
            finally
            {
                Interlocked.Add(ref _pendingSendBytes, -payloadBytes.Length);
            }
        }

        private async Task RunAsync()
        {
            try
            {
                if (await TryConnectAsync().ConfigureAwait(false))
                    await ReceiveUntilClosedAsync().ConfigureAwait(false);
            }
            catch (Exception exception) when (IsConnectionException(exception))
            {
                SetCloseReason(exception.Message);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
            finally
            {
                SetCloseReason("Connection closed");
                ReleaseResources();
            }
        }

        private async Task<bool> TryConnectAsync()
        {
            try
            {
                await _clientWebSocket.ConnectAsync(_serverUri, _disconnectTokenSource.Token).ConfigureAwait(false);
                return true;
            }
            catch (Exception exception) when (IsConnectionException(exception))
            {
                SetCloseReason($"Connect failed: {exception.GetBaseException().Message}");
                return false;
            }
        }

        private async Task ReceiveUntilClosedAsync()
        {
            CancellationToken cancellationToken = _disconnectTokenSource.Token;
            byte[] chunkBuffer = new byte[ReceiveChunkSize];
            ArrayBufferWriter<byte> payloadWriter = new();

            while (true)
            {
                while (Volatile.Read(ref _unreadPayloadBytes) > MaxUnreadPayloadBytes)
                    await Task.Delay(UnreadPayloadPollInterval, cancellationToken).ConfigureAwait(false);

                WebSocketReceiveResult result = await _clientWebSocket.ReceiveAsync(
                    new ArraySegment<byte>(chunkBuffer),
                    cancellationToken).ConfigureAwait(false);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    SetCloseReason($"Server closed the connection ({(int?)result.CloseStatus} {result.CloseStatusDescription})");
                    await CloseOutputAsync().ConfigureAwait(false);
                    return;
                }

                payloadWriter.Write(new ReadOnlySpan<byte>(chunkBuffer, 0, result.Count));
                if (!result.EndOfMessage)
                    continue;

                byte[] payloadBytes = payloadWriter.WrittenSpan.ToArray();
                payloadWriter.Clear();
                Interlocked.Add(ref _unreadPayloadBytes, payloadBytes.Length);
                _payloadQueue.Enqueue(payloadBytes);
            }
        }

        private async Task CloseOutputAsync()
        {
            WebSocketState state = _clientWebSocket.State;
            if (state != WebSocketState.Open && state != WebSocketState.CloseReceived)
                return;

            // Bounded because a stalled connection never drains the close frame.
            using CancellationTokenSource timeoutTokenSource = new(CloseHandshakeTimeout);
            try
            {
                await _clientWebSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, string.Empty, timeoutTokenSource.Token).ConfigureAwait(false);
            }
            catch (Exception exception) when (IsConnectionException(exception))
            {
            }
        }

        private void SetCloseReason(string reason)
        {
            Interlocked.CompareExchange(ref _closeReason, reason, null);
        }

        private void ReleaseResources()
        {
            _clientWebSocket.Dispose();
            _isClosed = true;
        }
    }
}
