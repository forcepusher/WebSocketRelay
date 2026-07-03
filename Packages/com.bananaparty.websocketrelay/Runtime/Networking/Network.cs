using System;
using System.Collections.Generic;
using BananaParty.WebSocketRelay.Transport;
using UnityEngine;

namespace BananaParty.WebSocketRelay
{
    public class Network : MonoBehaviour, IRelayListener, IDisposable
    {
        [SerializeField]
        private NetworkContext _networkContext;

        private readonly INetworkListener _networkListener;
        private readonly string _serverAddress;

        private Guid _localPlayerGuid;

        private RelayServerProcess _relayServerProcess;
        private RelayClient _relayClient;

        private readonly List<Room> _rooms = new();

        public Network(INetworkListener networkListener, string serverAddress)
        {
            _networkListener = networkListener;
            _serverAddress = serverAddress;
        }

        public void StartServer()
        {
            if (_relayServerProcess != null)
                throw new InvalidOperationException("Server already running");

            _relayServerProcess = new RelayServerProcess();
            _relayServerProcess.Start();
            Debug.Log("Relay server started.");
        }

        public void StopServer()
        {
            if (_relayServerProcess == null)
                throw new InvalidOperationException("Server not started to stop it");

            _relayServerProcess.Stop();
            _relayServerProcess = null;
            Debug.Log("Relay server stopped.");
        }

        public void Connect()
        {
            if (_relayClient != null)
                throw new InvalidOperationException("Already connected");

            _relayClient = new RelayClient(_serverAddress, this);
            _relayClient.Connect();
            Debug.Log($"Connected to relay server at {_serverAddress}");
        }

        public void Disconnect()
        {
            if (_relayClient == null)
                throw new InvalidOperationException("Not connected to disconnect");

            _relayClient.Dispose();
            _relayClient = null;
            Debug.Log("Disconnected from relay server.");
        }

        private void Update()
        {
            _relayClient?.ProcessIncomingMessages();

            foreach (Room room in _rooms)
                room.ManualUpdate(Time.unscaledDeltaTime);
        }

        public void Send(string topic, byte[] data)
        {
            _relayClient.Send(topic, data);
        }

        public void JoinRoom(string roomName)
        {
            _relayClient.Subscribe(roomName);
        }

        public void LeaveRoom(string roomName)
        {
            _relayClient.Unsubscribe(roomName);
        }

        public void Dispose()
        {
            _relayServerProcess?.Stop();
            _relayClient?.Dispose();
        }

        public void OnConnectedToRelay(Guid clientGuid)
        {
            _localPlayerGuid = clientGuid;
            _networkListener.OnConnectedToRelay(clientGuid);
        }

        public void OnSubscribedToTopic(string topic)
        {
            var room = new Room(this, topic, _localPlayerGuid);
            _rooms.Add(room);
            _networkListener.OnConnectedToRoom(room);
        }

        public void OnUnsubscribedFromTopic(string topic)
        {
            Room room = _rooms.Find(room => room.RoomName == topic);
            _networkListener.OnDisconnectedFromRoom(room);
            _rooms.Remove(room);
        }

        public void OnTopicMessage(Guid senderGuid, string topic, byte[] data)
        {
            Room room = _rooms.Find(room => room.RoomName == topic);
            room?.OnTopicMessage(senderGuid, topic, data);
        }

        internal void AddNetworkPlayer(NetworkPlayer networkPlayer)
        {
            _networkListener.OnRoomPlayerAdded(networkPlayer);
        }

        internal void RemoveNetworkPlayer(NetworkPlayer networkPlayer)
        {
            _networkListener.OnRoomPlayerRemoved(networkPlayer);
        }
    }
}
