using System;
using System.Collections.Generic;
using UnityEngine;

namespace BananaParty.WebSocketRelay
{
    public class NetworkIdentity : MonoBehaviour, INetworkIdentity, IRpcTarget
    {
        private const string ClaimAuthorityVersionKey = nameof(ClaimAuthorityVersionKey);

        [SerializeField]
        private NetworkContext _networkContext;
        [SerializeField]
        private string _prefabName;
        [SerializeField]
        private bool _distanceBasedAuthority;
        [SerializeField]
        [Tooltip("Ignored for scene objects with a NetworkBinding, which cannot be spawned again and only lose their owner.")]
        private bool _destroyWhenAuthorityOwnerLeaves = true;

        private readonly List<INetworkState> _networkStates = new();

        public GameObject GameObject => gameObject;
        public string PrefabName => _prefabName;
        public string Channel { get; set; }
        public Guid NetworkIdentifier { get; set; }
        public Guid NetworkAuthorityOwner { get; set; }

        // Incremented on every authority claim. Lets receivers discard in-flight ownership
        // and component state written by a previous owner after an authority transfer.
        public long NetworkAuthorityVersion { get; set; }
        public bool NetworkAuthority => _networkContext.LocalClientIdentity == NetworkAuthorityOwner;
        public bool HasAuthorityOwner => NetworkAuthorityOwner != Guid.Empty;

        public bool DistanceBasedAuthority => _distanceBasedAuthority;
        public bool DestroyWhenAuthorityOwnerLeaves => _destroyWhenAuthorityOwnerLeaves && !IsSceneBound;

        /// <summary>
        /// Set by <see cref="NetworkBinding"/> for identities placed in a scene rather than spawned from a prefab.
        /// </summary>
        public bool IsSceneBound { get; internal set; }
        public NetworkContext NetworkContext => _networkContext;

        public string RpcSubjectName => nameof(ClaimAuthority);

        INetworkIdentity IRpcTarget.NetworkIdentity => this;

        private void Awake()
        {
            GetComponents(_networkStates);
        }

        public void WriteNetworkState(IStateOutput stateOutput)
        {
            stateOutput.WriteString(nameof(PrefabName), PrefabName);
            stateOutput.WriteLong(nameof(NetworkAuthorityVersion), NetworkAuthorityVersion);

            stateOutput.BeginArrayProperty("NetworkStates");
            foreach (INetworkState networkState in _networkStates)
            {
                stateOutput.BeginObjectElement();
                networkState.WriteNetworkState(stateOutput);
                stateOutput.EndObject();
            }
            stateOutput.EndArray();
        }

        public bool ReadNetworkState(IStateInput stateInput, Guid senderGuid)
        {
            // Only the authority owner syncs an identity, so the state makes its sender the owner.
            // Authority is applied first so a client that missed a ClaimAuthority RPC
            // still converges on the owner through the owner's state broadcasts.
            // State written before the latest known authority claim, e.g. the previous owner's
            // in-flight broadcasts right after a transfer, is ignored. The state input is
            // per-identity, so abandoning it mid-object is safe.
            if (!ReadNetworkAuthority(stateInput, senderGuid))
                return false;

            ReadComponentStates(stateInput);
            return true;
        }

        internal void ReadComponentStates(IStateInput stateInput)
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

        private bool ReadNetworkAuthority(IStateInput stateInput, Guid senderGuid)
        {
            string prefabName = stateInput.ReadString(nameof(PrefabName));
            if (prefabName != PrefabName)
                throw new InvalidOperationException($"Prefab name mismatch. Expected: {PrefabName}, Received: {prefabName}");

            long networkAuthorityVersion = stateInput.ReadLong(nameof(NetworkAuthorityVersion));
            return TryApplyAuthority(senderGuid, networkAuthorityVersion);
        }

        private bool TryApplyAuthority(Guid networkAuthorityOwner, long networkAuthorityVersion)
        {
            if (networkAuthorityVersion < NetworkAuthorityVersion)
                return false;

            // Concurrent claims can produce the same version with different owners.
            // The smaller owner guid wins so every client converges on the same owner.
            if (networkAuthorityVersion == NetworkAuthorityVersion
                && networkAuthorityOwner != NetworkAuthorityOwner
                && networkAuthorityOwner.CompareTo(NetworkAuthorityOwner) > 0)
                return false;

            NetworkAuthorityOwner = networkAuthorityOwner;
            NetworkAuthorityVersion = networkAuthorityVersion;
            return true;
        }

        public void SendRpc(string rpcSubjectName, IStateOutput parametersStateOutput, bool invokeLocally = true, bool reliable = false)
        {
            _networkContext.SendRpc(NetworkIdentifier, rpcSubjectName, parametersStateOutput, Channel, invokeLocally, reliable);
        }

        public void ClaimAuthority()
        {
            IStateOutput parametersStateOutput = _networkContext.StateFormat.CreateOutput();
            parametersStateOutput.WriteLong(ClaimAuthorityVersionKey, NetworkAuthorityVersion + 1);
            SendRpc(RpcSubjectName, parametersStateOutput);
        }

        // A claim is always for its sender, so no client can claim an identity for another.
        public void ReceiveRpc(Guid senderGuid, IStateInput parametersStateInput)
        {
            long claimVersion = parametersStateInput.ReadLong(ClaimAuthorityVersionKey);
            TryApplyAuthority(senderGuid, claimVersion);
        }

        private void OnValidate()
        {
            _prefabName = transform.name;
        }
    }
}
