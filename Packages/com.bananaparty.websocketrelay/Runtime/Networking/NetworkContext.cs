using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace BananaParty.WebSocketRelay
{
    [CreateAssetMenu]
    public class NetworkContext : ScriptableObject, INetworkContext
    {
        [SerializeField]
        private float _playerTimeoutSeconds = 5f;

        [SerializeField]
        private bool _useBinary = false;

        [SerializeField]
        private List<NetworkIdentity> _networkPrefabs;

        public bool UseBinary => _useBinary;

        public Guid LocalClientIdentity { get; set; }

        private readonly List<INetworkIdentity> _networkIdentities = new();
        private readonly Dictionary<Guid, INetworkIdentity> _networkIdentitiesByGuid = new();

        private readonly List<IAuthorityOrigin> _authorityOrigins = new();

        private readonly List<NetworkPlayer> _networkPlayers = new();
        private readonly Dictionary<Guid, NetworkPlayer> _networkPlayersByGuid = new();

        private readonly Dictionary<Guid, List<IRpcTarget>> _rpcTargetsByIdentity = new();

        private readonly Queue<(string channel, byte[] message)> _outgoingRpcMessages = new();

        public IReadOnlyList<INetworkIdentity> NetworkIdentities => _networkIdentities;

        public IReadOnlyList<IAuthorityOrigin> AuthorityOrigins => _authorityOrigins;

        public NetworkIdentity Instantiate(NetworkIdentity networkIdentityPrefab, string channel)
        {
            return Instantiate(networkIdentityPrefab.PrefabName, channel, Guid.NewGuid(), LocalClientIdentity);
        }

        private NetworkIdentity Instantiate(string prefabName, string channel, Guid networkIdentifier, Guid networkOwner)
        {
            NetworkIdentity prefab = _networkPrefabs.Find(networkPrefab => networkPrefab.PrefabName == prefabName);
            if (prefab == null)
                throw new InvalidOperationException($"No network prefab registered with name {prefabName}");

            // Instantiate deactivated so Awake/OnEnable run after identity fields are assigned,
            // otherwise components register themselves using an empty NetworkIdentifier.
            bool prefabWasActive = prefab.gameObject.activeSelf;
            prefab.gameObject.SetActive(false);
            NetworkIdentity networkIdentity = GameObject.Instantiate(prefab);
            prefab.gameObject.SetActive(prefabWasActive);

            networkIdentity.NetworkIdentifier = networkIdentifier;
            networkIdentity.NetworkOwner = networkOwner;
            networkIdentity.Channel = channel;
            networkIdentity.gameObject.SetActive(prefabWasActive);

            RegisterNetworkIdentity(networkIdentity);

            Debug.Log($"Spawned remote network identity '{prefabName}' ({networkIdentifier}) for player {networkOwner}");

            return networkIdentity;
        }

        public void RegisterNetworkIdentity(INetworkIdentity networkIdentity)
        {
            Debug.Log("Added " + networkIdentity.PrefabName + " to network context");
            _networkIdentities.Add(networkIdentity);
            _networkIdentitiesByGuid[networkIdentity.NetworkIdentifier] = networkIdentity;
        }

        public void UnregisterNetworkIdentity(INetworkIdentity networkIdentity)
        {
            _networkIdentities.Remove(networkIdentity);
            _networkIdentitiesByGuid.Remove(networkIdentity.NetworkIdentifier);
        }

        public void RegisterRpcTarget(IRpcTarget rpcTarget)
        {
            Guid networkIdentifier = rpcTarget.NetworkIdentity.NetworkIdentifier;
            if (!_rpcTargetsByIdentity.TryGetValue(networkIdentifier, out List<IRpcTarget> rpcTargets))
            {
                rpcTargets = new List<IRpcTarget>();
                _rpcTargetsByIdentity[networkIdentifier] = rpcTargets;
            }

            rpcTargets.Add(rpcTarget);
        }

        public void UnregisterRpcTarget(IRpcTarget rpcTarget)
        {
            Guid networkIdentifier = rpcTarget.NetworkIdentity.NetworkIdentifier;
            if (!_rpcTargetsByIdentity.TryGetValue(networkIdentifier, out List<IRpcTarget> rpcTargets))
                return;

            rpcTargets.Remove(rpcTarget);

            if (rpcTargets.Count == 0)
                _rpcTargetsByIdentity.Remove(networkIdentifier);
        }

        public void RegisterAuthorityOrigin(IAuthorityOrigin authorityOrigin)
        {
            _authorityOrigins.Add(authorityOrigin);
        }

        public void UnregisterAuthorityOrigin(IAuthorityOrigin authorityOrigin)
        {
            _authorityOrigins.Remove(authorityOrigin);
        }

        public void ClearNetworkSession()
        {
            for (int identityIndex = _networkIdentities.Count - 1; identityIndex >= 0; identityIndex--)
            {
                INetworkIdentity networkIdentity = _networkIdentities[identityIndex];
                UnregisterNetworkIdentity(networkIdentity);

                if (networkIdentity.GameObject != null)
                    Destroy(networkIdentity.GameObject);
            }

            _networkPlayers.Clear();
            _networkPlayersByGuid.Clear();
            _outgoingRpcMessages.Clear();
            LocalClientIdentity = Guid.Empty;
        }

        private void AddNetworkPlayer(NetworkPlayer networkPlayer)
        {
            _networkPlayers.Add(networkPlayer);
            _networkPlayersByGuid[networkPlayer.Guid] = networkPlayer;
        }

        private void RemoveNetworkPlayer(NetworkPlayer networkPlayer)
        {
            _networkPlayers.Remove(networkPlayer);
            _networkPlayersByGuid.Remove(networkPlayer.Guid);
        }

        public void ReadNetworkStates(IStateInput stateInput)
        {
            stateInput.BeginObjectElement();
            foreach (INetworkIdentity networkIdentity in _networkIdentities)
            {
                stateInput.BeginObjectProperty(networkIdentity.NetworkIdentifier.ToString());
                ReadNetworkIdentityState(networkIdentity, stateInput);
                stateInput.EndObject();
            }
            stateInput.EndObject();
        }

        public void WriteNetworkStates(IStateOutput stateOutput)
        {
            stateOutput.BeginObjectElement();
            foreach (INetworkIdentity networkIdentity in _networkIdentities)
            {
                stateOutput.BeginObjectProperty(networkIdentity.NetworkIdentifier.ToString());
                WriteNetworkIdentityState(networkIdentity, stateOutput);
                stateOutput.EndObject();
            }
            stateOutput.EndObject();
        }

        private static void ReadNetworkIdentityState(INetworkIdentity networkIdentity, IStateInput stateInput)
        {
            ReadNetworkIdentityOwnership(networkIdentity, stateInput);
            ReadNetworkIdentityComponents(networkIdentity, stateInput);
        }

        private static void ReadNetworkIdentityOwnership(INetworkIdentity networkIdentity, IStateInput stateInput)
        {
            string prefabName = stateInput.ReadString(nameof(NetworkIdentity.PrefabName));
            if (prefabName != networkIdentity.PrefabName)
                throw new InvalidOperationException($"Prefab name mismatch. Expected: {networkIdentity.PrefabName}, Received: {prefabName}");

            Guid networkOwner = stateInput.ReadGuid(nameof(NetworkIdentity.NetworkOwner));
            networkIdentity.NetworkOwner = networkOwner;
        }

        private static void ReadNetworkIdentityComponents(INetworkIdentity networkIdentity, IStateInput stateInput)
        {
            stateInput.BeginArrayProperty("NetworkStates");
            foreach (INetworkState networkState in networkIdentity.NetworkStates)
            {
                stateInput.BeginObjectElement();
                networkState.ReadNetworkState(stateInput);
                stateInput.EndObject();
            }
            stateInput.EndArray();
        }

        private static void WriteNetworkIdentityState(INetworkIdentity networkIdentity, IStateOutput stateOutput)
        {
            stateOutput.WriteString(nameof(NetworkIdentity.PrefabName), networkIdentity.PrefabName);
            stateOutput.WriteGuid(nameof(NetworkIdentity.NetworkOwner), networkIdentity.NetworkOwner);
            WriteNetworkIdentityComponents(networkIdentity, stateOutput);
        }

        private static void WriteNetworkIdentityComponents(INetworkIdentity networkIdentity, IStateOutput stateOutput)
        {
            stateOutput.BeginArrayProperty("NetworkStates");
            foreach (INetworkState networkState in networkIdentity.NetworkStates)
            {
                stateOutput.BeginObjectElement();
                networkState.WriteNetworkState(stateOutput);
                stateOutput.EndObject();
            }
            stateOutput.EndArray();
        }

        public void ManualUpdate(float unscaledDeltaTime)
        {
            TickPlayers(unscaledDeltaTime);
        }

        private void TickPlayers(float unscaledDeltaTime)
        {
            for (int networkPlayerIndex = _networkPlayers.Count - 1; networkPlayerIndex >= 0; networkPlayerIndex -= 1)
            {
                NetworkPlayer networkPlayer = _networkPlayers[networkPlayerIndex];
                networkPlayer.TimeSinceLastMessage += unscaledDeltaTime;

                if (networkPlayer.TimeSinceLastMessage < _playerTimeoutSeconds)
                    continue;

                Guid playerGuid = networkPlayer.Guid;

                for (int identityIndex = _networkIdentities.Count - 1; identityIndex >= 0; identityIndex -= 1)
                {
                    INetworkIdentity networkIdentity = _networkIdentities[identityIndex];
                    if (networkIdentity.NetworkOwner != playerGuid)
                        continue;

                    UnregisterNetworkIdentity(networkIdentity);
                    Destroy(networkIdentity.GameObject);
                }

                RemoveNetworkPlayer(networkPlayer);
                Debug.Log($"Removed timed out player {playerGuid}");
            }
        }

        public void ProcessChannelMessage(Guid senderGuid, string channel, byte[] data)
        {
            if (senderGuid == LocalClientIdentity)
                return;

            if (_networkPlayersByGuid.TryGetValue(senderGuid, out NetworkPlayer networkPlayer))
                networkPlayer.TimeSinceLastMessage = 0f;
            else
                AddNetworkPlayer(new NetworkPlayer(senderGuid));

            if (data == null || data.Length == 0)
                throw new InvalidOperationException("Channel message data is null or empty");

            if (data[0] == NetworkMessage.Rpc)
                ProcessIncomingRpcMessage(data);
            else
                ApplyIncomingChannelState(senderGuid, channel, data);
        }

        public void SendRpc(Guid networkIdentifier, string rpcSubjectName, IStateOutput parametersStateOutput, string channel)
        {
            byte[] parametersPayload = SerializeRpcParameters(parametersStateOutput);
            _outgoingRpcMessages.Enqueue((channel, CreateRpcMessage(networkIdentifier, rpcSubjectName, parametersPayload)));
            DispatchRpc(networkIdentifier, rpcSubjectName, parametersPayload);
        }

        public bool TryDequeueOutgoingRpcMessage(out string channel, out byte[] message)
        {
            if (_outgoingRpcMessages.Count == 0)
            {
                channel = null;
                message = null;
                return false;
            }

            (channel, message) = _outgoingRpcMessages.Dequeue();
            return true;
        }

        private byte[] SerializeRpcParameters(IStateOutput parametersStateOutput)
        {
            if (parametersStateOutput is BinaryStateOutput binaryStateOutput)
                return binaryStateOutput.ToArray();

            return Encoding.UTF8.GetBytes(parametersStateOutput.ToString());
        }

        private static byte[] CreateRpcMessage(Guid networkIdentifier, string rpcSubjectName, byte[] parametersPayload)
        {
            byte[] subjectNameBytes = Encoding.UTF8.GetBytes(rpcSubjectName);
            byte[] message = new byte[19 + subjectNameBytes.Length + parametersPayload.Length];
            message[0] = NetworkMessage.Rpc;
            message[1] = (byte)subjectNameBytes.Length;
            message[2] = (byte)(subjectNameBytes.Length >> 8);
            Buffer.BlockCopy(subjectNameBytes, 0, message, 3, subjectNameBytes.Length);
            Buffer.BlockCopy(networkIdentifier.ToByteArray(), 0, message, 3 + subjectNameBytes.Length, 16);
            Buffer.BlockCopy(parametersPayload, 0, message, 19 + subjectNameBytes.Length, parametersPayload.Length);
            return message;
        }

        private void ProcessIncomingRpcMessage(byte[] data)
        {
            int subjectNameLength = data[1] | (data[2] << 8);
            string rpcSubjectName = Encoding.UTF8.GetString(data, 3, subjectNameLength);
            Guid networkIdentifier = new Guid(data.AsSpan(3 + subjectNameLength, 16));

            byte[] parametersPayload = new byte[data.Length - 19 - subjectNameLength];
            Buffer.BlockCopy(data, 19 + subjectNameLength, parametersPayload, 0, parametersPayload.Length);

            DispatchRpc(networkIdentifier, rpcSubjectName, parametersPayload);
        }

        private void DispatchRpc(Guid networkIdentifier, string rpcSubjectName, byte[] parametersPayload)
        {
            if (!_rpcTargetsByIdentity.TryGetValue(networkIdentifier, out List<IRpcTarget> rpcTargets))
                return;

            foreach (IRpcTarget rpcTarget in rpcTargets)
            {
                if (rpcTarget.RpcSubjectName != rpcSubjectName)
                    continue;

                rpcTarget.ReceiveRpc(CreateRpcParametersStateInput(parametersPayload));
            }
        }

        private IStateInput CreateRpcParametersStateInput(byte[] parametersPayload)
        {
            if (_useBinary)
                return new BinaryStateInput(parametersPayload);

            return new JsonStateInput(Encoding.UTF8.GetString(parametersPayload));
        }

        private void WriteOwnedNetworkStates(IStateOutput stateOutput, string channel)
        {
            stateOutput.BeginObjectElement();
            foreach (INetworkIdentity networkIdentity in _networkIdentities)
            {
                //if (networkIdentity.DistanceBasedAuthority)
                //    Debug.Log("1");

                if (!networkIdentity.NetworkAuthority)
                    continue;

                //if (networkIdentity.DistanceBasedAuthority)
                //    Debug.Log("2");

                if (networkIdentity.Channel != channel)
                    continue;

                //if (networkIdentity.DistanceBasedAuthority)
                //    Debug.Log("3");

                stateOutput.BeginObjectProperty(networkIdentity.NetworkIdentifier.ToString());
                WriteNetworkIdentityState(networkIdentity, stateOutput);
                stateOutput.EndObject();
            }
            stateOutput.EndObject();
        }

        public byte[] GetOwnedNetworkIdentitiesPayload(string channel)
        {
            if (_useBinary)
            {
                using BinaryStateOutput stateOutput = new();
                WriteOwnedNetworkStates(stateOutput, channel);
                return stateOutput.GetBuffer().ToArray();
            }
            else
            {
                JsonStateOutput jsonStateOutput = new(prettyPrint: false, bracesOnNewLine: false);
                WriteOwnedNetworkStates(jsonStateOutput, channel);
                return Encoding.UTF8.GetBytes(jsonStateOutput.ToString());
            }
        }

        private void ApplyIncomingChannelState(Guid senderGuid, string channel, byte[] data)
        {
            ReadOnlyMemory<byte> payload = StripMessageHeader(data);

            if (_useBinary)
            {
                foreach (Guid networkIdentifier in BinaryStateInput.GetRootIdentityIds(payload))
                    ApplyIncomingNetworkIdentity(senderGuid, channel, networkIdentifier, new BinaryStateInput(payload));
            }
            else
            {
                string json = Encoding.UTF8.GetString(payload.Span);

                foreach (Guid networkIdentifier in JsonStateInput.GetRootIdentityIds(json))
                    ApplyIncomingNetworkIdentity(senderGuid, channel, networkIdentifier, new JsonStateInput(json));
            }
        }

        private void ApplyIncomingNetworkIdentity(Guid senderGuid, string channel, Guid networkIdentifier, IStateInput stateInput)
        {
            stateInput.BeginObjectElement();
            stateInput.BeginObjectProperty(networkIdentifier.ToString());

            if (_networkIdentitiesByGuid.TryGetValue(networkIdentifier, out INetworkIdentity networkIdentity))
            {
                // Ownership is applied first so a client that missed a TakeAuthority RPC
                // still converges on the owner carried by the owner's state broadcasts.
                ReadNetworkIdentityOwnership(networkIdentity, stateInput);

                // Ignore stale component state from a client that is no longer the owner,
                // e.g. right after a distance-based authority transfer.
                // The state input is per-identity, so abandoning it mid-object is safe.
                if (senderGuid != networkIdentity.NetworkOwner)
                    return;

                ReadNetworkIdentityComponents(networkIdentity, stateInput);
            }
            else
            {
                string prefabName = stateInput.ReadString(nameof(NetworkIdentity.PrefabName));
                Guid networkOwner = stateInput.ReadGuid(nameof(NetworkIdentity.NetworkOwner));

                NetworkIdentity spawnedNetworkIdentity = Instantiate(prefabName, channel, networkIdentifier, networkOwner);
                ReadNetworkIdentityComponents(spawnedNetworkIdentity, stateInput);
            }

            stateInput.EndObject();
        }

        private static ReadOnlyMemory<byte> StripMessageHeader(byte[] data)
        {
            if (data[0] == NetworkMessage.SyncIdentities)
                return data.AsMemory(1);

            return data.AsMemory();
        }
    }
}
