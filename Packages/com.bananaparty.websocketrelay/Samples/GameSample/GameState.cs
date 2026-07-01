using System.Collections.Generic;
using UnityEngine;

namespace BananaParty.WebSocketRelay.Samples
{
    public class GameState : MonoBehaviour, IState
    {
        [SerializeField]
        private PlayerCharacterSpawn _playerCharacterSpawn;
        [SerializeField]
        private BotCharacterSpawn _botCharacterSpawn;
        [SerializeField]
        private List<ItemSpawn> _itemSpawns;
        private StaticArrayState<ItemSpawn> _itemSpawnsState;

        private Network _network = new("ws://127.0.0.1:23144");

        private IntegerState _playTimeState = new(nameof(_playTimeState), 0);

        private List<IState> _states;

        private void Awake()
        {
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
            var jsonStateOutput = new JsonStateOutput();
            WriteState(jsonStateOutput);
            Debug.Log(jsonStateOutput.ToString());
        }

        private void Update()
        {
            _network.ManualUpdate(Time.unscaledDeltaTime);
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
    }
}
