using System;
using System.Collections.Generic;
using UnityEngine;

namespace BananaParty.WebSocketRelay
{
    public class NetworkIdentity : MonoBehaviour, INetworkIdentity
    {
        [SerializeField]
        private NetworkContext _networkContext;

        private List<INetworkState> _networkStates = new();

        public Guid NetworkIdentifier { get; set; } = Guid.NewGuid();
        public Guid NetworkOwner { get; set; } = Guid.NewGuid();
        public bool NetworkAuthority { get; set; } = false;

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
            _networkStates.Remove(this);
        }

        public string NetworkStateName => nameof(NetworkIdentity);

        public void ReadNetworkState(IStateInput stateInput)
        {
            NetworkIdentifier = stateInput.ReadGuid(nameof(NetworkIdentifier));
            NetworkOwner = stateInput.ReadGuid(nameof(NetworkOwner));
            NetworkAuthority = stateInput.ReadBool(nameof(NetworkAuthority));

            foreach (var networkState in _networkStates)
                networkState.ReadNetworkState(stateInput);
        }

        public void WriteNetworkState(IStateOutput stateOutput)
        {
            stateOutput.WriteGuid(nameof(NetworkIdentifier), NetworkIdentifier);
            stateOutput.WriteGuid(nameof(NetworkOwner), NetworkOwner);
            stateOutput.WriteBool(nameof(NetworkAuthority), NetworkAuthority);

            foreach (var networkState in _networkStates)
                networkState.WriteNetworkState(stateOutput);
        }
    }
}
