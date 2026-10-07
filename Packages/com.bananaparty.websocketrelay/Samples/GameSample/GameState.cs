using System;
using System.Collections;
using BananaParty.WebSocketRelay.Transport;
using UnityEngine;

namespace BananaParty.WebSocketRelay.Samples
{
    public class GameState : MonoBehaviour
    {
        private const float SyncIntervalSeconds = 0.1f;
        private const string ChannelName = "game-room";

        [SerializeField]
        private string _serverAddress = "ws://127.0.0.1:80";

        [SerializeField]
        private NetworkContext _networkContext;

        [SerializeField]
        private NetworkChannel _networkChannel;

        [SerializeField]
        private NetworkIdentity _playerCharacterPrefab;

        [SerializeField]
        private RelayConnectionSettings _connectionSettings = new();

        private Network _network;
        private float _timeSinceLastSync;

        private void Start()
        {
            _network = new Network(_serverAddress, _networkContext, connectionSettings: _connectionSettings);
        }

        private void Update()
        {
            _network.ManualUpdate(Time.unscaledDeltaTime);

            if (!_network.IsConnected)
                return;

            _timeSinceLastSync += Time.unscaledDeltaTime;
            if (_timeSinceLastSync < SyncIntervalSeconds)
                return;

            _timeSinceLastSync = 0f;
            _network.SendSyncIdentities();
        }

        private void OnDestroy()
        {
            _network?.Dispose();
        }

        public void OnStartServerButtonClick()
        {
            _network.StartServer();
        }

        public void OnStopServerButtonClick()
        {
            _network.StopServer();
        }

        public void OnConnectButtonClick()
        {
            StartCoroutine(ConnectCoroutine());
        }

        public void OnDisconnectButtonClick()
        {
            _network.Disconnect();
        }

        private IEnumerator ConnectCoroutine()
        {
            _network.Connect(Guid.NewGuid());

            // Update polls the network, which connects or gives up after the connect timeout on its own.
            while (_network.ConnectionState == RelayConnectionState.Connecting)
                yield return null;

            if (!_network.IsConnected)
            {
                if (_network.HasRelayClient)
                    _network.Disconnect();

                yield break;
            }

            // Subscriptions, owned identities and the client GUID survive reconnects, so this runs only once.
            _network.SubscribeToChannel(ChannelName);
            _networkChannel.SetChannel(ChannelName);
            _networkContext.Instantiate(_playerCharacterPrefab, ChannelName);
        }
    }
}
