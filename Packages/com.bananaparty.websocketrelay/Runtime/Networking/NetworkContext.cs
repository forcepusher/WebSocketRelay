using System;
using System.Collections.Generic;
using UnityEngine;

namespace BananaParty.WebSocketRelay
{
    [CreateAssetMenu]
    public class NetworkContext : ScriptableObject, INetworkContext
    {
        [SerializeField]
        private List<NetworkIdentity> _networkPrefabs;

        public Guid LocalClientIdentity { get; set; }

        private readonly List<INetworkIdentity> _networkIdentities = new();
        private readonly Dictionary<Guid, INetworkIdentity> _networkIdentitiesByGuid = new();

        public void RegisterNetworkIdentity(INetworkIdentity networkIdentity)
        {
            Debug.Log("Added " + networkIdentity.NetworkStateName + " to network context");
            _networkIdentities.Add(networkIdentity);
            _networkIdentitiesByGuid[networkIdentity.NetworkIdentifier] = networkIdentity;
        }

        public void UnregisterNetworkIdentity(INetworkIdentity networkIdentity)
        {
            _networkIdentities.Remove(networkIdentity);
            _networkIdentitiesByGuid.Remove(networkIdentity.NetworkIdentifier);
        }

        public void ReadNetworkStates(IStateInput stateInput)
        {
            stateInput.BeginObjectElement();
            foreach (INetworkIdentity networkIdentity in _networkIdentities)
            {
                string identityKey = networkIdentity.NetworkIdentifier.ToString();
                if (!stateInput.TryBeginObjectProperty(identityKey))
                    continue;

                networkIdentity.ReadNetworkState(stateInput);
                stateInput.EndObject();
            }
            stateInput.EndObject();
        }

        public void WriteNetworkStates(IStateOutput stateOutput)
        {
            stateOutput.BeginObjectElement();
            foreach (INetworkIdentity networkIdentity in _networkIdentities)
            {
                stateOutput.BeginObjectProperty(networkIdentity.NetworkIdentifier.ToString());
                networkIdentity.WriteNetworkState(stateOutput);
                stateOutput.EndObject();
            }
            stateOutput.EndObject();
        }
    }
}
