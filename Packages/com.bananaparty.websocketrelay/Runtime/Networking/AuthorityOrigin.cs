using UnityEngine;

namespace BananaParty.WebSocketRelay
{
    public class AuthorityOrigin : MonoBehaviour, IAuthorityOrigin
    {
        [SerializeField]
        private NetworkContext _networkContext;

        private NetworkIdentity _networkIdentity;
        public NetworkIdentity NetworkIdentity => _networkIdentity;

        public Vector3 Position => transform.position;

        private void Awake()
        {
            _networkIdentity = GetComponent<NetworkIdentity>();
        }

        private void OnEnable()
        {
            _networkContext.RegisterAuthorityOrigin(this);
        }

        private void OnDisable()
        {
            _networkContext.UnregisterAuthorityOrigin(this);
        }
    }
}
