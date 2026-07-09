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
        private float _playerTimeoutSeconds = 10f;

        [SerializeField]
        private bool _useBinary = false;

        [SerializeField]
        private List<NetworkIdentity> _networkPrefabs;

        public Guid LocalClientIdentity { get; set; }

        private readonly List<INetworkIdentity> _networkIdentities = new();
        private readonly Dictionary<Guid, INetworkIdentity> _networkIdentitiesByGuid = new();

        private readonly List<NetworkPlayer> _networkPlayers = new();
        private readonly Dictionary<Guid, NetworkPlayer> _networkPlayersByGuid = new();

        public NetworkIdentity Instantiate(NetworkIdentity networkIdentityPrefab, string topic)
        {
            return Instantiate(networkIdentityPrefab.PrefabName, topic, Guid.NewGuid(), LocalClientIdentity);
        }

        private NetworkIdentity Instantiate(string prefabName, string topic, Guid networkIdentifier, Guid networkOwner)
        {
            NetworkIdentity prefab = _networkPrefabs.Find(networkPrefab => networkPrefab.PrefabName == prefabName);
            if (prefab == null)
                throw new InvalidOperationException($"No network prefab registered with name {prefabName}");

            NetworkIdentity networkIdentity = GameObject.Instantiate(prefab);
            networkIdentity.NetworkIdentifier = networkIdentifier;
            networkIdentity.NetworkOwner = networkOwner;
            networkIdentity.Topic = topic;

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
            string prefabName = stateInput.ReadString(nameof(NetworkIdentity.PrefabName));
            if (prefabName != networkIdentity.PrefabName)
                throw new InvalidOperationException($"Prefab name mismatch. Expected: {networkIdentity.PrefabName}, Received: {prefabName}");

            networkIdentity.NetworkOwner = stateInput.ReadGuid(nameof(NetworkIdentity.NetworkOwner));
            ReadNetworkIdentityComponents(networkIdentity, stateInput);
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

        public void ProcessTopicMessage(Guid senderGuid, string topic, byte[] data)
        {
            if (senderGuid == LocalClientIdentity)
                return;

            if (_networkPlayersByGuid.TryGetValue(senderGuid, out NetworkPlayer networkPlayer))
                networkPlayer.TimeSinceLastMessage = 0f;
            else
                AddNetworkPlayer(new NetworkPlayer(senderGuid));

            if (data == null || data.Length == 0)
                throw new InvalidOperationException("Topic message data is null or empty");

            ApplyIncomingTopicState(topic, data);
        }

        private void WriteOwnedNetworkStates(IStateOutput stateOutput, string topic)
        {
            stateOutput.BeginObjectElement();
            foreach (INetworkIdentity networkIdentity in _networkIdentities)
            {
                if (networkIdentity.NetworkOwner != LocalClientIdentity)
                    continue;

                if (networkIdentity.Topic != topic)
                    continue;

                stateOutput.BeginObjectProperty(networkIdentity.NetworkIdentifier.ToString());
                WriteNetworkIdentityState(networkIdentity, stateOutput);
                stateOutput.EndObject();
            }
            stateOutput.EndObject();
        }

        public byte[] GetOwnedNetworkIdentitiesPayload(string topic)
        {
            if (_useBinary)
            {
                using BinaryStateOutput stateOutput = new();
                WriteOwnedNetworkStates(stateOutput, topic);
                return stateOutput.GetBuffer().ToArray();
            }
            else
            {
                JsonStateOutput jsonStateOutput = new(prettyPrint: false, bracesOnNewLine: false);
                WriteOwnedNetworkStates(jsonStateOutput, topic);
                return Encoding.UTF8.GetBytes(jsonStateOutput.ToString());
            }
        }

        private void ApplyIncomingTopicState(string topic, byte[] data)
        {
            ReadOnlyMemory<byte> payload = StripMessageHeader(data);

            IReadOnlyList<Guid> identityIds = _useBinary
                ? BinaryStateInput.GetRootIdentityIds(payload)
                : JsonStateInput.GetRootIdentityIds(Encoding.UTF8.GetString(payload.Span));

            IStateInput stateInput = _useBinary
                ? new BinaryStateInput(payload)
                : new JsonStateInput(Encoding.UTF8.GetString(payload.Span));

            Debug.Log("Received " + data.Length + " " + Encoding.UTF8.GetString(payload.Span));

            stateInput.BeginObjectElement();

            foreach (Guid networkIdentifier in identityIds)
            {
                stateInput.BeginObjectProperty(networkIdentifier.ToString());

                if (_networkIdentitiesByGuid.TryGetValue(networkIdentifier, out INetworkIdentity networkIdentity))
                {
                    ReadNetworkIdentityState(networkIdentity, stateInput);
                }
                else
                {
                    string prefabName = stateInput.ReadString(nameof(NetworkIdentity.PrefabName));
                    Guid networkOwner = stateInput.ReadGuid(nameof(NetworkIdentity.NetworkOwner));

                    NetworkIdentity spawnedNetworkIdentity = Instantiate(prefabName, topic, networkIdentifier, networkOwner);
                    ReadNetworkIdentityComponents(spawnedNetworkIdentity, stateInput);
                }

                stateInput.EndObject();
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
