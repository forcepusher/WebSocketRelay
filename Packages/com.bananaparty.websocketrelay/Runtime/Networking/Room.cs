using System;
using System.Collections.Generic;

namespace BananaParty.WebSocketRelay
{
    public class Room
    {
        private const float ConnectionTimeoutSeconds = 15f;

        private readonly Network _network;
        private readonly Guid _localPlayerGuid;

        private readonly List<NetworkPlayer> _networkPlayers = new();
        private readonly Dictionary<Guid, NetworkPlayer> _guidToNetworkPlayers = new();
        private readonly Dictionary<Guid, float> _timeSinceLastMessageByPlayer = new();
        private readonly Queue<byte[]> _payloadQueue = new();

        public string RoomName { get; private set; }

        public bool HasUnreadPayloadQueue => _payloadQueue.Count > 0;

        public byte[] ReadPayloadQueue() => _payloadQueue.Dequeue();

        public Room(Network network, string roomName, Guid localPlayerGuid)
        {
            RoomName = roomName;
            _network = network;
            _localPlayerGuid = localPlayerGuid;
        }

        public void Send(byte[] data)
        {
            _network.Send(RoomName, data);
        }

        public void ManualUpdate(float unscaledDeltaTime)
        {
            for (int networkPlayerIndex = _networkPlayers.Count - 1; networkPlayerIndex >= 0; networkPlayerIndex -= 1)
            {
                NetworkPlayer networkPlayer = _networkPlayers[networkPlayerIndex];
                Guid playerGuid = networkPlayer.Guid;
                float timeSinceLastMessage = _timeSinceLastMessageByPlayer[playerGuid] + unscaledDeltaTime;
                _timeSinceLastMessageByPlayer[playerGuid] = timeSinceLastMessage;

                if (timeSinceLastMessage > ConnectionTimeoutSeconds)
                    RemoveNetworkPlayer(networkPlayer);
            }
        }

        public void OnTopicMessage(Guid senderGuid, string topic, byte[] data)
        {
            _payloadQueue.Enqueue(data);

            if (senderGuid == _localPlayerGuid)
                return;

            if (_guidToNetworkPlayers.ContainsKey(senderGuid))
            {
                _timeSinceLastMessageByPlayer[senderGuid] = 0f;
            }
            else
            {
                NetworkPlayer networkPlayer = new NetworkPlayer(senderGuid);
                AddNetworkPlayer(networkPlayer);
            }
        }

        private void AddNetworkPlayer(NetworkPlayer networkPlayer)
        {
            _networkPlayers.Add(networkPlayer);
            _guidToNetworkPlayers[networkPlayer.Guid] = networkPlayer;
            _timeSinceLastMessageByPlayer[networkPlayer.Guid] = 0f;
            _network.AddNetworkPlayer(networkPlayer);
        }

        private void RemoveNetworkPlayer(NetworkPlayer networkPlayer)
        {
            _networkPlayers.Remove(networkPlayer);
            _guidToNetworkPlayers.Remove(networkPlayer.Guid);
            _timeSinceLastMessageByPlayer.Remove(networkPlayer.Guid);
            _network.RemoveNetworkPlayer(networkPlayer);
        }
    }
}
