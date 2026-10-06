using System;
using System.Collections;
using System.Linq;
using System.Text.RegularExpressions;
using BananaParty.WebSocketRelay.Transport;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BananaParty.WebSocketRelay.Tests
{
    public class NetworkTests
    {
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return RelayServerLauncher.StartCoroutine();
        }

        [Test]
        public void SubscribeWhenNotConnected_ThrowsInvalidOperationException()
        {
            NetworkContext context = NetworkContextTestHelpers.CreateContext();
            Network network = new Network(TestParameters.RelayServerAddress, context);

            Assert.Throws<InvalidOperationException>(() => network.SubscribeToChannel("room"));
            Assert.Throws<InvalidOperationException>(() => network.UnsubscribeFromChannel("room"));

            UnityEngine.Object.DestroyImmediate(context);
        }

        [UnityTest]
        public IEnumerator OfflineMode_FullSessionLifecycleWorksWithoutRelayServer()
        {
            NetworkContext context = NetworkContextTestHelpers.CreateContext();
            Guid clientGuid = Guid.NewGuid();
            Network network = new Network("ws://unreachable-address", context, offlineMode: true);

            network.StartServer();
            network.Connect(clientGuid);

            Assert.IsTrue(network.IsConnected);
            Assert.AreEqual(clientGuid, context.LocalClientIdentity);

            network.SubscribeToChannel("room");

            StubNetworkIdentity identity = new(
                new GameObject("OfflineOwnedObject"),
                "OfflinePrefab",
                clientGuid,
                Guid.NewGuid(),
                channel: "room");
            context.RegisterNetworkIdentity(identity);
            StubRpcTarget rpcTarget = new(identity, "TestRpc");
            context.RegisterRpcTarget(rpcTarget);

            context.SendRpc(identity.NetworkIdentifier, "TestRpc", NetworkContextTestHelpers.CreateRpcParameters(42), "room");
            network.SendSyncIdentities();
            network.ManualUpdate(Time.deltaTime);
            yield return null;
            network.ManualUpdate(Time.deltaTime);

            Assert.IsTrue(network.IsConnected);
            Assert.AreEqual(1, rpcTarget.ReceiveCount);
            Assert.AreEqual(42, rpcTarget.LastReceivedValue);
            Assert.IsFalse(context.TryDequeueOutgoingRpcMessage(out _, out _));

            network.StopServer();
            network.Disconnect();

            Assert.IsFalse(network.HasRelayClient);
            Assert.AreEqual(Guid.Empty, context.LocalClientIdentity);

            UnityEngine.Object.DestroyImmediate(context);
        }

        [UnityTest]
        public IEnumerator ConnectTimeout_DisconnectAllowsReconnect()
        {
            NetworkContext context = NetworkContextTestHelpers.CreateContext();
            Network network = new Network("ws://127.0.0.1:1", context);

            network.Connect(Guid.NewGuid());

            yield return TestParameters.WaitForDuration(1f, () => network.ManualUpdate(Time.deltaTime));

            Assert.IsFalse(network.IsConnected);
            Assert.IsTrue(network.HasRelayClient);

            network.Disconnect();

            Assert.IsFalse(network.HasRelayClient);

            Network connectedNetwork = new Network(TestParameters.RelayServerAddress, context);
            connectedNetwork.Connect(Guid.NewGuid());
            yield return TestParameters.WaitForCondition(
                () => connectedNetwork.IsConnected,
                TestParameters.ConnectTimeoutThreshold,
                () => connectedNetwork.ManualUpdate(Time.deltaTime));

            Assert.IsTrue(connectedNetwork.IsConnected);

            connectedNetwork.Disconnect();
            UnityEngine.Object.DestroyImmediate(context);
        }

        [UnityTest]
        public IEnumerator Disconnect_ClearSessionFalse_PreservesNetworkIdentities()
        {
            NetworkContext context = NetworkContextTestHelpers.CreateContext();
            Guid clientGuid = Guid.NewGuid();
            Network network = new Network("ws://127.0.0.1:1", context);

            network.Connect(clientGuid);

            GameObject sceneObject = new("SceneNetworkedObject");
            context.RegisterNetworkIdentity(new StubNetworkIdentity(
                sceneObject,
                "ScenePrefab",
                clientGuid,
                Guid.NewGuid()));

            yield return TestParameters.WaitForDuration(1f, () => network.ManualUpdate(Time.deltaTime));

            Assert.IsFalse(network.IsConnected);
            Assert.IsTrue(network.HasRelayClient);

            network.Disconnect(clearSession: false);

            Assert.IsFalse(network.HasRelayClient);
            Assert.AreEqual(1, context.NetworkIdentities.Count);
            Assert.IsFalse(sceneObject == null);

            UnityEngine.Object.DestroyImmediate(sceneObject);
            UnityEngine.Object.DestroyImmediate(context);
        }

        [UnityTest]
        public IEnumerator ServerStop_ReconnectDisabled_ClearsSessionAndAllowsReconnect()
        {
            NetworkContext context = NetworkContextTestHelpers.CreateContext();
            Guid clientGuid = Guid.NewGuid();
            Network network = new Network(TestParameters.RelayServerAddress, context, connectionSettings: TestParameters.ReconnectDisabledSettings());

            network.Connect(clientGuid);
            yield return TestParameters.WaitForCondition(
                () => network.IsConnected,
                TestParameters.ConnectTimeoutThreshold,
                () => network.ManualUpdate(Time.deltaTime));
            network.ManualUpdate(Time.deltaTime);

            GameObject localObject = new("LocalOwnedObject");
            context.RegisterNetworkIdentity(new StubNetworkIdentity(
                localObject,
                "LocalPrefab",
                clientGuid,
                Guid.NewGuid()));

            yield return RelayServerLauncher.StopCoroutine();

            yield return TestParameters.WaitForCondition(
                () => !network.HasRelayClient,
                TestParameters.DisconnectTimeoutThreshold,
                () => network.ManualUpdate(Time.deltaTime));

            Assert.IsFalse(network.HasRelayClient);
            Assert.AreEqual(Guid.Empty, context.LocalClientIdentity);
            Assert.AreEqual(0, context.NetworkIdentities.Count);
            Assert.IsTrue(localObject == null);

            yield return RelayServerLauncher.StartCoroutine();

            network.Connect(Guid.NewGuid());
            yield return TestParameters.WaitForCondition(
                () => network.IsConnected,
                TestParameters.ConnectTimeoutThreshold,
                () => network.ManualUpdate(Time.deltaTime));

            Assert.IsTrue(network.IsConnected);

            network.Disconnect();
            UnityEngine.Object.DestroyImmediate(context);
        }

        [UnityTest]
        public IEnumerator ServerStop_ReconnectDisabled_ManualUpdateDetectsDroppedConnection()
        {
            NetworkContext context = NetworkContextTestHelpers.CreateContext();
            Guid clientGuid = Guid.NewGuid();
            Network network = new Network(TestParameters.RelayServerAddress, context, connectionSettings: TestParameters.ReconnectDisabledSettings());

            network.Connect(clientGuid);
            yield return TestParameters.WaitForCondition(
                () => network.IsConnected,
                TestParameters.ConnectTimeoutThreshold,
                () => network.ManualUpdate(Time.deltaTime));
            network.ManualUpdate(Time.deltaTime);
            network.ManualUpdate(Time.deltaTime);

            GameObject localObject = new("LocalOwnedObject");
            context.RegisterNetworkIdentity(new StubNetworkIdentity(
                localObject,
                "LocalPrefab",
                clientGuid,
                Guid.NewGuid()));

            yield return RelayServerLauncher.StopCoroutine();

            Assert.IsTrue(network.HasRelayClient);

            yield return TestParameters.WaitForCondition(
                () => !network.HasRelayClient,
                TestParameters.DisconnectTimeoutThreshold,
                () => network.ManualUpdate(Time.deltaTime));

            Assert.IsFalse(network.IsConnected);
            Assert.AreEqual(0, context.NetworkIdentities.Count);
            Assert.IsTrue(localObject == null);

            UnityEngine.Object.DestroyImmediate(context);
        }

        [UnityTest]
        public IEnumerator ServerRestart_ResumesSessionWithSameIdentities()
        {
            NetworkContext contextA = NetworkContextTestHelpers.CreateContext();
            NetworkContext contextB = NetworkContextTestHelpers.CreateContext();
            Guid guidA = Guid.NewGuid();
            Guid guidB = Guid.NewGuid();
            Network networkA = new(TestParameters.RelayServerAddress, contextA, connectionSettings: TestParameters.FastReconnectSettings());
            Network networkB = new(TestParameters.RelayServerAddress, contextB, connectionSettings: TestParameters.FastReconnectSettings());

            void UpdateBoth()
            {
                networkA.ManualUpdate(Time.deltaTime);
                networkB.ManualUpdate(Time.deltaTime);
            }

            networkA.Connect(guidA);
            networkB.Connect(guidB);
            yield return TestParameters.WaitForCondition(
                () => networkA.IsConnected && networkB.IsConnected,
                TestParameters.ConnectTimeoutThreshold,
                UpdateBoth);
            networkA.SubscribeToChannel("room");
            networkB.SubscribeToChannel("room");
            yield return TestParameters.WaitForDuration(0.1f, UpdateBoth);

            GameObject ownedObject = new("OwnedByA");
            contextA.RegisterNetworkIdentity(new StubNetworkIdentity(ownedObject, "OwnedPrefab", guidA, Guid.NewGuid()));

            networkA.SendSyncIdentities();
            yield return TestParameters.WaitForCondition(
                () => FindPlayer(contextB, guidA) != null,
                TestParameters.ReceiveTimeoutThreshold,
                UpdateBoth);
            Assert.IsNotNull(FindPlayer(contextB, guidA));

            LogAssert.Expect(LogType.Warning, new Regex("^Lost connection to relay server"));
            LogAssert.Expect(LogType.Warning, new Regex("^Lost connection to relay server"));
            yield return TestParameters.StopRelayServer(UpdateBoth);
            yield return TestParameters.WaitForCondition(
                () => networkA.ConnectionState == RelayConnectionState.Reconnecting
                      && networkB.ConnectionState == RelayConnectionState.Reconnecting,
                TestParameters.DisconnectTimeoutThreshold,
                UpdateBoth);
            Assert.AreEqual(RelayConnectionState.Reconnecting, networkA.ConnectionState);
            Assert.AreEqual(RelayConnectionState.Reconnecting, networkB.ConnectionState);

            yield return TestParameters.StartRelayServer(UpdateBoth);
            yield return TestParameters.WaitForCondition(
                () => networkA.IsConnected && networkB.IsConnected,
                TestParameters.ConnectTimeoutThreshold,
                UpdateBoth);
            Assert.IsTrue(networkA.IsConnected && networkB.IsConnected, "Networks did not reconnect after the server came back.");

            Assert.AreEqual(guidA, contextA.LocalClientIdentity);
            Assert.AreEqual(guidB, contextB.LocalClientIdentity);
            Assert.AreEqual(1, contextA.NetworkIdentities.Count);
            Assert.IsFalse(ownedObject == null);
            Assert.AreEqual(1, contextB.NetworkPlayers.Count);

            // Both sides resubscribe on their own; a sync sent before the other side resubscribed would be lost.
            yield return TestParameters.WaitForDuration(0.25f, UpdateBoth);
            float timeSinceLastMessage = FindPlayer(contextB, guidA).TimeSinceLastMessage;
            networkA.SendSyncIdentities();
            yield return TestParameters.WaitForCondition(
                () => FindPlayer(contextB, guidA).TimeSinceLastMessage < timeSinceLastMessage,
                TestParameters.ReceiveTimeoutThreshold,
                UpdateBoth);
            Assert.Less(FindPlayer(contextB, guidA).TimeSinceLastMessage, timeSinceLastMessage, "Sync after reconnect did not arrive.");
            Assert.AreEqual(1, contextB.NetworkPlayers.Count);

            networkA.Disconnect();
            networkB.Disconnect();
            UnityEngine.Object.DestroyImmediate(contextA);
            UnityEngine.Object.DestroyImmediate(contextB);
        }

        private static NetworkPlayer FindPlayer(NetworkContext context, Guid playerGuid)
        {
            return context.NetworkPlayers.FirstOrDefault(player => player.Guid == playerGuid);
        }
    }
}
