using System;
using UnityEngine;

namespace BananaParty.WebSocketRelay.Samples
{
    public class Network : MonoBehaviour
    {
        private readonly string _serverAddress = "ws://127.0.0.1:23144";
        private Server _server;
        private RelayConnection _connection;

        public void StartServer()
        {
            if (_server != null)
                throw new InvalidOperationException("Server already running");

            _server = new Server();
            _server.Start();
            Debug.Log("Relay server started.");
        }

        public void StopServer()
        {
            if (_server == null)
                throw new InvalidOperationException("Server not started to stop it");

            _server.Stop();
            _server = null;
            Debug.Log("Relay server stopped.");
        }

        public void Connect()
        {
            if (_connection != null)
                throw new InvalidOperationException("Already connected");

            _connection = new RelayConnection(_serverAddress);
            _connection.Connect();
            Debug.Log($"Connected to relay server at {_serverAddress}");
        }

        public void Disconnect()
        {
            if (_connection == null)
                throw new InvalidOperationException("Not connected to disconnect");

            _connection.Dispose();
            _connection = null;
            Debug.Log("Disconnected from relay server.");
        }

        private void Update()
        {
            _connection?.ProcessIncomingMessages();
        }

        private void OnDestroy()
        {
            _server?.Stop();
            _connection?.Dispose();
        }
    }
}
