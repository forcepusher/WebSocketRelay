using System;
using System.Collections.Generic;
using UnityEngine;

namespace BananaParty.WebSocketRelay.Samples
{
    public class BotCharacterSpawn : MonoBehaviour, IState, IFactory<Character>
    {
        [SerializeField]
        private Character _botCharacterPrefab;

        private readonly List<Character> _characters = new();
        private DynamicArrayState<Character> _charactersState;
        private List<IState> _states;

        public string StateName => transform.name;

        private void Awake()
        {
            _charactersState = new(nameof(_charactersState), _characters, this);
            _states = new List<IState> { _charactersState };
        }

        public Character Create(Guid key)
        {
            Character character = Instantiate(_botCharacterPrefab, transform);
            character.StateKey.Value = key;

            return character;
        }

        public void Dispose(Character character) => Destroy(character.gameObject);

        public void WriteState(IStateOutput stateOutput) => stateOutput.WriteObject(StateName, _states);

        public void ReadState(IStateInput stateInput) => stateInput.ReadObject(StateName, _states);
    }
}
