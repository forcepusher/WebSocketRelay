using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace BananaParty.WebSocketRelay
{
    public class RelayConnection : IDisposable
    {
        private readonly Socket _socket;
        private readonly Dictionary<int, RoomConnection> _roomConnections = new();

        public bool IsConnected => _socket.IsConnected;

        public event Action<RoomConnection, byte[]> OnRoomMessage;

        public RelayConnection(string serverAddress)
        {
            _socket = new Socket(serverAddress);
        }

        public void Connect()
        {
            _socket.Connect();
        }

        internal void SendToServer(byte[] data)
        {
            _socket.Send(data);
        }

        internal void CheckPayloads()
        {
            while (_socket.HasUnreadPayloadQueue)
            {
                byte[] payload = _socket.ReadPayloadQueue();
                ProcessPayload(payload);
            }
        }

        public RoomConnection JoinRoom(int roomId)
        {
            if (_roomConnections.TryGetValue(roomId, out RoomConnection existing))
                return existing;

            byte[] joinMessage = new byte[5];
            joinMessage[0] = 0x01;
            BinaryPrimitives.WriteInt32LittleEndian(joinMessage.AsSpan(1), roomId);
            _socket.Send(joinMessage);

            RoomConnection roomConnection = new RoomConnection(this, roomId);
            _roomConnections.Add(roomId, roomConnection);
            return roomConnection;
        }

        public void LeaveRoom(int roomId)
        {
            if (!_roomConnections.TryGetValue(roomId, out RoomConnection roomConnection))
                throw new KeyNotFoundException($"Not connected to room {roomId}.");

            byte[] leaveMessage = new byte[5];
            leaveMessage[0] = 0x02;
            BinaryPrimitives.WriteInt32LittleEndian(leaveMessage.AsSpan(1), roomId);
            _socket.Send(leaveMessage);

            _roomConnections.Remove(roomId);
        }

        public void Dispose()
        {
            foreach (RoomConnection room in _roomConnections.Values)
            {
                room.Dispose();
            }
            _roomConnections.Clear();

            try
            {
                if (_socket.IsConnected)
                    _socket.Disconnect();
            }
            finally
            {
                _socket.Dispose();
            }
        }

        internal void ProcessPayload(byte[] payloadBytes)
        {
            while (payloadBytes.Length > 0)
            {
                int processedLength;
                byte type = payloadBytes[0];

                switch (type)
                {
                    case 0x12: // ROOM_MESSAGE
                        processedLength = ProcessRoomMessage(payloadBytes);
                        break;
                    default:
                        processedLength = SkipUnknownMessage(payloadBytes, type);
                        break;
                }

                if (processedLength >= payloadBytes.Length)
                    return;

                byte[] remaining = new byte[payloadBytes.Length - processedLength];
                Array.Copy(payloadBytes, processedLength, remaining, 0, remaining.Length);
                payloadBytes = remaining;
            }
        }

        private int ProcessRoomMessage(byte[] data)
        {
            if (data.Length < 5)
                throw new InvalidDataException("Incomplete ROOM_MESSAGE payload.");

            int roomId = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(1, 4));

            if (_roomConnections.TryGetValue(roomId, out RoomConnection room))
            {
                byte[] messageData = new byte[data.Length - 5];
                Array.Copy(data, 5, messageData, 0, messageData.Length);
                OnRoomMessage?.Invoke(room, messageData);
                room.InvokeOnMessage(messageData);
            }

            return data.Length;
        }

        private int SkipUnknownMessage(byte[] data, byte type)
        {
            Console.WriteLine($"[RelayConnection] Unknown message type: 0x{type:X2}");
            // Cannot determine length of unknown messages; skip what we can
            return 1;
        }
    }
}
