#if UNITY_WEBGL && !UNITY_EDITOR
using System;
using System.Runtime.InteropServices;

namespace BananaParty.WebSocketRelay
{
    public class BrowserSocket : ISocket
    {
        private const int NotConnectedSocketId = 0;

        private readonly string _serverAddress;

        private int _socketId = NotConnectedSocketId;
        private bool _isDisposed;
        private string _closeReason;

        public BrowserSocket(string serverAddress)
        {
            _serverAddress = serverAddress;
        }

        private bool HasSocket => _socketId != NotConnectedSocketId && !_isDisposed;

        public bool IsConnected => HasSocket && GetBrowserSocketIsConnected(_socketId);

        [DllImport("__Internal")]
        private static extern bool GetBrowserSocketIsConnected(int socketId);

        public bool IsClosed => _isDisposed || (HasSocket && GetBrowserSocketCloseCode(_socketId) != 0);

        public string CloseReason
        {
            get
            {
                if (_closeReason != null || !HasSocket)
                    return _closeReason;

                int closeCode = GetBrowserSocketCloseCode(_socketId);
                return closeCode == 0 ? null : $"Connection closed (code {closeCode})";
            }
        }

        /// <returns>WebSocket close code, or 0 while the socket has not closed.</returns>
        [DllImport("__Internal")]
        private static extern int GetBrowserSocketCloseCode(int socketId);

        public int PendingSendBytes => HasSocket ? GetBrowserSocketBufferedAmount(_socketId) : 0;

        [DllImport("__Internal")]
        private static extern int GetBrowserSocketBufferedAmount(int socketId);

        public bool HasUnreadPayloadQueue => HasSocket && GetBrowserSocketHasUnreadPayloadQueue(_socketId);

        [DllImport("__Internal")]
        private static extern bool GetBrowserSocketHasUnreadPayloadQueue(int socketId);

        public byte[] ReadPayloadQueue()
        {
            if (!HasUnreadPayloadQueue)
                throw new InvalidOperationException($"Trying to use {nameof(ReadPayloadQueue)} while {nameof(HasUnreadPayloadQueue)} is false.");

            int payloadBytesCount = BrowserSocketReadPayloadQueue(_socketId, null, 0);
            byte[] payloadBytesBuffer = new byte[payloadBytesCount];
            BrowserSocketReadPayloadQueue(_socketId, payloadBytesBuffer, payloadBytesBuffer.Length);
            return payloadBytesBuffer;
        }

        /// <summary>
        /// Does not remove item from the queue if it's not going to fit in <paramref name="payloadBytesBufferLength"/>.
        /// </summary>
        /// <returns>Received bytes count.</returns>
        [DllImport("__Internal")]
        private static extern int BrowserSocketReadPayloadQueue(int socketId, byte[] payloadBytesBuffer, int payloadBytesBufferLength);

        public void Connect()
        {
            if (_socketId != NotConnectedSocketId || _isDisposed)
                throw new InvalidOperationException($"{nameof(BrowserSocket)} can only connect once. Create a new one to reconnect.");

            _socketId = BrowserSocketConnect(_serverAddress);
        }

        [DllImport("__Internal")]
        private static extern int BrowserSocketConnect(string serverAddress);

        public void Send(byte[] payloadBytes)
        {
            if (!IsConnected)
                throw new InvalidOperationException($"Trying to use {nameof(Send)} while not {nameof(IsConnected)}.");

            BrowserSocketSend(_socketId, payloadBytes, payloadBytes.Length);
        }

        [DllImport("__Internal")]
        private static extern void BrowserSocketSend(int socketId, byte[] payloadBytes, int payloadBytesCount);

        public void Disconnect()
        {
            if (!HasSocket)
                return;

            _closeReason ??= CloseReason ?? "Disconnected locally";
            BrowserSocketDisconnect(_socketId);
        }

        [DllImport("__Internal")]
        private static extern void BrowserSocketDisconnect(int socketId);

        public void Dispose()
        {
            if (_isDisposed)
                return;

            Disconnect();
            _closeReason ??= "Disconnected locally";

            if (_socketId != NotConnectedSocketId)
                BrowserSocketDispose(_socketId);

            _isDisposed = true;
        }

        [DllImport("__Internal")]
        private static extern void BrowserSocketDispose(int socketId);
    }
}
#endif
