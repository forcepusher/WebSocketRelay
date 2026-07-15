using UnityEngine;

namespace BananaParty.WebSocketRelay
{
    public class AuthorityOrigin : MonoBehaviour, IAuthorityOrigin, IRpcTarget
    {
        [SerializeField]
        private NetworkContext _networkContext;

        private NetworkIdentity _networkIdentity;
        public NetworkIdentity NetworkIdentity => _networkIdentity;

        public Vector3 Position => transform.position;

        public string RpcSubjectName => nameof(AuthorityOrigin);

        private void Awake()
        {
            _networkIdentity = GetComponent<NetworkIdentity>();
        }

        private void OnEnable()
        {
            _networkContext.RegisterAuthorityOrigin(this);
            _networkContext.RegisterRpcTarget(this);
        }

        private void OnDisable()
        {
            _networkContext.UnregisterAuthorityOrigin(this);
            _networkContext.UnregisterRpcTarget(this);
        }

        private void Update()
        {

        }

        public void ReceiveRpc(IStateInput parametersStateInput)
        {

        }

        private void SendRpc()
        {
            IStateOutput parametersStateOutput = _networkContext.UseBinary ? new BinaryStateOutput() : new JsonStateOutput();
            _networkIdentity.SendRpc(RpcSubjectName, parametersStateOutput);
        }

        //// THIS HAS TO BE DELETED AFTER A REFACTOR, MOVE IT TO AuthorityOrigin
        //public AuthorityOrigin GetClosestAuthorityOrigin(Vector3 position)
        //{
        //    AuthorityOrigin closestAuthorityOrigin = null;
        //    float closestDistance = float.MaxValue;

        //    foreach (IAuthorityOrigin authorityOrigin in _authorityOrigins)
        //    {
        //        float distance = (authorityOrigin.Position - position).magnitude;
        //        if (distance >= closestDistance)
        //            continue;

        //        closestDistance = distance;
        //        closestAuthorityOrigin = (AuthorityOrigin)authorityOrigin;
        //    }

        //    return closestAuthorityOrigin;
        //}
    }
}
