using System;
using UnityEngine;

namespace BananaParty.WebSocketRelay
{
    public class Network : MonoBehaviour, IRelayListener
    {
        [SerializeField]
        private string _serverAddress = "ws://127.0.0.1:23144";

        private RelayServerProcess _relayServerProcess;
        private RelayConnection _relayConnection;

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
            if (_relayConnection != null)
                throw new InvalidOperationException("Already connected");

            _relayConnection = new RelayConnection(_serverAddress, this);
            _relayConnection.Connect();
            Debug.Log($"Connected to relay server at {_serverAddress}");
        }

        public void Disconnect()
        {
            if (_relayConnection == null)
                throw new InvalidOperationException("Not connected to disconnect");

            _relayConnection.Dispose();
            _relayConnection = null;
            Debug.Log("Disconnected from relay server.");
        }

        private void Update()
        {
            _relayConnection?.ProcessIncomingMessages();
        }

        private void OnDestroy()
        {
            _relayServerProcess?.Stop();
            _relayConnection?.Dispose();
        }

        public void ProcessConnected(Guid clientGuid)
        {
        }

        public void ProcessSubscribed(string topic)
        {
        }

        public void ProcessUnsubscribed(string topic)
        {
        }

        public void ProcessTopicMessage(Guid senderGuid, string topic, byte[] data)
        {
        }
    }
}
