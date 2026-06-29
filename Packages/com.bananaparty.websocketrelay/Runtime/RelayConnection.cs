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

        public bool HasUnreadPayloadQueue => _socket.HasUnreadPayloadQueue;

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

        /// <summary>
        /// Drains queued WebSocket frames and dispatches room messages to their <see cref="RoomConnection"/> handlers.
        /// Call this periodically (e.g. in Update).
        /// </summary>
        public void ProcessIncomingMessages()
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

            byte[] joinMessage = new byte[RelayMessageHeader.Length];
            joinMessage[0] = RelayMessageType.JoinRoom;
            BinaryPrimitives.WriteInt32LittleEndian(joinMessage.AsSpan(RelayMessageHeader.RoomIdOffset), roomId);
            _socket.Send(joinMessage);

            RoomConnection roomConnection = new RoomConnection(this, roomId);
            _roomConnections.Add(roomId, roomConnection);
            return roomConnection;
        }

        public void LeaveRoom(int roomId)
        {
            if (!_roomConnections.TryGetValue(roomId, out RoomConnection roomConnection))
                throw new KeyNotFoundException($"Not connected to room {roomId}.");

            byte[] leaveMessage = new byte[RelayMessageHeader.Length];
            leaveMessage[0] = RelayMessageType.LeaveRoom;
            BinaryPrimitives.WriteInt32LittleEndian(leaveMessage.AsSpan(RelayMessageHeader.RoomIdOffset), roomId);
            _socket.Send(leaveMessage);

            _roomConnections.Remove(roomId);
            roomConnection.Dispose();
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
                    case RelayMessageType.JoinedRoom:
                    case RelayMessageType.LeftRoom:
                        processedLength = ProcessRoomControlMessage(payloadBytes);
                        break;
                    case RelayMessageType.RoomMessage:
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

        private int ProcessRoomControlMessage(byte[] data)
        {
            if (data.Length < RelayMessageHeader.Length)
                throw new InvalidDataException("Incomplete room control message.");

            return RelayMessageHeader.Length;
        }

        private int ProcessRoomMessage(byte[] data)
        {
            if (data.Length < RelayMessageHeader.Length)
                throw new InvalidDataException("Incomplete ROOM_MESSAGE payload.");

            int roomId = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(RelayMessageHeader.RoomIdOffset, 4));

            if (_roomConnections.TryGetValue(roomId, out RoomConnection room))
            {
                byte[] messageData = new byte[data.Length - RelayMessageHeader.PayloadOffset];
                Array.Copy(data, RelayMessageHeader.PayloadOffset, messageData, 0, messageData.Length);
                OnRoomMessage?.Invoke(room, messageData);
                room.InvokeOnMessage(messageData);
            }

            return data.Length;
        }

        private int SkipUnknownMessage(byte[] data, byte type)
        {
            return data.Length;
        }
    }
}
