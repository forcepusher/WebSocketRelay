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
        public string _prefabName;

        private readonly List<INetworkState> _networkStates = new();

        public GameObject GameObject => gameObject;
        public string PrefabName => _prefabName;
        public string Channel { get; set; }
        public Guid NetworkIdentifier { get; set; }
        public Guid NetworkOwner { get; set; }
        public bool NetworkAuthority => _networkContext.LocalClientIdentity == NetworkOwner;
        public NetworkContext NetworkContext => _networkContext;
        public IReadOnlyList<INetworkState> NetworkStates => _networkStates;

        private void Awake()
        {
            _networkStates.AddRange(GetComponents<INetworkState>());
        }

        private void OnValidate()
        {
            _prefabName = transform.name;
        }
    }
}
