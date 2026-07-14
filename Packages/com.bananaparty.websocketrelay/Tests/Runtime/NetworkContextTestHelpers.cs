using System;
using System.Collections.Generic;
using System.Reflection;
using BananaParty.WebSocketRelay;
using UnityEngine;

namespace BananaParty.WebSocketRelay.Tests
{
    internal static class NetworkContextTestHelpers
    {
        public static NetworkContext CreateContext(float playerTimeoutSeconds = 10f)
        {
            NetworkContext context = ScriptableObject.CreateInstance<NetworkContext>();
            SetPlayerTimeoutSeconds(context, playerTimeoutSeconds);
            return context;
        }

        public static void SetPlayerTimeoutSeconds(NetworkContext context, float playerTimeoutSeconds)
        {
            FieldInfo field = typeof(NetworkContext).GetField(
                "_playerTimeoutSeconds",
                BindingFlags.Instance | BindingFlags.NonPublic);
            field.SetValue(context, playerTimeoutSeconds);
        }

        public static int GetNetworkPlayerCount(NetworkContext context)
        {
            FieldInfo field = typeof(NetworkContext).GetField(
                "_networkPlayers",
                BindingFlags.Instance | BindingFlags.NonPublic);
            return ((List<NetworkPlayer>)field.GetValue(context)).Count;
        }

        public static int GetNetworkIdentityCount(NetworkContext context)
        {
            FieldInfo field = typeof(NetworkContext).GetField(
                "_networkIdentities",
                BindingFlags.Instance | BindingFlags.NonPublic);
            return ((List<INetworkIdentity>)field.GetValue(context)).Count;
        }

        public static int GetAuthorityOriginCount(NetworkContext context)
        {
            FieldInfo field = typeof(NetworkContext).GetField(
                "_authorityOrigins",
                BindingFlags.Instance | BindingFlags.NonPublic);
            return ((List<IAuthorityOrigin>)field.GetValue(context)).Count;
        }

        public static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            field.SetValue(target, value);
        }

        public static NetworkIdentity CreatePlayerActor(
            NetworkContext context,
            Guid playerId,
            Vector3 position,
            string name = "Player")
        {
            GameObject gameObject = new(name);
            gameObject.SetActive(false);
            gameObject.transform.position = position;

            NetworkIdentity networkIdentity = gameObject.AddComponent<NetworkIdentity>();
            AuthorityOrigin authorityOrigin = gameObject.AddComponent<AuthorityOrigin>();

            SetPrivateField(networkIdentity, "_networkContext", context);
            SetPrivateField(authorityOrigin, "_networkContext", context);
            SetPrivateField(networkIdentity, "_distanceBasedAuthority", false);

            networkIdentity.NetworkOwner = playerId;
            networkIdentity.NetworkIdentifier = Guid.NewGuid();

            gameObject.SetActive(true);
            return networkIdentity;
        }

        public static NetworkIdentity CreateDistanceBasedObject(
            NetworkContext context,
            Vector3 position,
            Guid networkOwner,
            string name = "WorldObject")
        {
            GameObject gameObject = new(name);
            gameObject.SetActive(false);
            gameObject.transform.position = position;

            NetworkIdentity networkIdentity = gameObject.AddComponent<NetworkIdentity>();
            SetPrivateField(networkIdentity, "_networkContext", context);
            SetPrivateField(networkIdentity, "_distanceBasedAuthority", true);

            networkIdentity.NetworkOwner = networkOwner;
            networkIdentity.NetworkIdentifier = Guid.NewGuid();

            gameObject.SetActive(true);
            return networkIdentity;
        }
    }

    internal sealed class StubNetworkIdentity : INetworkIdentity
    {
        public StubNetworkIdentity(
            GameObject gameObject,
            string prefabName,
            Guid networkOwner,
            Guid networkIdentifier,
            string channel = "test-channel")
        {
            GameObject = gameObject;
            PrefabName = prefabName;
            NetworkOwner = networkOwner;
            NetworkIdentifier = networkIdentifier;
            Channel = channel;
        }

        public string PrefabName { get; }
        public GameObject GameObject { get; }
        public string Channel { get; set; }
        public Guid NetworkIdentifier { get; set; }
        public Guid NetworkOwner { get; set; }
        public bool NetworkAuthority => false;
        public IReadOnlyList<INetworkState> NetworkStates => Array.Empty<INetworkState>();
        public bool DistanceBasedAuthority => false;
    }
}
