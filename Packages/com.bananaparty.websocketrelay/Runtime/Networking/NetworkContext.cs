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

        public void ReadNetworkStates(IStateInput stateInput)
        {
            foreach (var networkIdentity in _networkIdentities)
                networkIdentity.ReadNetworkState(stateInput);
        }

        public void WriteNetworkStates(IStateOutput stateOutput)
        {
            stateOutput.BeginArrayElement();
            foreach (var networkIdentity in _networkIdentities)
            {
                stateOutput.BeginObjectElement();
                networkIdentity.WriteNetworkState(stateOutput);
                stateOutput.EndObject();
            }
            stateOutput.EndArray();
        }
    }
}
