using System.Collections.Generic;
using UnityEngine;

namespace BananaParty.WebSocketRelay
{
    public class NetworkContext : ScriptableObject, INetworkContext
    {
        private List<INetworkIdentity> _networkIdentities;

        public void RegisterNetworkIdentity(INetworkIdentity networkIdentity)
        {
            _networkIdentities.Add(networkIdentity);
        }

        public void UnregisterNetworkIdentity(INetworkIdentity networkIdentity)
        {
            _networkIdentities.Remove(networkIdentity);
        }
    }
}
