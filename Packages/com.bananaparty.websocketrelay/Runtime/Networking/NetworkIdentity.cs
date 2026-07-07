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

        private List<INetworkState> _networkStates = new();

        public string PrefabName => _prefabName;
        public Guid NetworkIdentifier { get; set; }
        public Guid NetworkOwner { get; set; }
        public bool NetworkAuthority => _networkContext.LocalClientIdentity == NetworkOwner;

        private void OnEnable()
        {
            _networkContext.RegisterNetworkIdentity(this);
        }

        private void OnDisable()
        {
            _networkContext.UnregisterNetworkIdentity(this);
        }

        private void Awake()
        {
            _networkStates.AddRange(GetComponents<INetworkState>());
            _networkStates.Remove(this);
        }

        public string NetworkStateName => nameof(NetworkIdentity);

        public void ReadNetworkState(IStateInput stateInput)
        {
            string prefabName = stateInput.ReadString(nameof(PrefabName));
            if (prefabName != PrefabName)
                throw new InvalidOperationException($"Prefab name mismatch. Expected: {PrefabName}, Received: {prefabName}");

            NetworkOwner = stateInput.ReadGuid(nameof(NetworkOwner));
            ReadNetworkStateBody(stateInput);
        }

        public void ReadNetworkStateBody(IStateInput stateInput)
        {
            stateInput.BeginArrayProperty("NetworkStates");
            foreach (INetworkState networkState in _networkStates)
            {
                stateInput.BeginObjectElement();
                networkState.ReadNetworkState(stateInput);
                stateInput.EndObject();
            }
            stateInput.EndArray();
        }

        public void WriteNetworkState(IStateOutput stateOutput)
        {
            stateOutput.WriteString(nameof(PrefabName), PrefabName);
            stateOutput.WriteGuid(nameof(NetworkOwner), NetworkOwner);

            stateOutput.BeginArrayProperty("NetworkStates");
            foreach (INetworkState networkState in _networkStates)
            {
                stateOutput.BeginObjectElement();
                networkState.WriteNetworkState(stateOutput);
                stateOutput.EndObject();
            }
            stateOutput.EndArray();
        }

        private void OnValidate()
        {
            _prefabName = transform.name;
        }
    }
}
