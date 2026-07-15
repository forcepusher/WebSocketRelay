using System;
using System.Collections.Generic;
using UnityEngine;

namespace BananaParty.WebSocketRelay
{
    public class NetworkIdentity : MonoBehaviour, INetworkIdentity
    {
        [SerializeField]
        private NetworkContext _networkContext;
        [SerializeField]
        private string _prefabName;
        [SerializeField]
        private bool _distanceBasedAuthority;

        private readonly List<INetworkState> _networkStates = new();

        public GameObject GameObject => gameObject;
        public string PrefabName => _prefabName;
        public string Channel { get; set; }
        public Guid NetworkIdentifier { get; set; }
        public Guid NetworkOwner { get; set; }
        public IReadOnlyList<INetworkState> NetworkStates => _networkStates;
        public bool NetworkAuthority
        {
            get
            {
                //if (!_distanceBasedAuthority)
                    return _networkContext.LocalClientIdentity == NetworkOwner;

                //AuthorityOrigin closestAuthorityOrigin = _networkContext.GetClosestAuthorityOrigin(transform.position);
                //return closestAuthorityOrigin?.NetworkIdentity.NetworkOwner == _networkContext.LocalClientIdentity;
            }
        }
        public bool DistanceBasedAuthority => _distanceBasedAuthority;

        private void Awake()
        {
            _networkStates.AddRange(GetComponents<INetworkState>());
        }

        public void SendRpc(string rpcSubjectName, IStateOutput parametersStateOutput)
        {

        }

        private void OnValidate()
        {
            _prefabName = transform.name;
        }
    }
}
