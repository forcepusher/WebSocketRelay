using System;
using System.Collections.Generic;
using System.Net.WebSockets;

namespace BananaParty.WebSocketRelay
{
    public class Room
    {
        private readonly Network _network;
        public string RoomName { get; private set; }

        private readonly Queue<byte[]> _payloadQueue = new();

        public bool HasUnreadPayloadQueue => _payloadQueue.Count > 0;

        public byte[] ReadPayloadQueue() => _payloadQueue.Dequeue();

        public Room(Network network, string roomName)
        {
            RoomName = roomName;
            _network = network;
        }

        public void Send(byte[] data)
        {
            _network.Send(RoomName, data);
        }

        public void OnTopicMessage(string topic, byte[] data)
        {
            _payloadQueue.Enqueue(data);
        }
    }
}
