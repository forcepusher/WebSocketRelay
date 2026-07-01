using System.Collections.Generic;
using UnityEngine;

namespace BananaParty.WebSocketRelay
{
    public class NetworkIdentityFactory : MonoBehaviour, INetworkIdentityFactory
    {
        private List<NetworkIdentity> _networkIdentities;

        public NetworkIdentity Instantiate(string resourcePath, string topic)
        {
            GameObject gameObject = Instantiate(Resources.Load<GameObject>(resourcePath));
            NetworkIdentity networkIdentity = gameObject.GetComponent<NetworkIdentity>();

            networkIdentity.SetTopic(topic);
            _networkIdentities.Add(networkIdentity);

            return networkIdentity;
        }

        public void Destroy(NetworkIdentity networkIdentity)
        {
            _networkIdentities.Remove(networkIdentity);
            GameObject.Destroy(networkIdentity.gameObject);
        }
    }
}
