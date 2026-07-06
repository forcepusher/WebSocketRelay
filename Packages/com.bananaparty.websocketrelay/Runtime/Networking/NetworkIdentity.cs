using System;
using System.Collections.Generic;
using UnityEngine;

namespace BananaParty.WebSocketRelay
{
    public class NetworkIdentity : MonoBehaviour, INetworkIdentity
    {
        [SerializeField]
        NetworkContext _networkContext;

        private List<INetworkState> _networkStates = new();

        public Guid NetworkIdentifier { get; set; } = Guid.NewGuid();
        public Guid NetworkOwner { get; set; } = Guid.NewGuid();
        public bool NetworkHasAuthority { get; set; } = false;

        private void OnEnable()
        {
            _networkContext.RegisterNetworkIdentity(this);
        }

        private void OnDisable()
        {
            _networkContext.UnregisterNetworkIdentity(this);
        }

        private void Awake()
        {
            _networkStates.AddRange(GetComponents<INetworkState>());

            foreach (var networkState in _networkStates)
            {
                Debug.Log(networkState.StateName);
            }
        }

        public string StateName => nameof(NetworkIdentity);

        public void ReadState(IStateInput stateInput) => throw new NotImplementedException();
        public void WriteState(IStateOutput stateOutput) => throw new NotImplementedException();
    }
}
