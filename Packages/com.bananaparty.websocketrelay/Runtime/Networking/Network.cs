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

        private readonly List<NetworkPlayer> _networkPlayers = new();

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


            //var jsonStateOutput = new JsonStateOutput();
            //_networkContext.WriteNetworkStates(jsonStateOutput);
            //Debug.Log(jsonStateOutput.ToString());
        }

        public void Send(string topic, byte[] data)
        {
            _relayClient.Send(topic, data);
        }

        public void SubscribeToTopic(string topic)
        {
            _relayClient.SubscribeToTopic(topic);
        }

        public void UnsubscribeFromTopic(string topic)
        {
            _relayClient.UnsubscribeToTopic(topic);
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
            _networkListener.OnConnectedToRelay(clientGuid);
        }

        public void OnSubscribedToTopic(string topic)
        {
            _networkListener.OnSubscribedToTopic(topic);
        }

        public void OnUnsubscribedFromTopic(string topic)
        {
            _networkListener.OnUnsubscribedFromTopic(topic);
        }

        public void OnTopicMessage(Guid senderGuid, string topic, byte[] data)
        {
            _networkListener.OnTopicMessage(senderGuid, topic, data);
        }

        private void AddNetworkPlayer(NetworkPlayer networkPlayer)
        {
            _networkListener.OnPlayerAdded(networkPlayer);
        }

        private void RemoveNetworkPlayer(NetworkPlayer networkPlayer)
        {
            _networkListener.OnPlayerRemoved(networkPlayer);
        }
    }
}
