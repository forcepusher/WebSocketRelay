using System;
using System.Collections.Generic;
using BananaParty.WebSocketRelay.Transport;
using UnityEngine;

namespace BananaParty.WebSocketRelay
{
    public class Network : IRelayListener, IDisposable
    {
        private readonly NetworkContext _networkContext;
        private readonly string _serverAddress;
        private readonly INetworkListener _networkListener;

        public Guid LocalPlayerGuid { get; private set; }

        private RelayServerProcess _relayServerProcess;
        private RelayClient _relayClient;

        private readonly List<Room> _rooms = new();

        public Network(string address, NetworkContext context, INetworkListener listener)
        {
            _serverAddress = address;
            _networkContext = context;
            _networkListener = listener;
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

        public void ManualUpdate(float unscaledDeltaTime)
        {
            _relayClient?.ProcessIncomingMessages();

            foreach (Room room in _rooms)
                room.ManualUpdate(unscaledDeltaTime);

            //var jsonStateOutput = new JsonStateOutput();
            //_networkContext.WriteNetworkStates(jsonStateOutput);
            //Debug.Log(jsonStateOutput.ToString());
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
            LocalPlayerGuid = clientGuid;
            _networkContext.LocalClientIdentity = clientGuid;
            _networkListener.OnConnectedToRelay();
        }

        public void OnSubscribedToTopic(string topic)
        {
            var room = new Room(this, topic, LocalPlayerGuid);
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
