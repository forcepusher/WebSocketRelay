using System.Collections.Generic;
using UnityEngine;

namespace BananaParty.WebSocketRelay.Samples
{
    public class Game : MonoBehaviour, IState
    {
        [SerializeField]
        private Character _playerCharacterState;

        [SerializeField]
        private Character _botCharacterState;

        [SerializeField]
        private List<ItemSpawn> _itemSpawns;
        private StaticArrayState<ItemSpawn> _itemSpawnsState;

        private IntegerState _playTimeState = new(nameof(_playTimeState), 0);

        private List<IState> _states;

        private void Awake()
        {
            _itemSpawnsState = new(nameof(_itemSpawns), _itemSpawns);

            _states = new List<IState>
            {
                _playTimeState,
                _playerCharacterState,
                _botCharacterState,
                _itemSpawnsState
            };
        }

        private void Start()
        {
            var jsonStateOutput = new JsonStateOutput();
            WriteState(jsonStateOutput);
            Debug.Log(jsonStateOutput.ToString());
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
