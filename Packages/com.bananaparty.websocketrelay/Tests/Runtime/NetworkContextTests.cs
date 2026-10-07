using System;
using System.Collections;
using BananaParty.WebSocketRelay;
using BananaParty.WebSocketRelay.Transport;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BananaParty.WebSocketRelay.Tests
{
    public class NetworkContextTests
    {
        [Test]
        public void ProcessChannelMessage_IgnoresLocalSender()
        {
            NetworkContext context = NetworkContextTestHelpers.CreateContext();
            context.LocalClientIdentity = Guid.NewGuid();

            context.ProcessChannelMessage(context.LocalClientIdentity, "room", NetworkContextTestHelpers.CreateEmptySyncIdentitiesMessage());

            Assert.AreEqual(0, context.NetworkPlayers.Count);
            UnityEngine.Object.DestroyImmediate(context);
        }

        [Test]
        public void ProcessChannelMessage_TracksRemotePlayer()
        {
            NetworkContext context = NetworkContextTestHelpers.CreateContext();
            context.LocalClientIdentity = Guid.NewGuid();
            Guid remotePlayer = Guid.NewGuid();

            context.ProcessChannelMessage(remotePlayer, "room", NetworkContextTestHelpers.CreateEmptySyncIdentitiesMessage());

            Assert.AreEqual(1, context.NetworkPlayers.Count);
            UnityEngine.Object.DestroyImmediate(context);
        }

        [UnityTest]
        public IEnumerator TimedOutPlayer_RemovesOwnedIdentities()
        {
            NetworkContext context = NetworkContextTestHelpers.CreateContext(playerTimeoutSeconds: 1f);
            context.LocalClientIdentity = Guid.NewGuid();
            Guid remotePlayer = Guid.NewGuid();

            context.ProcessChannelMessage(remotePlayer, "room", NetworkContextTestHelpers.CreateEmptySyncIdentitiesMessage());

            GameObject remoteObject = new("RemoteOwnedObject");
            StubNetworkIdentity remoteIdentity = new(
                remoteObject,
                "RemotePrefab",
                remotePlayer,
                Guid.NewGuid());
            context.RegisterNetworkIdentity(remoteIdentity);

            NetworkContextTestHelpers.Advance(context, 1.1f);
            yield return null;

            Assert.AreEqual(0, context.NetworkPlayers.Count);
            Assert.AreEqual(0, context.NetworkIdentities.Count);
            Assert.IsTrue(remoteObject == null);

            UnityEngine.Object.DestroyImmediate(context);
        }

        [UnityTest]
        public IEnumerator TimedOutPlayer_KeepsIdentitiesWhenDestroyWhenAuthorityOwnerLeavesIsFalse()
        {
            NetworkContext context = NetworkContextTestHelpers.CreateContext(playerTimeoutSeconds: 1f);
            context.LocalClientIdentity = Guid.NewGuid();
            Guid remotePlayer = Guid.NewGuid();

            context.ProcessChannelMessage(remotePlayer, "room", NetworkContextTestHelpers.CreateEmptySyncIdentitiesMessage());

            GameObject remoteObject = new("PersistentOwnedObject");
            StubNetworkIdentity remoteIdentity = new(
                remoteObject,
                "RemotePrefab",
                remotePlayer,
                Guid.NewGuid())
            {
                DestroyWhenAuthorityOwnerLeaves = false
            };
            context.RegisterNetworkIdentity(remoteIdentity);

            NetworkContextTestHelpers.Advance(context, 1.1f);
            yield return null;

            Assert.AreEqual(0, context.NetworkPlayers.Count);
            Assert.AreEqual(1, context.NetworkIdentities.Count);
            Assert.IsFalse(remoteObject == null);

            UnityEngine.Object.DestroyImmediate(remoteObject);
            UnityEngine.Object.DestroyImmediate(context);
        }

        [UnityTest]
        public IEnumerator TimedOutPlayer_DoesNotRemoveOtherPlayersIdentities()
        {
            NetworkContext context = NetworkContextTestHelpers.CreateContext(playerTimeoutSeconds: 2f);
            context.LocalClientIdentity = Guid.NewGuid();
            Guid timingOutPlayer = Guid.NewGuid();
            Guid activePlayer = Guid.NewGuid();

            context.ProcessChannelMessage(timingOutPlayer, "room", NetworkContextTestHelpers.CreateEmptySyncIdentitiesMessage());
            context.ProcessChannelMessage(activePlayer, "room", NetworkContextTestHelpers.CreateEmptySyncIdentitiesMessage());

            GameObject timingOutObject = new("TimingOutObject");
            GameObject activeObject = new("ActiveObject");
            context.RegisterNetworkIdentity(new StubNetworkIdentity(
                timingOutObject,
                "TimingOutPrefab",
                timingOutPlayer,
                Guid.NewGuid()));
            context.RegisterNetworkIdentity(new StubNetworkIdentity(
                activeObject,
                "ActivePrefab",
                activePlayer,
                Guid.NewGuid()));

            NetworkContextTestHelpers.Advance(context, 1.1f);
            context.ProcessChannelMessage(activePlayer, "room", NetworkContextTestHelpers.CreateEmptySyncIdentitiesMessage());
            NetworkContextTestHelpers.Advance(context, 1.1f);
            yield return null;

            Assert.AreEqual(1, context.NetworkPlayers.Count);
            Assert.AreEqual(1, context.NetworkIdentities.Count);
            Assert.IsTrue(timingOutObject == null);
            Assert.IsFalse(activeObject == null);

            UnityEngine.Object.DestroyImmediate(activeObject);
            UnityEngine.Object.DestroyImmediate(context);
        }

        [UnityTest]
        public IEnumerator ChannelMessage_ResetsPlayerTimeout()
        {
            NetworkContext context = NetworkContextTestHelpers.CreateContext(playerTimeoutSeconds: 2f);
            context.LocalClientIdentity = Guid.NewGuid();
            Guid remotePlayer = Guid.NewGuid();

            context.ProcessChannelMessage(remotePlayer, "room", NetworkContextTestHelpers.CreateEmptySyncIdentitiesMessage());

            GameObject remoteObject = new("RemoteOwnedObject");
            context.RegisterNetworkIdentity(new StubNetworkIdentity(
                remoteObject,
                "RemotePrefab",
                remotePlayer,
                Guid.NewGuid()));

            NetworkContextTestHelpers.Advance(context, 1.5f);
            context.ProcessChannelMessage(remotePlayer, "room", NetworkContextTestHelpers.CreateEmptySyncIdentitiesMessage());
            NetworkContextTestHelpers.Advance(context, 1.5f);
            yield return null;

            Assert.AreEqual(1, context.NetworkPlayers.Count);
            Assert.AreEqual(1, context.NetworkIdentities.Count);
            Assert.IsFalse(remoteObject == null);

            UnityEngine.Object.DestroyImmediate(remoteObject);
            UnityEngine.Object.DestroyImmediate(context);
        }

        [UnityTest]
        public IEnumerator ClearNetworkSession_RemovesAllIdentitiesAndPlayers()
        {
            NetworkContext context = NetworkContextTestHelpers.CreateContext();
            Guid localPlayer = Guid.NewGuid();
            Guid remotePlayer = Guid.NewGuid();
            context.LocalClientIdentity = localPlayer;

            context.ProcessChannelMessage(remotePlayer, "room", NetworkContextTestHelpers.CreateEmptySyncIdentitiesMessage());

            GameObject localObject = new("LocalOwnedObject");
            GameObject remoteObject = new("RemoteOwnedObject");
            context.RegisterNetworkIdentity(new StubNetworkIdentity(
                localObject,
                "LocalPrefab",
                localPlayer,
                Guid.NewGuid()));
            context.RegisterNetworkIdentity(new StubNetworkIdentity(
                remoteObject,
                "RemotePrefab",
                remotePlayer,
                Guid.NewGuid()));

            context.ClearNetworkSession();
            yield return null;

            Assert.AreEqual(Guid.Empty, context.LocalClientIdentity);
            Assert.AreEqual(0, context.NetworkPlayers.Count);
            Assert.AreEqual(0, context.NetworkIdentities.Count);
            Assert.IsTrue(localObject == null);
            Assert.IsTrue(remoteObject == null);

            UnityEngine.Object.DestroyImmediate(context);
        }

        [UnityTest]
        public IEnumerator ClearNetworkSession_KeepsSceneBoundIdentitiesWithoutOwner()
        {
            NetworkContext context = NetworkContextTestHelpers.CreateContext();
            NetworkChannel networkChannel = ScriptableObject.CreateInstance<NetworkChannel>();
            Guid localPlayer = Guid.NewGuid();
            context.LocalClientIdentity = localPlayer;

            NetworkIdentity sceneIdentity = NetworkContextTestHelpers.CreateSceneBoundIdentity(context, networkChannel, localPlayer);
            GameObject spawnedObject = new("SpawnedObject");
            context.RegisterNetworkIdentity(new StubNetworkIdentity(spawnedObject, "SpawnedPrefab", localPlayer, Guid.NewGuid()));

            context.ClearNetworkSession();
            yield return null;

            Assert.IsFalse(sceneIdentity == null, "Scene identity was destroyed, but scene objects cannot be spawned again.");
            Assert.AreEqual(Guid.Empty, sceneIdentity.NetworkAuthorityOwner);
            CollectionAssert.AreEqual(new INetworkIdentity[] { sceneIdentity }, context.NetworkIdentities);
            Assert.IsTrue(spawnedObject == null);

            UnityEngine.Object.DestroyImmediate(sceneIdentity.gameObject);
            UnityEngine.Object.DestroyImmediate(networkChannel);
            UnityEngine.Object.DestroyImmediate(context);
        }

        [UnityTest]
        public IEnumerator DisconnectedFromRelay_ClearsNetworkSession()
        {
            NetworkContext context = NetworkContextTestHelpers.CreateContext();
            context.LocalClientIdentity = Guid.NewGuid();

            GameObject localObject = new("LocalOwnedObject");
            context.RegisterNetworkIdentity(new StubNetworkIdentity(
                localObject,
                "LocalPrefab",
                context.LocalClientIdentity,
                Guid.NewGuid()));

            Network network = new Network("ws://127.0.0.1:1", context);
            network.Connect(context.LocalClientIdentity);
            ((IRelayListener)network).OnConnectionStateChanged(RelayConnectionState.Connected, RelayConnectionState.Disconnected, "Test");
            yield return null;

            Assert.IsFalse(network.HasRelayClient);

            Assert.AreEqual(Guid.Empty, context.LocalClientIdentity);
            Assert.AreEqual(0, context.NetworkIdentities.Count);
            Assert.IsTrue(localObject == null);

            UnityEngine.Object.DestroyImmediate(context);
        }

        [Test]
        public void LongFrame_DoesNotTimeOutPlayersWhoseMessagesWereQueued()
        {
            NetworkContext context = NetworkContextTestHelpers.CreateContext(playerTimeoutSeconds: 10f);
            context.LocalClientIdentity = Guid.NewGuid();

            context.ProcessChannelMessage(Guid.NewGuid(), "room", NetworkContextTestHelpers.CreateEmptySyncIdentitiesMessage());
            context.ManualUpdate(30f);

            Assert.AreEqual(1, context.NetworkPlayers.Count);
            UnityEngine.Object.DestroyImmediate(context);
        }

        [Test]
        public void ConnectionInterrupted_PausesPlayerTimeouts()
        {
            NetworkContext context = NetworkContextTestHelpers.CreateContext(playerTimeoutSeconds: 1f);
            context.LocalClientIdentity = Guid.NewGuid();
            context.ProcessChannelMessage(Guid.NewGuid(), "room", NetworkContextTestHelpers.CreateEmptySyncIdentitiesMessage());

            context.IsConnectionInterrupted = true;
            NetworkContextTestHelpers.Advance(context, 5f);
            Assert.AreEqual(1, context.NetworkPlayers.Count);

            context.IsConnectionInterrupted = false;
            NetworkContextTestHelpers.Advance(context, 0.9f);
            Assert.AreEqual(1, context.NetworkPlayers.Count);

            NetworkContextTestHelpers.Advance(context, 0.2f);
            Assert.AreEqual(0, context.NetworkPlayers.Count);

            UnityEngine.Object.DestroyImmediate(context);
        }

        [Test]
        public void ClearNetworkSession_ClearsConnectionInterrupted()
        {
            NetworkContext context = NetworkContextTestHelpers.CreateContext();
            context.IsConnectionInterrupted = true;

            context.ClearNetworkSession();

            Assert.IsFalse(context.IsConnectionInterrupted);
            UnityEngine.Object.DestroyImmediate(context);
        }

        [UnityTest]
        public IEnumerator TimedOutPlayer_KeepsSceneBoundIdentityAndClearsItsOwner()
        {
            NetworkContext context = NetworkContextTestHelpers.CreateContext(playerTimeoutSeconds: 1f);
            NetworkChannel networkChannel = ScriptableObject.CreateInstance<NetworkChannel>();
            context.LocalClientIdentity = Guid.NewGuid();
            Guid remotePlayer = Guid.NewGuid();

            NetworkIdentity sceneIdentity = NetworkContextTestHelpers.CreateSceneBoundIdentity(context, networkChannel, remotePlayer);
            NetworkIdentity spawnedIdentity = NetworkContextTestHelpers.CreateDistanceBasedObject(context, Vector3.zero, remotePlayer);
            context.RegisterNetworkIdentity(spawnedIdentity);

            Assert.IsTrue(sceneIdentity.IsSceneBound);
            Assert.IsFalse(sceneIdentity.DestroyWhenAuthorityOwnerLeaves);
            Assert.IsTrue(spawnedIdentity.DestroyWhenAuthorityOwnerLeaves);
            Assert.AreEqual(2, context.NetworkIdentities.Count);

            context.ProcessChannelMessage(remotePlayer, "room", NetworkContextTestHelpers.CreateEmptySyncIdentitiesMessage());
            NetworkContextTestHelpers.Advance(context, 1.1f);
            yield return null;

            Assert.AreEqual(0, context.NetworkPlayers.Count);
            Assert.AreEqual(1, context.NetworkIdentities.Count);
            Assert.IsFalse(sceneIdentity == null);
            Assert.AreEqual(Guid.Empty, sceneIdentity.NetworkAuthorityOwner);
            Assert.IsTrue(spawnedIdentity == null);

            UnityEngine.Object.DestroyImmediate(sceneIdentity.gameObject);
            UnityEngine.Object.DestroyImmediate(networkChannel);
            UnityEngine.Object.DestroyImmediate(context);
        }

        [UnityTest]
        public IEnumerator AuthorityOrigin_DoesNotClaimWhileConnectionInterrupted()
        {
            NetworkContext context = NetworkContextTestHelpers.CreateContext();
            Guid localPlayer = Guid.NewGuid();
            context.LocalClientIdentity = localPlayer;
            context.IsConnectionInterrupted = true;

            NetworkIdentity playerActor = NetworkContextTestHelpers.CreatePlayerActor(context, localPlayer, Vector3.zero);
            NetworkIdentity worldObject = NetworkContextTestHelpers.CreateDistanceBasedObject(context, Vector3.right, Guid.Empty);
            worldObject.Channel = "room";
            context.RegisterNetworkIdentity(worldObject);

            yield return null;
            yield return null;
            Assert.AreEqual(Guid.Empty, worldObject.NetworkAuthorityOwner);
            Assert.IsFalse(context.TryDequeueOutgoingRpcMessage(out _, out _));

            context.IsConnectionInterrupted = false;
            yield return null;
            yield return null;
            Assert.AreEqual(localPlayer, worldObject.NetworkAuthorityOwner);

            UnityEngine.Object.DestroyImmediate(playerActor.gameObject);
            UnityEngine.Object.DestroyImmediate(worldObject.gameObject);
            UnityEngine.Object.DestroyImmediate(context);
        }
    }
}
