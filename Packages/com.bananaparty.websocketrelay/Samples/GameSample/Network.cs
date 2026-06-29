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
            _server = new Server();
            _server.Start();
            Debug.Log("Relay server started.");
        }

        public void StopServer()
        {
            _server.Stop();
            _server = null;
            Debug.Log("Relay server stopped.");
        }

        public void Connect()
        {
            _socket = new Socket(_serverAddress);
            _socket.Connect();
            Debug.Log($"Connected to relay server at {_serverAddress}");
        }

        public void Disconnect()
        {
            _socket.Disconnect();
            Debug.Log("Disconnected from relay server.");
        }

        private void OnDestroy()
        {
            if (_server == null)
                return;

            _server.Stop();
            _socket.Dispose();
        }
    }
}
