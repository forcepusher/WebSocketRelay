#if !UNITY_WEBGL || UNITY_EDITOR
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
    /// <summary>
    /// Runs real clients against the real relay server through a proxy that simulates an unreliable network.
    /// </summary>
    public class ConnectionFaultTests
    {
        private const string Channel = "fault";

        private TcpFaultProxy _proxy;
        private RelayClient _relayA;
        private RelayClient _relayB;
        private TestRelayListener _listenerA;
        private TestRelayListener _listenerB;
        private Network _networkA;
        private Network _networkB;
        private NetworkContext _contextA;
        private NetworkContext _contextB;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return RelayServerLauncher.StartCoroutine();
            _proxy = new TcpFaultProxy(TestParameters.RelayServerPort);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            _relayA?.Dispose();
            _relayB?.Dispose();
            if (_networkA is { HasRelayClient: true }) _networkA.Disconnect();
            if (_networkB is { HasRelayClient: true }) _networkB.Disconnect();
            if (_contextA != null) UnityEngine.Object.DestroyImmediate(_contextA);
            if (_contextB != null) UnityEngine.Object.DestroyImmediate(_contextB);
            _proxy.Dispose();

            _relayA = _relayB = null;
            _networkA = _networkB = null;
            _contextA = _contextB = null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator StallShorterThanHeartbeatTimeout_KeepsConnectionAndDeliversHeldMessages()
        {
            yield return ConnectRelaysSubscribedToChannel();

            int receivedByA = 0;
            _listenerA.ChannelMessageReceived += (_, channel, _) => { if (channel == Channel) receivedByA++; };

            _proxy.IsStalled = true;
            for (int i = 0; i < 5; i++)
                _relayB.Send(Channel, new byte[] { (byte)i });

            yield return TestParameters.WaitForCondition(() => !_relayA.IsLinkHealthy, 1f, PollRelays);
            Assert.IsFalse(_relayA.IsLinkHealthy, "Stall was not noticed.");

            yield return TestParameters.WaitForDuration(0.5f, PollRelays);
            Assert.AreEqual(RelayConnectionState.Connected, _relayA.State);
            Assert.AreEqual(0, receivedByA);

            _proxy.IsStalled = false;
            yield return TestParameters.WaitForCondition(() => receivedByA == 5 && _relayA.IsLinkHealthy, TestParameters.ReceiveTimeoutThreshold, PollRelays);

            Assert.AreEqual(5, receivedByA);
            Assert.IsTrue(_relayA.IsLinkHealthy);
            CollectionAssert.DoesNotContain(_listenerA.States, RelayConnectionState.Reconnecting);
            Assert.AreEqual(1, _proxy.AcceptedConnectionCount);
        }

        [UnityTest]
        public IEnumerator StallLongerThanHeartbeatTimeout_ReconnectsWithSameGuidAndResubscribes()
        {
            yield return ConnectRelaysSubscribedToChannel();
            Guid guidA = _relayA.ClientGuid;

            _proxy.IsStalled = true;
            yield return TestParameters.WaitForCondition(
                () => _relayA.State == RelayConnectionState.Reconnecting,
                TestParameters.DisconnectTimeoutThreshold,
                PollRelays);
            Assert.AreEqual(RelayConnectionState.Reconnecting, _relayA.State, "Silent connection was not detected.");
            StringAssert.StartsWith("Nothing received for", _listenerA.LastReason);
            Assert.AreEqual(RelayConnectionState.Connected, _relayB.State);

            // Attempts made during the stall hang in the handshake and are cut off by the connect timeout.
            yield return TestParameters.WaitForDuration(1.5f, PollRelays);
            Assert.AreEqual(RelayConnectionState.Reconnecting, _relayA.State);

            _proxy.IsStalled = false;
            yield return TestParameters.WaitUntilRelayConnected(_relayA, TestParameters.ConnectTimeoutThreshold);
            Assert.IsTrue(_relayA.IsConnected, "Did not reconnect after the network recovered.");
            Assert.AreEqual(guidA, _relayA.ClientGuid);

            yield return AssertMessagesFlowBothWays();
        }

        [UnityTest]
        public IEnumerator ConnectionReset_ReconnectsRightAway()
        {
            yield return ConnectRelaysSubscribedToChannel();

            _proxy.ResetAllConnections();
            yield return TestParameters.WaitForCondition(
                () => _relayA.State == RelayConnectionState.Reconnecting,
                1f,
                PollRelays);
            Assert.AreEqual(RelayConnectionState.Reconnecting, _relayA.State, "Reset was not detected right away.");

            yield return TestParameters.WaitUntilRelayConnected(_relayA, 1f);
            Assert.IsTrue(_relayA.IsConnected, "Did not reconnect quickly after a reset.");
            Assert.AreEqual(2, _proxy.AcceptedConnectionCount);

            yield return AssertMessagesFlowBothWays();
        }

        [UnityTest]
        public IEnumerator RepeatedConnectionResets_EachRecovered()
        {
            yield return ConnectRelaysSubscribedToChannel();

            for (int reset = 1; reset <= 5; reset++)
            {
                _proxy.ResetAllConnections();
                yield return TestParameters.WaitForCondition(() => !_relayA.IsConnected, 1f, PollRelays);
                yield return TestParameters.WaitUntilRelayConnected(_relayA, TestParameters.ConnectTimeoutThreshold);
                Assert.IsTrue(_relayA.IsConnected, $"Did not recover from reset {reset}.");
            }

            Assert.AreEqual(0, _listenerA.States.Count(state => state == RelayConnectionState.Disconnected));
            yield return AssertMessagesFlowBothWays();
        }

        [UnityTest]
        public IEnumerator Latency_ShowsInRoundTripTime()
        {
            _proxy.LatencyMilliseconds = 100;
            yield return ConnectRelaysSubscribedToChannel();

            yield return TestParameters.WaitForCondition(
                () => _relayA.RoundTripTimeSeconds > 0d,
                TestParameters.ReceiveTimeoutThreshold,
                PollRelays);
            yield return TestParameters.WaitForDuration(1f, PollRelays);

            Assert.GreaterOrEqual(_relayA.RoundTripTimeSeconds, 0.19d);
            Assert.Less(_relayA.RoundTripTimeSeconds, 0.6d);
            Assert.Less(_relayB.RoundTripTimeSeconds, _relayA.RoundTripTimeSeconds);
            Assert.IsTrue(_relayA.IsLinkHealthy);
        }

        [UnityTest]
        public IEnumerator Network_LocalOutage_FreezesLocalRosterWhilePeersDropUs()
        {
            yield return ConnectNetworks(playerTimeoutSeconds: 2f);
            Guid guidA = _contextA.LocalClientIdentity;
            Guid guidB = _contextB.LocalClientIdentity;

            LogAssert.Expect(LogType.Warning, new Regex("^Lost connection to relay server: Nothing received for"));
            _proxy.IsStalled = true;
            yield return TestParameters.WaitForDuration(3f, UpdateNetworks);

            Assert.AreEqual(RelayConnectionState.Reconnecting, _networkA.ConnectionState);
            Assert.IsTrue(_contextA.IsConnectionInterrupted);
            Assert.IsNotNull(FindPlayer(_contextA, guidB), "Peer was removed while the local connection was down.");
            Assert.IsNull(FindPlayer(_contextB, guidA), "Silent peer was not removed on the healthy side.");

            _proxy.IsStalled = false;
            yield return TestParameters.WaitForCondition(
                () => _networkA.IsConnected && FindPlayer(_contextB, guidA) != null,
                TestParameters.ConnectTimeoutThreshold + TestParameters.ReceiveTimeoutThreshold,
                UpdateNetworks);

            Assert.IsTrue(_networkA.IsConnected);
            Assert.IsFalse(_contextA.IsConnectionInterrupted);
            Assert.AreEqual(guidA, _contextA.LocalClientIdentity);
            Assert.AreEqual(1, _contextA.NetworkPlayers.Count);
            Assert.AreEqual(1, _contextB.NetworkPlayers.Count);
        }

        [UnityTest]
        public IEnumerator Network_OutageShorterThanPlayerTimeout_PeersNeverDropEachOther()
        {
            yield return ConnectNetworks(playerTimeoutSeconds: 6f);
            Guid guidA = _contextA.LocalClientIdentity;
            Guid guidB = _contextB.LocalClientIdentity;

            bool peerWasDropped = false;
            void UpdateAndWatch()
            {
                UpdateNetworks();
                peerWasDropped |= FindPlayer(_contextA, guidB) == null || FindPlayer(_contextB, guidA) == null;
            }

            LogAssert.Expect(LogType.Warning, new Regex("^Lost connection to relay server: Nothing received for"));
            _proxy.IsStalled = true;
            yield return TestParameters.WaitForCondition(
                () => _networkA.ConnectionState == RelayConnectionState.Reconnecting,
                TestParameters.DisconnectTimeoutThreshold,
                UpdateAndWatch);
            Assert.AreEqual(RelayConnectionState.Reconnecting, _networkA.ConnectionState);

            _proxy.IsStalled = false;
            yield return TestParameters.WaitForCondition(() => _networkA.IsConnected, TestParameters.ConnectTimeoutThreshold, UpdateAndWatch);
            Assert.IsTrue(_networkA.IsConnected);

            yield return TestParameters.WaitForCondition(
                () => FindPlayer(_contextB, guidA).TimeSinceLastMessage < 0.5f,
                TestParameters.ReceiveTimeoutThreshold,
                UpdateAndWatch);

            Assert.IsFalse(peerWasDropped, "A peer was dropped although the outage was shorter than the player timeout.");
            Assert.Less(FindPlayer(_contextB, guidA).TimeSinceLastMessage, 0.5f, "Syncs did not resume after reconnecting.");
            Assert.AreEqual(1, _contextA.NetworkPlayers.Count);
            Assert.AreEqual(1, _contextB.NetworkPlayers.Count);
        }

        private IEnumerator ConnectRelaysSubscribedToChannel()
        {
            _listenerA = new TestRelayListener();
            _listenerB = new TestRelayListener();
            _relayA = new RelayClient(_proxy.ServerAddress, _listenerA, Guid.NewGuid(), settings: TestParameters.FastReconnectSettings());
            _relayB = new RelayClient(TestParameters.RelayServerAddress, _listenerB, Guid.NewGuid(), settings: TestParameters.FastReconnectSettings());
            _relayA.Connect();
            _relayB.Connect();
            yield return TestParameters.WaitUntilRelayConnected(_relayA, _relayB);
            Assert.IsTrue(_relayA.IsConnected && _relayB.IsConnected, "Relay clients did not connect.");

            _relayA.SubscribeToChannel(Channel);
            _relayB.SubscribeToChannel(Channel);
            yield return AssertMessagesFlowBothWays();
        }

        /// <summary>
        /// The relay does not acknowledge subscriptions, so probe until messages arrive in both directions.
        /// </summary>
        private IEnumerator AssertMessagesFlowBothWays()
        {
            const float probeIntervalSeconds = 0.05f;

            Guid senderSeenByA = Guid.Empty;
            Guid senderSeenByB = Guid.Empty;
            Action<Guid, string, byte[]> onReceivedByA = (senderGuid, channel, _) => { if (channel == Channel) senderSeenByA = senderGuid; };
            Action<Guid, string, byte[]> onReceivedByB = (senderGuid, channel, _) => { if (channel == Channel) senderSeenByB = senderGuid; };
            _listenerA.ChannelMessageReceived += onReceivedByA;
            _listenerB.ChannelMessageReceived += onReceivedByB;

            float elapsedSeconds = 0f;
            float nextProbeSeconds = 0f;
            while ((senderSeenByA == Guid.Empty || senderSeenByB == Guid.Empty) && elapsedSeconds < TestParameters.ReceiveTimeoutThreshold)
            {
                if (elapsedSeconds >= nextProbeSeconds)
                {
                    if (senderSeenByB == Guid.Empty)
                        _relayA.Send(Channel, new byte[] { 0xA1 });
                    if (senderSeenByA == Guid.Empty)
                        _relayB.Send(Channel, new byte[] { 0xB1 });
                    nextProbeSeconds = elapsedSeconds + probeIntervalSeconds;
                }

                PollRelays();
                yield return null;
                elapsedSeconds += Time.deltaTime;
            }

            // Lets probes still in flight arrive before the test starts counting messages.
            yield return TestParameters.WaitForDuration(0.1f, PollRelays);
            _listenerA.ChannelMessageReceived -= onReceivedByA;
            _listenerB.ChannelMessageReceived -= onReceivedByB;

            Assert.AreEqual(_relayB.ClientGuid, senderSeenByA, "A did not receive from B.");
            Assert.AreEqual(_relayA.ClientGuid, senderSeenByB, "B did not receive from A.");
        }

        private void PollRelays()
        {
            _relayA.ProcessIncomingMessages();
            _relayB.ProcessIncomingMessages();
        }

        private IEnumerator ConnectNetworks(float playerTimeoutSeconds)
        {
            _contextA = NetworkContextTestHelpers.CreateContext(playerTimeoutSeconds);
            _contextB = NetworkContextTestHelpers.CreateContext(playerTimeoutSeconds);
            _networkA = new Network(_proxy.ServerAddress, _contextA, connectionSettings: TestParameters.FastReconnectSettings());
            _networkB = new Network(TestParameters.RelayServerAddress, _contextB, connectionSettings: TestParameters.FastReconnectSettings());

            _networkA.Connect(Guid.NewGuid());
            _networkB.Connect(Guid.NewGuid());
            yield return TestParameters.WaitForCondition(
                () => _networkA.IsConnected && _networkB.IsConnected,
                TestParameters.ConnectTimeoutThreshold,
                UpdateNetworks);
            Assert.IsTrue(_networkA.IsConnected && _networkB.IsConnected, "Networks did not connect.");

            _networkA.SubscribeToChannel(Channel);
            _networkB.SubscribeToChannel(Channel);
            yield return TestParameters.WaitForCondition(
                () => _contextA.NetworkPlayers.Count == 1
                      && _contextB.NetworkPlayers.Count == 1,
                TestParameters.ReceiveTimeoutThreshold,
                UpdateNetworks);
            Assert.AreEqual(1, _contextA.NetworkPlayers.Count, "A does not see B.");
            Assert.AreEqual(1, _contextB.NetworkPlayers.Count, "B does not see A.");
        }

        private void UpdateNetworks()
        {
            _networkA.SendSyncIdentities();
            _networkB.SendSyncIdentities();
            _networkA.ManualUpdate(Time.deltaTime);
            _networkB.ManualUpdate(Time.deltaTime);
        }

        private static NetworkPlayer FindPlayer(NetworkContext context, Guid playerGuid)
        {
            return context.NetworkPlayers.FirstOrDefault(player => player.Guid == playerGuid);
        }
    }
}
#endif
