using System;
using System.Collections.Generic;
using BananaParty.WebSocketRelay.Transport;
using UnityEngine;

namespace BananaParty.WebSocketRelay
{
    public class Network : IRelayListener, IDisposable
    {
        private readonly string _serverAddress;

        private readonly Dictionary<Guid, NetworkPlayer> _guidToNetworkPlayers = new();

        private RelayServerProcess _relayServerProcess;
        private RelayClient _relayClient;

        public Network(string serverAddress)
        {
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

        public void ManualUpdate(float deltaTime)
        {
            _relayClient?.ProcessIncomingMessages();

            foreach (var networkPlayer in _guidToNetworkPlayers.Values)
                networkPlayer.ManualUpdate(deltaTime);
        }

        public void Dispose()
        {
            _relayServerProcess?.Stop();
            _relayClient?.Dispose();
        }

        public void OnConnectedToRelay(Guid clientGuid)
        {
        }

        public void OnSubscribedToTopic(string topic)
        {
        }

        public void OnUnsubscribedFtomTopic(string topic)
        {
        }

        public void OnTopicMessage(Guid senderGuid, string topic, byte[] data)
        {
            if (_guidToNetworkPlayers.ContainsKey(senderGuid))
            {
                _guidToNetworkPlayers[senderGuid].OnTopicMessage(topic, data);
            }
            else
            {
                NetworkPlayer networkPlayer = new NetworkPlayer(senderGuid);
                _guidToNetworkPlayers[senderGuid] = networkPlayer;
                networkPlayer.OnTopicMessage(topic, data);
            }
        }
    }
}
