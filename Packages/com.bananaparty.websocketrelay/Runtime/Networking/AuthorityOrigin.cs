using System;
using UnityEngine;

namespace BananaParty.WebSocketRelay
{
    public class AuthorityOrigin : MonoBehaviour, IAuthorityOrigin, IRpcTarget
    {
        private const float AuthorityInterceptionThreshold = 0.8f;
        private const string TakeAuthorityGuidKey = nameof(TakeAuthorityGuidKey);
        private const string TakeAuthorityRequesterGuidKey = nameof(TakeAuthorityRequesterGuidKey);

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
            if (_networkIdentity.NetworkOwner != _networkContext.LocalClientIdentity)
                return;

            foreach (INetworkIdentity networkIdentity in _networkContext.NetworkIdentities)
            {
                if (!networkIdentity.DistanceBasedAuthority)
                    continue;

                if (networkIdentity.NetworkOwner == _networkContext.LocalClientIdentity)
                    continue;

                AuthorityOrigin currentOwnerAuthorityOrigin = GetAuthorityOriginForNetworkOwner(networkIdentity.NetworkOwner);
                if (currentOwnerAuthorityOrigin == null)
                {
                    if (GetClosestAuthorityOrigin(networkIdentity.GameObject.transform.position) == this)
                        TakeAuthority((NetworkIdentity)networkIdentity);

                    continue;
                }

                float currentOwnerDistance = GetDistanceTo(networkIdentity.GameObject.transform.position, currentOwnerAuthorityOrigin.Position);
                float localDistance = GetDistanceTo(networkIdentity.GameObject.transform.position, Position);

                if (localDistance > currentOwnerDistance * AuthorityInterceptionThreshold)
                    continue;

                TakeAuthority((NetworkIdentity)networkIdentity);
            }
        }

        public void ReceiveRpc(IStateInput parametersStateInput)
        {
            Guid networkIdentityToControl = parametersStateInput.ReadGuid(TakeAuthorityGuidKey);
            Guid requesterGuid = parametersStateInput.ReadGuid(TakeAuthorityRequesterGuidKey);

            foreach (INetworkIdentity networkIdentity in _networkContext.NetworkIdentities)
            {
                if (networkIdentity.NetworkIdentifier != networkIdentityToControl)
                    continue;

                networkIdentity.NetworkOwner = requesterGuid;
                return;
            }
        }

        private static float GetDistanceTo(Vector3 targetPosition, Vector3 authorityOriginPosition)
        {
            return (targetPosition - authorityOriginPosition).magnitude;
        }

        private AuthorityOrigin GetAuthorityOriginForNetworkOwner(Guid networkOwner)
        {
            foreach (IAuthorityOrigin authorityOrigin in _networkContext.AuthorityOrigins)
            {
                if (authorityOrigin.NetworkIdentity.NetworkOwner != networkOwner)
                    continue;

                return (AuthorityOrigin)authorityOrigin;
            }

            return null;
        }

        private AuthorityOrigin GetClosestAuthorityOrigin(Vector3 targetPosition)
        {
            AuthorityOrigin closestAuthorityOrigin = null;
            float closestDistance = float.MaxValue;

            foreach (IAuthorityOrigin authorityOrigin in _networkContext.AuthorityOrigins)
            {
                float distance = GetDistanceTo(targetPosition, authorityOrigin.Position);
                if (distance >= closestDistance)
                    continue;

                closestDistance = distance;
                closestAuthorityOrigin = (AuthorityOrigin)authorityOrigin;
            }

            return closestAuthorityOrigin;
        }

        private void TakeAuthority(NetworkIdentity networkIdentity)
        {
            IStateOutput parametersStateOutput = _networkContext.UseBinary ? new BinaryStateOutput() : new JsonStateOutput();
            parametersStateOutput.WriteGuid(TakeAuthorityGuidKey, networkIdentity.NetworkIdentifier);
            parametersStateOutput.WriteGuid(TakeAuthorityRequesterGuidKey, _networkContext.LocalClientIdentity);
            _networkIdentity.SendRpc(RpcSubjectName, parametersStateOutput);
        }
    }
}
