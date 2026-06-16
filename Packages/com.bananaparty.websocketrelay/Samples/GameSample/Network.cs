using UnityEngine;
using System.IO;
using BananaParty.WebSocketRelay;

namespace BananaParty.WebSocketRelay.Samples
{
    public class Network : MonoBehaviour
    {
        [SerializeField] private string serverAddress = "ws://127.0.0.1:23144";
        private Server _server;
        private Socket _socket;

        public void StartServer()
        {
            _server = new Server();
            _server.Start();
            Debug.Log("Relay server started.");
        }

        public void StopServer()
        {
            _server?.Stop();
            _server = null;
            Debug.Log("Relay server stopped.");
        }

        public void StartClient()
        {
            _socket = new Socket(serverAddress);
            _socket.Connect();
            Debug.Log($"Connected to relay server at {serverAddress}");
        }

        public void StopClient()
        {
            _socket?.Dispose();
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
