using System;
using UnityEngine;

namespace BananaParty.WebSocketRelay
{
    public class NetworkIdentity : MonoBehaviour, INetworkIdentity
    {
        [SerializeField]
        NetworkContext _networkContext;

        public Guid NetworkIdentifier { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public Guid NetworkOwner { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public bool NetworkHasAuthority { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public void ReadState(IStateInput stateInput) => throw new NotImplementedException();
        public void WriteState(IStateOutput stateOutput) => throw new NotImplementedException();

        private void OnEnable()
        {
            _networkContext.RegisterNetworkIdentity(this);
        }

        private void OnDisable()
        {
            _networkContext.UnregisterNetworkIdentity(this);
        }
    }
}
