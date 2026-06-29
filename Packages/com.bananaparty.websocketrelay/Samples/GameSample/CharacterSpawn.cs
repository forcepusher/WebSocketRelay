using System;
using UnityEngine;

namespace BananaParty.WebSocketRelay.Samples
{
    public class CharacterSpawn : IState, IFactory<Character>
    {
        public string StateName => nameof(CharacterSpawn);

        public Character Create(Guid key) => throw new NotImplementedException();
        public void Dispose(Character entry) => throw new NotImplementedException();

        public void ReadState(IStateInput stateInput) => throw new NotImplementedException();
        public void WriteState(IStateOutput stateOutput) => throw new NotImplementedException();
    }
}
