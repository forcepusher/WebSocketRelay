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
        public bool HasRelayClient => _relayClient != null;
        public HashSet<string> SubscribedChannels => _relayClient?.SubscribedChannels;

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
            Debug.Log($"Connecting to relay server at {_serverAddress}");
        }

        public void Disconnect()
        {
            if (_relayClient == null)
                throw new InvalidOperationException("Not connected to disconnect");

            _networkContext.ClearNetworkSession();
            DisposeRelayClient();
        }

        public void ManualUpdate(float unscaledDeltaTime)
        {
            _relayClient?.ProcessIncomingMessages();
            _networkContext.ManualUpdate(unscaledDeltaTime);
            SendQueuedRpcMessages();

            //var jsonStateOutput = new JsonStateOutput();
            //_networkContext.WriteNetworkStates(jsonStateOutput);
            //Debug.Log(jsonStateOutput.ToString());
        }

        private void SendQueuedRpcMessages()
        {
            if (_relayClient == null || !_relayClient.IsConnected)
                return;

            while (_networkContext.TryDequeueOutgoingRpcMessage(out string channel, out byte[] message))
                _relayClient.Send(channel, message);
        }

        public void SendSyncIdentities()
        {
            if (_relayClient == null)
                return;

            foreach (string channel in SubscribedChannels)
            {
                byte[] payload = _networkContext.GetOwnedNetworkIdentitiesPayload(channel);
                byte[] message = new byte[payload.Length + 1];
                message[0] = NetworkMessage.SyncIdentities;
                Buffer.BlockCopy(payload, 0, message, 1, payload.Length);
                _relayClient.Send(channel, message);
            }
        }

        public void SubscribeToChannel(string channel)
        {
            if (_relayClient == null)
                throw new InvalidOperationException("Not connected to subscribe to a channel");

            _relayClient.SubscribeToChannel(channel);
        }

        public void UnsubscribeFromChannel(string channel)
        {
            if (_relayClient == null)
                throw new InvalidOperationException("Not connected to unsubscribe from a channel");

            _relayClient.UnsubscribeFromChannel(channel);
        }

        public void Dispose()
        {
            _relayServerProcess?.Stop();

            if (_relayClient != null)
            {
                _networkContext.ClearNetworkSession();
                DisposeRelayClient();
            }
        }

        public void OnConnectedToRelay()
        {
        }

        public void OnDisconnectedFromRelay()
        {
            _networkContext.ClearNetworkSession();
            DisposeRelayClient();
        }

        private void DisposeRelayClient()
        {
            if (_relayClient == null)
                return;

            _relayClient.Dispose();
            _relayClient = null;
        }

        public void OnChannelMessage(Guid senderGuid, string channel, byte[] data)
        {
            _networkContext.ProcessChannelMessage(senderGuid, channel, data);
        }
    }
}
