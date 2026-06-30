using System.Collections.Generic;
using UnityEngine;

namespace BananaParty.WebSocketRelay
{
    public class NetworkIdentityFactory : MonoBehaviour
    {
        private List<NetworkIdentity> _networkIdentities;

        public NetworkIdentity Instantiate(string resourcePath)
        {
            GameObject gameObject = Instantiate(Resources.Load<GameObject>(resourcePath));
            NetworkIdentity networkIdentity = gameObject.GetComponent<NetworkIdentity>();

            _networkIdentities.Add(networkIdentity);

            return networkIdentity;
        }
    }
}
