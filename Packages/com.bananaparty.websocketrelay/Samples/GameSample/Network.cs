using UnityEngine;
using System.IO;
using BananaParty.WebSocketRelay;

namespace BananaParty.WebSocketRelay.Samples
{
    public class Network : MonoBehaviour
    {
        [SerializeField] private string serverAddress = "ws://127.0.0.1:23144";
        private Socket _socket;

        public void StartServer()
        {
            var server = new Server();
            server.Start();
            Debug.Log("Relay server started.");
        }

        public void StartClient()
        {
            _socket = new Socket(serverAddress);
            _socket.Connect();
            Debug.Log($"Connected to relay server at {serverAddress}");
        }

        private void OnDestroy()
        {
            _socket?.Dispose();
        }
    }
}
