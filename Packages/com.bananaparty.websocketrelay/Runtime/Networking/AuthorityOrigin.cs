using System;
using UnityEngine;

namespace BananaParty.WebSocketRelay
{
    /// <summary>
    /// Claims authority over identities with distance based authority that are closer to it than to their current owner.
    /// </summary>
    public class AuthorityOrigin : MonoBehaviour, IAuthorityOrigin
    {
        [SerializeField]
        [Range(0.1f, 0.9f)]
        private float _authorityInterceptionDistanceThreshold = 0.5f;

        [SerializeField]
        private NetworkContext _networkContext;

        public NetworkIdentity NetworkIdentity { get; private set; }

        public Vector3 Position => transform.position;

        private void Awake()
        {
            NetworkIdentity = GetComponent<NetworkIdentity>();
        }

        private void OnEnable()
        {
            _networkContext.RegisterAuthorityOrigin(this);
        }

        private void OnDisable()
        {
            _networkContext.UnregisterAuthorityOrigin(this);
        }

        private void Update()
        {
            if (NetworkIdentity.NetworkAuthorityOwner != _networkContext.LocalClientIdentity)
                return;

            // Positions of other players are stale while interrupted, and claims would only be delivered after reconnecting.
            if (_networkContext.IsConnectionInterrupted)
                return;

            foreach (INetworkIdentity networkIdentity in _networkContext.NetworkIdentities)
            {
                if (!networkIdentity.DistanceBasedAuthority || networkIdentity.NetworkAuthorityOwner == _networkContext.LocalClientIdentity)
                    continue;

                if (ShouldClaim(networkIdentity.GameObject.transform.position, networkIdentity.NetworkAuthorityOwner))
                    networkIdentity.ClaimAuthority();
            }
        }

        private bool ShouldClaim(Vector3 targetPosition, Guid currentAuthorityOwner)
        {
            IAuthorityOrigin currentAuthorityOwnerOrigin = FindAuthorityOrigin(currentAuthorityOwner);

            // Without an origin to compare against, the closest origin takes it.
            if (currentAuthorityOwnerOrigin == null)
                return ReferenceEquals(FindClosestAuthorityOrigin(targetPosition), this);

            float currentAuthorityOwnerDistance = Vector3.Distance(targetPosition, currentAuthorityOwnerOrigin.Position);
            float localDistance = Vector3.Distance(targetPosition, Position);
            return localDistance <= currentAuthorityOwnerDistance * _authorityInterceptionDistanceThreshold;
        }

        private IAuthorityOrigin FindAuthorityOrigin(Guid networkAuthorityOwner)
        {
            foreach (IAuthorityOrigin authorityOrigin in _networkContext.AuthorityOrigins)
            {
                if (authorityOrigin.NetworkIdentity.NetworkAuthorityOwner == networkAuthorityOwner)
                    return authorityOrigin;
            }

            return null;
        }

        private IAuthorityOrigin FindClosestAuthorityOrigin(Vector3 targetPosition)
        {
            IAuthorityOrigin closestAuthorityOrigin = null;
            float closestDistance = float.MaxValue;

            foreach (IAuthorityOrigin authorityOrigin in _networkContext.AuthorityOrigins)
            {
                float distance = Vector3.Distance(targetPosition, authorityOrigin.Position);
                if (distance >= closestDistance)
                    continue;

                closestDistance = distance;
                closestAuthorityOrigin = authorityOrigin;
            }

            return closestAuthorityOrigin;
        }
    }
}
