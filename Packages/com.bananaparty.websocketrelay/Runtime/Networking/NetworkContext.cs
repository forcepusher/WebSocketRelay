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
        private float _playerTimeoutSeconds = 15f;

        [SerializeField]
        private bool _useBinary = false;

        [SerializeField]
        private List<NetworkIdentity> _networkPrefabs;

        public Guid LocalClientIdentity { get; set; }

        private readonly List<INetworkIdentity> _networkIdentities = new();
        private readonly Dictionary<Guid, INetworkIdentity> _networkIdentitiesByGuid = new();

        private readonly List<NetworkPlayer> _networkPlayers = new();
        private readonly Dictionary<Guid, NetworkPlayer> _networkPlayersByGuid = new();

        public NetworkIdentity Instantiate(NetworkIdentity networkIdentityPrefab, Guid networkOwner)
        {
            return Instantiate(networkIdentityPrefab.PrefabName, Guid.NewGuid(), networkOwner);
        }

        private NetworkIdentity Instantiate(string prefabName, Guid networkIdentifier, Guid networkOwner)
        {
            NetworkIdentity prefab = _networkPrefabs.Find(networkPrefab => networkPrefab.PrefabName == prefabName);
            if (prefab == null)
                throw new InvalidOperationException($"No network prefab registered with name {prefabName}");

            NetworkIdentity networkIdentity = GameObject.Instantiate(prefab);
            networkIdentity.NetworkIdentifier = networkIdentifier;
            networkIdentity.NetworkOwner = networkOwner;

            RegisterNetworkIdentity(networkIdentity);

            Debug.Log($"Spawned remote network identity '{prefabName}' ({networkIdentifier}) for player {networkOwner}");

            return networkIdentity;
        }

        public void RegisterNetworkIdentity(INetworkIdentity networkIdentity)
        {
            Debug.Log("Added " + networkIdentity.NetworkStateName + " to network context");
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
                networkIdentity.ReadNetworkState(stateInput);
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
                networkIdentity.WriteNetworkState(stateOutput);
                stateOutput.EndObject();
            }
            stateOutput.EndObject();
        }

        public void ManualUpdate(float unscaledDeltaTime)
        {
            for (int networkPlayerIndex = _networkPlayers.Count - 1; networkPlayerIndex >= 0; networkPlayerIndex -= 1)
            {
                NetworkPlayer networkPlayer = _networkPlayers[networkPlayerIndex];

                if (networkPlayer.TimeSinceLastMessage >= _playerTimeoutSeconds)
                    RemoveNetworkPlayer(networkPlayer);
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

            ApplyIncomingTopicState(data);
        }

        // public byte[] SerializeNetworkStates()
        // {
        //     if (_useBinary)
        //     {
        //         using BinaryStateOutput stateOutput = new();
        //         WriteNetworkStates(stateOutput);
        //         return stateOutput.GetBuffer().ToArray();
        //     }
        //     else
        //     {
        //         JsonStateOutput jsonStateOutput = new(prettyPrint: false, bracesOnNewLine: false);
        //         WriteNetworkStates(jsonStateOutput);
        //         return Encoding.UTF8.GetBytes(jsonStateOutput.ToString());
        //     }
        // }

        #region SLOP
        private void ApplyIncomingTopicState(byte[] data)
        {
            ReadOnlyMemory<byte> payload = StripMessageHeader(data);

            IReadOnlyList<Guid> identityIds = _useBinary
                ? BinaryStateInput.GetRootIdentityIds(payload)
                : JsonStateInput.GetRootIdentityIds(Encoding.UTF8.GetString(payload.Span));

            IStateInput stateInput = _useBinary
                ? new BinaryStateInput(payload)
                : new JsonStateInput(Encoding.UTF8.GetString(payload.Span));

            stateInput.BeginObjectElement();

            foreach (Guid networkIdentifier in identityIds)
            {
                stateInput.BeginObjectProperty(networkIdentifier.ToString());

                if (_networkIdentitiesByGuid.TryGetValue(networkIdentifier, out INetworkIdentity networkIdentity))
                {
                    networkIdentity.ReadNetworkState(stateInput);
                }
                else
                {
                    string prefabName = stateInput.ReadString(nameof(NetworkIdentity.PrefabName));
                    Guid networkOwner = stateInput.ReadGuid(nameof(NetworkIdentity.NetworkOwner));

                    NetworkIdentity spawnedNetworkIdentity = Instantiate(prefabName, networkIdentifier, networkOwner);
                    spawnedNetworkIdentity.ReadNetworkStateBody(stateInput);
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

#endregion SLOP
    }
}
