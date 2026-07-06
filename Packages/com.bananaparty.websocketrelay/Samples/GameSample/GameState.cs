using System;
using BananaParty.WebSocketRelay.Transport;
using UnityEngine;

namespace BananaParty.WebSocketRelay.Samples
{
    public class GameState : MonoBehaviour, INetworkListener
    {
        private Network _network;

        private Room _room;

        [SerializeField]
        private NetworkContext _networkContext;

        private void Start()
        {
            _network = new Network("ws://127.0.0.1:23144", _networkContext, this);
            _network.Connect();

            //var jsonStateOutput = new JsonStateOutput();
            //WriteState(jsonStateOutput);
            //Debug.Log(jsonStateOutput.ToString());
        }

        private void Update()
        {
            _network?.ManualUpdate(Time.unscaledDeltaTime);
        }

        public void OnConnectedToRelay(Guid clientGuid)
        {
            _network.JoinRoom("game-room");
        }

        public void OnConnectedToRoom(Room room)
        {
            _room = room;
        }

        public void OnDisconnectedFromRoom(Room room)
        {
            _room = null;
        }

        public void OnRoomPlayerAdded(NetworkPlayer networkPlayer)
        {

        }

        public void OnRoomPlayerRemoved(NetworkPlayer networkPlayer)
        {

        }
    }
}
