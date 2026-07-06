using System;
using BananaParty.WebSocketRelay.Transport;
using UnityEngine;

namespace BananaParty.WebSocketRelay.Samples
{
    public class GameState : MonoBehaviour
    {
        [SerializeField]
        private Network _network;

        private Room _room;

        private void Start()
        {
            _network.OnConnectedToRelayEvent.AddListener(OnConnectedToRelay);
            _network.OnConnectedToRoomEvent.AddListener(OnConnectedToRoom);
            _network.OnDisconnectedFromRoomEvent.AddListener(OnDisconnectedFromRoom);
            _network.OnRoomPlayerAddedEvent.AddListener(OnRoomPlayerAdded);
            _network.OnRoomPlayerRemovedEvent.AddListener(OnRoomPlayerRemoved);

            //var jsonStateOutput = new JsonStateOutput();
            //WriteState(jsonStateOutput);
            //Debug.Log(jsonStateOutput.ToString());
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
