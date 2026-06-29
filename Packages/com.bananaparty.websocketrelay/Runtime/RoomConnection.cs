using System;
using System.Buffers.Binary;

namespace BananaParty.WebSocketRelay
{
    public class RoomConnection : IDisposable
    {
        private readonly RelayConnection _relayConnection;
        private int _roomId;
        private bool _isDisposed = false;

        public int RoomId => _roomId;

        public event Action<byte[]> OnMessageReceived;

        internal RoomConnection(RelayConnection relayConnection, int roomId)
        {
            _relayConnection = relayConnection;
            _roomId = roomId;
        }

        public void Send(byte[] data)
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(RoomConnection));

            byte[] message = new byte[5 + data.Length];
            message[0] = 0x03;
            BinaryPrimitives.WriteInt32LittleEndian(message.AsSpan(1), _roomId);
            Array.Copy(data, 0, message, 5, data.Length);

            _relayConnection.SendToServer(message);
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            OnMessageReceived = null;
            _isDisposed = true;
        }

        internal void InvokeOnMessage(byte[] data)
        {
            if (!_isDisposed)
                OnMessageReceived?.Invoke(data);
        }
    }
}
