using System;
using System.Collections.Generic;
using System.Text;
using BananaParty.WebSocketRelay.Transport;
using UnityEngine;

namespace BananaParty.WebSocketRelay.Samples
{
    public class GameState : MonoBehaviour, IState, INetworkListener
    {
        [SerializeField]
        private PlayerCharacterSpawn _playerCharacterSpawn;
        [SerializeField]
        private BotCharacterSpawn _botCharacterSpawn;
        [SerializeField]
        private List<ItemSpawn> _itemSpawns;
        private StaticArrayState<ItemSpawn> _itemSpawnsState;

        private Network _network;
        private Room _room;

        private List<IState> _states;

        private void Awake()
        {
            _network = new(this, "ws://127.0.0.1:23144");

            _itemSpawnsState = new(nameof(_itemSpawns), _itemSpawns);

            _states = new List<IState>
            {
                _playerCharacterSpawn,
                _botCharacterSpawn,
                _playTimeState,
                _itemSpawnsState
            };
        }

        private void Start()
        {
            //var jsonStateOutput = new JsonStateOutput();
            //WriteState(jsonStateOutput);
            //Debug.Log(jsonStateOutput.ToString());
        }

        private void Update()
        {
            _network.ManualUpdate(Time.unscaledDeltaTime);

            if (_room != null)
            {
                if (_room.HasUnreadPayloadQueue)
                {
                    byte[] payload = _room.ReadPayloadQueue();
                    string payloadString = UTF8Encoding.UTF8.GetString(payload);
                    Debug.Log(payloadString);
                }
            }
        }

        public string StateName => transform.name;

        public void WriteState(IStateOutput writeGraph)
        {
            writeGraph.WriteObject(StateName, _states);
        }

        public void ReadState(IStateInput readGraph)
        {
            readGraph.ReadObject(StateName, _states);
        }

        public void OnConnectedToRelay(Guid clientGuid)
        {
            _network.JoinRoom("game-room");
        }

        public void OnConnectedToRoom(Room room)
        {
            _room = room;

            var jsonStateOutput = new JsonStateOutput();
            WriteState(jsonStateOutput);
            //Debug.Log(jsonStateOutput.ToString());

            room.Send(UTF8Encoding.UTF8.GetBytes(jsonStateOutput.ToString()));
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
