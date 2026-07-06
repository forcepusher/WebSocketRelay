using System;
using System.Collections.Generic;
using UnityEngine;

namespace BananaParty.WebSocketRelay
{
    [CreateAssetMenu]
    public class NetworkContext : ScriptableObject, INetworkContext
    {
        public Guid LocalClientIdentity { get; set; }

        private readonly List<INetworkIdentity> _networkIdentities = new();

        public void RegisterNetworkIdentity(INetworkIdentity networkIdentity)
        {
            Debug.Log("Added " + networkIdentity.NetworkStateName + " to network context");
            _networkIdentities.Add(networkIdentity);
        }

        public void UnregisterNetworkIdentity(INetworkIdentity networkIdentity)
        {
            _networkIdentities.Remove(networkIdentity);
        }

        public void ReadStates(IStateInput stateInput)
        {
            foreach (var networkIdentity in _networkIdentities)
                networkIdentity.ReadNetworkState(stateInput);
        }

        public void WriteStates(IStateOutput stateOutput)
        {
            foreach (var networkIdentity in _networkIdentities)
            {
                Debug.Log("Writing state for " + networkIdentity.NetworkStateName + " to network context");
                networkIdentity.WriteNetworkState(stateOutput);
            }
        }
    }
}
