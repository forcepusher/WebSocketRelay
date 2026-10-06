using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace BananaParty.WebSocketRelay
{
    public class StandaloneSocket : ISocket
    {
        private const int ReceiveChunkSize = 65536;

        private static readonly TimeSpan CloseHandshakeTimeout = TimeSpan.FromSeconds(1);

        private readonly Uri _serverUri;

        private readonly ClientWebSocket _clientWebSocket = new();
        private readonly CancellationTokenSource _disconnectTokenSource = new();

        private readonly Queue<byte[]> _payloadQueue = new();

        private Task _lastSend = Task.CompletedTask;
        private int _pendingSendBytes;

        private bool _connectStarted;

        public StandaloneSocket(string serverAddress)
        {
            _serverUri = new Uri(serverAddress);
        }

        public bool IsConnected => _clientWebSocket.State == WebSocketState.Open;

        public bool IsClosed { get; private set; }

        public string CloseReason { get; private set; }

        public int PendingSendBytes => Volatile.Read(ref _pendingSendBytes);

        public bool HasUnreadPayloadQueue => _payloadQueue.Count > 0;

        public byte[] ReadPayloadQueue() => _payloadQueue.Dequeue();

        public void Connect()
        {
            if (_connectStarted)
                throw new InvalidOperationException($"{nameof(StandaloneSocket)} can only connect once. Create a new one to reconnect.");

            _connectStarted = true;
            ConnectAndReceiveLoopAsync();
        }

        public void Send(byte[] payloadBytes)
        {
            if (!IsConnected)
                throw new InvalidOperationException($"Connection is not open. State = {_clientWebSocket.State}");

            // Sends are chained because ClientWebSocket forbids concurrent SendAsync calls.
            // The token is captured now because the token source is disposed on disconnect.
            Interlocked.Add(ref _pendingSendBytes, payloadBytes.Length);
            _lastSend = SendAsync(_lastSend, payloadBytes, _disconnectTokenSource.Token);
            ObserveSend(_lastSend, payloadBytes.Length);
        }

        public void Disconnect()
        {
            CloseReason ??= "Disconnected locally";

            if (!_connectStarted)
            {
                _connectStarted = true;
                ReleaseResources();
                return;
            }

            try
            {
                _disconnectTokenSource.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
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

        private static void ObserveAbandoned(Task task)
        {
            task.ContinueWith(
                abandonedTask => _ = abandonedTask.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        private async Task SendAsync(Task previousSend, byte[] payloadBytes, CancellationToken cancellationToken)
        {
            // A failed previous send is observed by its own ObserveSend call.
            await previousSend.ContinueWith(_ => { }, TaskScheduler.Default);
            await _clientWebSocket.SendAsync(
                new ArraySegment<byte>(payloadBytes),
                WebSocketMessageType.Binary,
                endOfMessage: true,
                cancellationToken);
        }

        /// <summary>
        /// Surfaces unexpected send failures on the main thread like a fire-and-forget async void would.
        /// Sends failing because the connection is gone are expected and only recorded as the close reason.
        /// </summary>
        private async void ObserveSend(Task sendTask, int payloadBytesCount)
        {
            try
            {
                await sendTask;
            }
            catch (Exception exception) when (IsConnectionException(exception))
            {
                CloseReason ??= $"Send failed: {exception.Message}";
            }
            finally
            {
                Interlocked.Add(ref _pendingSendBytes, -payloadBytesCount);
            }
        }

        private async void ConnectAndReceiveLoopAsync()
        {
            try
            {
                if (await TryConnectAsync())
                    await ReceiveUntilClosedAsync();
            }
            catch (Exception exception) when (IsConnectionException(exception))
            {
                CloseReason ??= exception.Message;
            }
            finally
            {
                CloseReason ??= "Connection closed";
                ReleaseResources();
            }
        }

        private void ReleaseResources()
        {
            _clientWebSocket.Dispose();
            _disconnectTokenSource.Dispose();
            IsClosed = true;
        }

        private async Task<bool> TryConnectAsync()
        {
            Task connectTask = _clientWebSocket.ConnectAsync(_serverUri, _disconnectTokenSource.Token);

            // Polled instead of awaited so a disconnect request during the handshake
            // does not surface as "Cannot access a disposed object".
            while (!connectTask.IsCompleted)
            {
                await Task.Yield();

                if (_disconnectTokenSource.IsCancellationRequested)
                {
                    ObserveAbandoned(connectTask);
                    return false;
                }
            }

            if (connectTask.IsCompletedSuccessfully)
                return true;

            CloseReason ??= connectTask.IsCanceled
                ? "Connect canceled"
                : $"Connect failed: {connectTask.Exception?.GetBaseException().Message}";
            return false;
        }

        private async Task ReceiveUntilClosedAsync()
        {
            byte[] chunkBuffer = new byte[ReceiveChunkSize];
            var payloadWriter = new ArrayBufferWriter<byte>();

            while (true)
            {
                Task<WebSocketReceiveResult> receiveTask = _clientWebSocket.ReceiveAsync(chunkBuffer, _disconnectTokenSource.Token);

                // Polled instead of awaited because ReceiveAsync can hang forever when the server is gone.
                while (!receiveTask.IsCompleted)
                {
                    await Task.Yield();

                    if (_clientWebSocket.State == WebSocketState.Aborted)
                    {
                        ObserveAbandoned(receiveTask);
                        CloseReason ??= "Connection aborted";
                        return;
                    }
                }

                if (_disconnectTokenSource.IsCancellationRequested)
                {
                    ObserveAbandoned(receiveTask);
                    break;
                }

                WebSocketReceiveResult result = await receiveTask;

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    CloseReason ??= $"Server closed the connection ({(int?)result.CloseStatus} {result.CloseStatusDescription})";
                    break;
                }

                payloadWriter.Write(new ArraySegment<byte>(chunkBuffer, 0, result.Count));

                if (result.EndOfMessage)
                {
                    _payloadQueue.Enqueue(payloadWriter.WrittenSpan.ToArray());
                    payloadWriter = new ArrayBufferWriter<byte>();
                }
            }

            await CloseOutputAsync();
        }

        private async Task CloseOutputAsync()
        {
            WebSocketState state = _clientWebSocket.State;
            if (state != WebSocketState.Open && state != WebSocketState.CloseReceived)
                return;

            // Bounded because a stalled connection never drains the close frame.
            using var timeoutTokenSource = new CancellationTokenSource(CloseHandshakeTimeout);
            try
            {
                await _clientWebSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, string.Empty, timeoutTokenSource.Token);
            }
            catch (Exception exception) when (IsConnectionException(exception))
            {
            }
        }
    }
}
