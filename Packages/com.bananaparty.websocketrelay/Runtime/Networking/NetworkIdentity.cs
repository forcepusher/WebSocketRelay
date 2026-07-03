using UnityEngine;

namespace BananaParty.WebSocketRelay
{
    [DefaultExecutionOrder(-100000)]
    public class NetworkIdentity : MonoBehaviour, INetworkIdentity
    {
        [SerializeField]
        NetworkContext _networkContext;

        private void OnEnable()
        {
            _networkContext.RegisterNetworkIdentity(this);
        }

        private void OnDisable()
        {
            _networkContext.UnregisterNetworkIdentity(this);
        }
    }
}
