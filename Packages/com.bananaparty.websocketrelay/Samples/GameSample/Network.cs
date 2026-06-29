using System;
using UnityEngine;

namespace BananaParty.WebSocketRelay.Samples
{
    public class Network : MonoBehaviour
    {
        private readonly string _serverAddress = "ws://127.0.0.1:23144";
        private Server _server;
        private Socket _socket;

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
            if (_socket != null)
                throw new InvalidOperationException("Socket already running");

            _socket = new Socket(_serverAddress);
            _socket.Connect();
            Debug.Log($"Connected to relay server at {_serverAddress}");
        }

        public void Disconnect()
        {
            if (_socket == null)
                throw new InvalidOperationException("Socket not connected to disconect it");

            _socket.Disconnect();
            _socket = null;
            Debug.Log("Disconnected from relay server.");
        }

        private void OnDestroy()
        {
            _server?.Stop();
            _socket?.Dispose();
        }
    }
}
