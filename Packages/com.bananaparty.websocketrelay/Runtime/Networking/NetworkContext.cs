using System.Collections.Generic;
using UnityEngine;

namespace BananaParty.WebSocketRelay
{
    [CreateAssetMenu]
    public class NetworkContext : ScriptableObject, INetworkContext
    {
        private List<INetworkIdentity> _networkIdentities = new();

        public void RegisterNetworkIdentity(INetworkIdentity networkIdentity)
        {
            _networkIdentities.Add(networkIdentity);
        }

        public void UnregisterNetworkIdentity(INetworkIdentity networkIdentity)
        {
            _networkIdentities.Remove(networkIdentity);
        }

        public void WriteStatesToJson(IStateOutput stateOutput)
        {
            var jsonStateOutput = new JsonStateOutput();

            foreach (var networkIdentity in _networkIdentities)
                networkIdentity.WriteState(jsonStateOutput);
        }

        public void WriteStatesToBinary(IStateOutput stateOutput)
        {
            var binaryStateOutput = new BinaryStateOutput();

            foreach (var networkIdentity in _networkIdentities)
                networkIdentity.WriteState(binaryStateOutput);
        }
    }
}
