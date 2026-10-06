using System;
using System.Collections;
using BananaParty.WebSocketRelay.Transport;
using UnityEngine;

namespace BananaParty.WebSocketRelay.Samples
{
    public class GameState : MonoBehaviour
    {
        private const float SyncInterval = 0.1f;

        private Network _network;

        private string _networkChannelName = "game-room";

        private float _timeSinceLastFullSync = 0f;

        [SerializeField]
        private NetworkContext _networkContext;

        [SerializeField]
        private NetworkChannel _networkChannel;

        [SerializeField]
        private NetworkIdentity _playerCharacterPrefab;

        [SerializeField]
        private RelayConnectionSettings _connectionSettings = new();

        private void Start()
        {
            _network = new Network("ws://127.0.0.1:80", _networkContext, connectionSettings: _connectionSettings);

            //var jsonStateOutput = new JsonStateOutput();
            //WriteState(jsonStateOutput);
            //Debug.Log(jsonStateOutput.ToString());
        }

        private void Update()
        {
            if (_network == null)
                return;

            _network.ManualUpdate(Time.unscaledDeltaTime);

            if (!_network.IsConnected)
                return;

            _timeSinceLastFullSync += Time.unscaledDeltaTime;
            if (_timeSinceLastFullSync >= SyncInterval)
            {
                _timeSinceLastFullSync = 0f;
                _network.SendSyncIdentities();
            }
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
            _network.SubscribeToChannel(_networkChannelName);

            _networkChannel.SetChannel(_networkChannelName);

            _networkContext.Instantiate(_playerCharacterPrefab, _networkChannelName);
        }
    }
}
