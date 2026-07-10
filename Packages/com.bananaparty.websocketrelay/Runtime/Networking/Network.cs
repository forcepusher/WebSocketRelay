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

        private RelayServerProcess _relayServerProcess;
        private RelayClient _relayClient;

        public bool IsConnected => _relayClient?.IsConnected ?? false;
        public HashSet<string> SubscribedTopics => _relayClient.SubscribedTopics;

        public Network(string address, NetworkContext context)
        {
            _serverAddress = address;
            _networkContext = context;
        }

        public void StartServer()
        {
            if (_relayServerProcess != null)
                throw new InvalidOperationException("Server already running");

            _relayServerProcess = new RelayServerProcess();
            _relayServerProcess.Start();
        }

        public void StopServer()
        {
            if (_relayServerProcess == null)
                throw new InvalidOperationException("Server not started to stop it");

            _relayServerProcess.Stop();
            _relayServerProcess = null;
            Debug.Log("Relay server stopped.");
        }

        public void Connect(Guid clientGuid)
        {
            if (_relayClient != null)
                throw new InvalidOperationException("Already connected");

            _networkContext.LocalClientIdentity = clientGuid;

            _relayClient = new RelayClient(_serverAddress, this, clientGuid);
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
            _networkContext.ManualUpdate(unscaledDeltaTime);

            //var jsonStateOutput = new JsonStateOutput();
            //_networkContext.WriteNetworkStates(jsonStateOutput);
            //Debug.Log(jsonStateOutput.ToString());
        }

        public void SendSyncIdentities()
        {
            foreach (string topic in SubscribedTopics)
            {
                byte[] payload = _networkContext.GetOwnedNetworkIdentitiesPayload(topic);
                byte[] message = new byte[payload.Length + 1];
                message[0] = NetworkMessage.SyncIdentities;
                Buffer.BlockCopy(payload, 0, message, 1, payload.Length);
                _relayClient.Send(topic, message);
            }
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

        public void OnConnectedToRelay()
        {
        }

        public void OnTopicMessage(Guid senderGuid, string topic, byte[] data)
        {
            _networkContext.ProcessTopicMessage(senderGuid, topic, data);
        }
    }
}
