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
        public bool NetworkAuthority => _networkContext.LocalClientIdentity == NetworkOwner;

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
            NetworkOwner = stateInput.ReadGuid(nameof(NetworkOwner));

            stateInput.BeginObjectProperty("NetworkStates");
            foreach (INetworkState networkState in _networkStates)
            {
                if (!stateInput.TryBeginObjectProperty(networkState.NetworkStateName))
                    continue;

                networkState.ReadNetworkState(stateInput);
                stateInput.EndObject();
            }
            stateInput.EndObject();
        }

        public void WriteNetworkState(IStateOutput stateOutput)
        {
            stateOutput.WriteGuid(nameof(NetworkOwner), NetworkOwner);

            stateOutput.BeginObjectProperty("NetworkStates");
            foreach (INetworkState networkState in _networkStates)
            {
                stateOutput.BeginObjectProperty(networkState.NetworkStateName);
                networkState.WriteNetworkState(stateOutput);
                stateOutput.EndObject();
            }
            stateOutput.EndObject();
        }
    }
}
