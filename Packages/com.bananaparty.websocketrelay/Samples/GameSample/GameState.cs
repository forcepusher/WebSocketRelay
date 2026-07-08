using System;
using System.Collections;
using UnityEngine;

namespace BananaParty.WebSocketRelay.Samples
{
    public class GameState : MonoBehaviour
    {
        private Network _network;

        [SerializeField]
        private NetworkContext _networkContext;

        [SerializeField]
        private NetworkIdentity _playerCharacterPrefab;

        private void Start()
        {
            _network = new Network("ws://127.0.0.1:23144", _networkContext);

            //var jsonStateOutput = new JsonStateOutput();
            //WriteState(jsonStateOutput);
            //Debug.Log(jsonStateOutput.ToString());
        }

        private void Update()
        {
            _network?.ManualUpdate(Time.unscaledDeltaTime);
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
            StartCoroutine(ConnectCoroutine(5f));
        }

        public void OnDisconnectButtonClick()
        {
            _network.Disconnect();
        }

        private IEnumerator ConnectCoroutine(float connectionTimeout)
        {
            float elapsed = 0;
            _network.Connect();

            while (!_network.IsConnected)
            {
                elapsed += Time.unscaledDeltaTime;
                if (elapsed > connectionTimeout)
                {
                    Debug.LogError($"Connection timed out after {connectionTimeout}s");
                    yield break;
                }
                yield return null;
            }

            Debug.Log("Connected to relay");

            _network.SubscribeToTopic("game-room");

            while (!_network.SubscribedTopics.Contains("game-room"))
            {
                elapsed += Time.unscaledDeltaTime;
                if (elapsed > connectionTimeout)
                {
                    Debug.LogError($"Subscription timed out after {connectionTimeout}s");
                    yield break;
                }
                yield return null;
            }

            Debug.Log("Subscribed to game-room");

            _networkContext.Instantiate(_playerCharacterPrefab);
        }
    }
}
