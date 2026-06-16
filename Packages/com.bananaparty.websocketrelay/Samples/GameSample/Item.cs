using System;
using System.Collections.Generic;
using UnityEngine;

namespace BananaParty.WebSocketRelay.Samples
{
    public class Item : MonoBehaviour, IKeyedState
    {
        public GuidState StateKey { get; } = new(nameof(StateKey), Guid.Empty);
        public readonly FloatState TimeToDisappear = new(nameof(TimeToDisappear), 5f);
        private List<IState> _states;

        public string StateName => transform.name;

        private void Awake()
        {
            _states = new List<IState> { StateKey, TimeToDisappear };
        }

        public void WriteState(IStateOutput stateOutput) => stateOutput.WriteObject(StateName, _states);

        public void ReadState(IStateInput stateInput) => stateInput.ReadObject(StateName, _states);
    }
}
