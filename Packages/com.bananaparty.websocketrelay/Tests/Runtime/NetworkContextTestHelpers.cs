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
    }
}
