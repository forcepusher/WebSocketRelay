using System;
using UnityEngine;

namespace BananaParty.WebSocketRelay.Samples
{
    public class GameState : MonoBehaviour, INetworkListener
    {
        private Network _network;

        [SerializeField]
        private NetworkContext _networkContext;

        [SerializeField]
        private NetworkIdentity _playerCharacterPrefab;

        private void Start()
        {
            _network = new Network("ws://127.0.0.1:23144", _networkContext, this);

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
            _network.Connect();
        }

        public void OnDisconnectButtonClick()
        {
            _network.Disconnect();
        }

        public void OnConnectedToRelay()
        {
            _network.SubscribeToTopic("game-room");
        }

        public void OnSubscribedToTopic(Room room)
        {
            room.Instantiate(_playerCharacterPrefab, room.LocalPlayerGuid);
        }

        public void OnDisconnectedFromRoom(Room room)
        {

        }

        public void OnPlayerAdded(NetworkPlayer networkPlayer)
        {

        }

        public void OnPlayerRemoved(NetworkPlayer networkPlayer)
        {

        }

        public void OnConnectedToRelay(Guid clientGuid) => throw new NotImplementedException();
        public void OnSubscribedToTopic(string topic) => throw new NotImplementedException();
        public void OnUnsubscribedFromTopic(string topic) => throw new NotImplementedException();
        public void OnTopicMessage(Guid senderGuid, string topic, byte[] data) => throw new NotImplementedException();
    }
}
