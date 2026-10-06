using System.Collections.Generic;
using BananaParty.WebSocketRelay.Transport;

namespace BananaParty.WebSocketRelay
{
    /// <summary>
    /// <see cref="ISocket"/> implementation for offline mode that connects instantly
    /// and discards outgoing payloads, so multiplayer code runs without a relay server.
    /// Heartbeats are answered like the relay does, so the connection never looks lost.
    /// </summary>
    public class OfflineSocket : ISocket
    {
        private readonly Queue<byte[]> _payloadQueue = new();

        public bool IsConnected { get; private set; }

        public bool IsClosed { get; private set; }

        public string CloseReason { get; private set; }

        public int PendingSendBytes => 0;

        public bool HasUnreadPayloadQueue => _payloadQueue.Count > 0;

        public byte[] ReadPayloadQueue()
        {
            if (_payloadQueue.Count == 0)
                throw new System.InvalidOperationException($"Trying to use {nameof(ReadPayloadQueue)} while {nameof(HasUnreadPayloadQueue)} is false.");

            return _payloadQueue.Dequeue();
        }

        public void Connect()
        {
            IsConnected = true;
            IsClosed = false;
            CloseReason = null;
        }

        public void Send(byte[] payloadBytes)
        {
            if (!IsConnected)
                throw new System.InvalidOperationException($"Trying to use {nameof(Send)} while not {nameof(IsConnected)}.");

            if (payloadBytes.Length > 0 && payloadBytes[0] == RelayMessageType.Ping)
            {
                byte[] pongBytes = (byte[])payloadBytes.Clone();
                pongBytes[0] = RelayMessageType.Pong;
                _payloadQueue.Enqueue(pongBytes);
            }
        }

        public void Disconnect()
        {
            Close();
        }

        public void Dispose()
        {
            Close();
        }

        private void Close()
        {
            if (!IsConnected)
                return;

            IsConnected = false;
            IsClosed = true;
            CloseReason = "Disconnected locally";
            _payloadQueue.Clear();
        }
    }
}
