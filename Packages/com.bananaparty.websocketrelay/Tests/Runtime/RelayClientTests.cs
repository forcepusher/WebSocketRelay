using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BananaParty.WebSocketRelay.Transport;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BananaParty.WebSocketRelay.Tests
{
    /// <summary>
    /// Relay clients talking through the real relay server.
    /// </summary>
    public class RelayClientTests
    {
        private RelayClient _relayA;
        private RelayClient _relayB;
        private RelayClient _relayC;
        private TestRelayListener _listenerA;
        private TestRelayListener _listenerB;
        private TestRelayListener _listenerC;
        private int _previousTargetFrameRate;
        private int _previousVSyncCount;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _previousTargetFrameRate = Application.targetFrameRate;
            _previousVSyncCount = QualitySettings.vSyncCount;
            yield return RelayServerLauncher.StartCoroutine();
        }

        [TearDown]
        public void TearDown()
        {
            _relayA?.Dispose();
            _relayB?.Dispose();
            _relayC?.Dispose();
            _relayA = _relayB = _relayC = null;
            _listenerA = _listenerB = _listenerC = null;
            Application.targetFrameRate = _previousTargetFrameRate;
            QualitySettings.vSyncCount = _previousVSyncCount;
        }

        [Test]
        public void ClientUsesProvidedGuid()
        {
            Guid expectedGuid = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

            _relayA = new RelayClient(TestParameters.RelayServerAddress, new TestRelayListener(), expectedGuid);

            Assert.AreEqual(expectedGuid, _relayA.ClientGuid);
        }

        [UnityTest]
        public IEnumerator Connect_NoServerHandshakeChannelMessages()
        {
            _relayA = CreateRelay(out _listenerA);
            bool isChannelMessageReceived = false;
            _listenerA.ChannelMessageReceived += (_, _, _) => isChannelMessageReceived = true;

            yield return Connect(_relayA);
            yield return TestParameters.WaitForDuration(0.25f, PollAll);

            Assert.IsFalse(isChannelMessageReceived);
        }

        [UnityTest]
        public IEnumerator Subscribe_NoServerConfirmation()
        {
            _relayA = CreateRelay(out _listenerA);
            bool isChannelMessageReceived = false;
            _listenerA.ChannelMessageReceived += (_, _, _) => isChannelMessageReceived = true;
            yield return Connect(_relayA);

            _relayA.SubscribeToChannel("no-ack");
            Assert.IsTrue(_relayA.SubscribedChannels.Contains("no-ack"));
            yield return TestParameters.WaitForDuration(0.25f, PollAll);

            Assert.IsFalse(isChannelMessageReceived);
        }

        [UnityTest]
        public IEnumerator ChannelMessageIncludesSenderGuid()
        {
            yield return ConnectSubscribed("guid-test", clientCount: 2);

            Guid receivedSenderGuid = Guid.Empty;
            _listenerB.ChannelMessageReceived += (senderGuid, channel, _) =>
            {
                if (channel == "guid-test")
                    receivedSenderGuid = senderGuid;
            };

            _relayA.Send("guid-test", new byte[] { 0x01 });
            yield return TestParameters.WaitForCondition(() => receivedSenderGuid != Guid.Empty, TestParameters.ReceiveTimeoutThreshold, PollAll);

            Assert.AreEqual(_relayA.ClientGuid, receivedSenderGuid);
        }

        [UnityTest]
        public IEnumerator TwoClients_MessageRelay() => AssertChannelMessageReachesEveryOtherClient(clientCount: 2);

        [UnityTest]
        public IEnumerator ThreeClients_AllReceive() => AssertChannelMessageReachesEveryOtherClient(clientCount: 3);

        [UnityTest]
        public IEnumerator DifferentChannels_Isolated()
        {
            yield return ConnectRelays(clientCount: 2);
            _relayA.SubscribeToChannel("alpha");
            _relayB.SubscribeToChannel("beta");
            yield return WaitForSubscriptionChanges();

            bool isReceivedByB = false;
            _listenerB.ChannelMessageReceived += (_, _, _) => isReceivedByB = true;

            _relayA.Send("alpha", new byte[] { 0xAA });
            yield return TestParameters.WaitForDuration(1f, PollAll);

            Assert.IsFalse(isReceivedByB, "Client B received a message from a channel it is not subscribed to.");
        }

        [UnityTest]
        public IEnumerator MultipleChannels_SubscribeAndSwitch()
        {
            yield return ConnectRelays(clientCount: 2);
            _relayA.SubscribeToChannel("channel-a");
            _relayB.SubscribeToChannel("channel-a");
            _relayB.SubscribeToChannel("channel-b");
            yield return WaitForSubscriptionChanges();

            List<string> channelsReceivedByA = new();
            List<string> channelsReceivedByB = new();
            _listenerA.ChannelMessageReceived += (_, channel, _) => channelsReceivedByA.Add(channel);
            _listenerB.ChannelMessageReceived += (_, channel, _) => channelsReceivedByB.Add(channel);

            _relayA.Send("channel-a", new byte[] { 0xCC });
            yield return TestParameters.WaitForCondition(() => channelsReceivedByB.Contains("channel-a"), TestParameters.ReceiveTimeoutThreshold, PollAll);
            Assert.Contains("channel-a", channelsReceivedByB, "B did not receive the channel-a message.");

            _relayB.Send("channel-b", new byte[] { 0xDD });
            yield return TestParameters.WaitForDuration(1f, PollAll);
            Assert.IsEmpty(channelsReceivedByA, "A received a message from a channel it is not subscribed to.");

            _relayA.SubscribeToChannel("channel-b");
            yield return WaitForSubscriptionChanges();

            _relayB.Send("channel-b", new byte[] { 0xEE });
            yield return TestParameters.WaitForCondition(() => channelsReceivedByA.Contains("channel-b"), TestParameters.ReceiveTimeoutThreshold, PollAll);
            Assert.Contains("channel-b", channelsReceivedByA, "A did not receive the channel-b message after subscribing.");
        }

        [UnityTest]
        public IEnumerator UnsubscribeStopsReceiving()
        {
            yield return ConnectSubscribed("shared", clientCount: 2);

            int receivedByB = 0;
            _listenerB.ChannelMessageReceived += (_, channel, _) => { if (channel == "shared") receivedByB++; };

            _relayA.Send("shared", new byte[] { 0xEE });
            yield return TestParameters.WaitForCondition(() => receivedByB == 1, TestParameters.ReceiveTimeoutThreshold, PollAll);
            Assert.AreEqual(1, receivedByB, "B did not receive before unsubscribing.");

            _relayB.UnsubscribeFromChannel("shared");
            yield return WaitForSubscriptionChanges();

            _relayA.Send("shared", new byte[] { 0xFF });
            yield return TestParameters.WaitForDuration(1f, PollAll);
            Assert.AreEqual(1, receivedByB, "B received a message after unsubscribing.");
        }

        [UnityTest]
        public IEnumerator SendAfterUnsubscribe_ThrowsKeyNotFoundException()
        {
            _relayA = CreateRelay(out _listenerA);
            yield return Connect(_relayA);

            _relayA.SubscribeToChannel("temp");
            _relayA.UnsubscribeFromChannel("temp");

            Assert.Throws<KeyNotFoundException>(() => _relayA.Send("temp", new byte[] { 0x01 }));
        }

        [UnityTest]
        public IEnumerator EmptyPayload_Relays()
        {
            yield return ConnectSubscribed("empty", clientCount: 2);

            byte[] received = null;
            _listenerB.ChannelMessageReceived += (_, channel, data) => { if (channel == "empty") received = data; };

            _relayA.Send("empty", Array.Empty<byte>());
            yield return TestParameters.WaitForCondition(() => received != null, TestParameters.ReceiveTimeoutThreshold, PollAll);

            Assert.IsNotNull(received, "Empty message was not received.");
            Assert.AreEqual(0, received.Length);
        }

        [UnityTest]
        public IEnumerator LargePayload_Relays()
        {
            yield return ConnectSubscribed("large", clientCount: 2);

            byte[] sent = GenerateRandomBytes(40_000);
            byte[] received = null;
            _listenerB.ChannelMessageReceived += (_, channel, data) => { if (channel == "large") received = data; };

            _relayA.Send("large", sent);
            yield return TestParameters.WaitForCondition(() => received != null, TestParameters.ReceiveTimeoutThreshold, PollAll);

            CollectionAssert.AreEqual(sent, received);
        }

        [UnityTest]
        public IEnumerator RapidMessages_AllDeliveredInOrder()
        {
            const int messageCount = 50;
            yield return ConnectSubscribed("rapid", clientCount: 2);

            List<byte> received = new();
            _listenerB.ChannelMessageReceived += (_, channel, data) => { if (channel == "rapid") received.Add(data[0]); };

            for (int index = 0; index < messageCount; index++)
                _relayA.Send("rapid", new[] { (byte)index });

            yield return TestParameters.WaitForCondition(() => received.Count >= messageCount, TestParameters.ReceiveTimeoutThreshold, PollAll);

            CollectionAssert.AreEqual(Enumerable.Range(0, messageCount).Select(index => (byte)index), received);
        }

        [UnityTest]
        public IEnumerator BurstOfMessages_IsDeliveredWithinAFewFrames()
        {
            // At a real frame rate a socket that moves one message per frame needs hundreds of frames for this.
            const int messageCount = 200;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 30;
            yield return ConnectSubscribed("burst", clientCount: 2);

            int receivedCount = 0;
            _listenerB.ChannelMessageReceived += (_, channel, _) => { if (channel == "burst") receivedCount++; };

            for (int index = 0; index < messageCount; index++)
                _relayA.Send("burst", new byte[1000]);

            int frameCount = 0;
            while (receivedCount < messageCount && frameCount < 300)
            {
                yield return null;
                frameCount++;
                PollAll();
            }

            Assert.AreEqual(messageCount, receivedCount);
            Assert.Less(frameCount, 15, "The burst was delivered at the pace of the frame rate instead of the connection.");
        }

        [UnityTest]
        public IEnumerator DisposeWhileConnected_DoesNotCallDisconnected()
        {
            _relayA = CreateRelay(out _listenerA);
            int disconnectCount = 0;
            _listenerA.Disconnected += () => disconnectCount++;
            yield return Connect(_relayA);

            _relayA.Dispose();

            Assert.AreEqual(0, disconnectCount);
        }

        [Test]
        public void DisposeBeforeConnect_DoesNotCallDisconnected()
        {
            _relayA = CreateRelay(out _listenerA);
            int disconnectCount = 0;
            _listenerA.Disconnected += () => disconnectCount++;

            _relayA.Dispose();

            Assert.AreEqual(0, disconnectCount);
        }

        [UnityTest]
        public IEnumerator ServerStop_ReconnectDisabled_CallsDisconnectedOnce()
        {
            _relayA = CreateRelay(out _listenerA, TestParameters.ReconnectDisabledSettings());
            int disconnectCount = 0;
            _listenerA.Disconnected += () => disconnectCount++;
            yield return Connect(_relayA);

            yield return RelayServerLauncher.StopCoroutine();
            yield return TestParameters.WaitForCondition(() => disconnectCount > 0, TestParameters.DisconnectTimeoutThreshold, PollAll);

            Assert.AreEqual(1, disconnectCount);
            CollectionAssert.AreEqual(
                new[] { RelayConnectionState.Connecting, RelayConnectionState.Connected, RelayConnectionState.Disconnected },
                _listenerA.States);
            Assert.IsNotEmpty(_listenerA.LastReason);

            PollAll();
            PollAll();
            Assert.AreEqual(1, disconnectCount, "Disconnected was reported again while polling.");
            Assert.DoesNotThrow(() => _relayA.Dispose());
        }

        [UnityTest]
        public IEnumerator ServerStop_DisposeDoesNotThrow()
        {
            _relayA = CreateRelay(out _listenerA);
            yield return Connect(_relayA);

            yield return RelayServerLauncher.StopCoroutine();
            yield return TestParameters.WaitForDuration(0.5f, PollAll);

            Assert.DoesNotThrow(() => _relayA.Dispose());
        }

        [UnityTest]
        public IEnumerator ServerRestart_ReconnectsWithSameGuidAndResubscribes()
        {
            _relayA = CreateRelay(out _listenerA, TestParameters.FastReconnectSettings());
            _relayB = CreateRelay(out _listenerB, TestParameters.FastReconnectSettings());
            Guid guidA = _relayA.ClientGuid;
            Guid guidB = _relayB.ClientGuid;
            yield return Connect(_relayA, _relayB);
            _relayA.SubscribeToChannel("resume");
            _relayB.SubscribeToChannel("resume");

            yield return TestParameters.StopRelayServer(PollAll);
            yield return TestParameters.WaitForCondition(
                () => _relayA.State == RelayConnectionState.Reconnecting && _relayB.State == RelayConnectionState.Reconnecting,
                TestParameters.DisconnectTimeoutThreshold,
                PollAll);
            Assert.AreEqual(RelayConnectionState.Reconnecting, _relayA.State);
            Assert.AreEqual(RelayConnectionState.Reconnecting, _relayB.State);
            Assert.IsFalse(_relayA.Send("resume", new byte[] { 0x01 }));

            yield return TestParameters.StartRelayServer(PollAll);
            yield return TestParameters.WaitUntilRelayConnected(_relayA, _relayB, TestParameters.ConnectTimeoutThreshold);
            Assert.IsTrue(_relayA.IsConnected && _relayB.IsConnected, "Clients did not reconnect after the server came back.");
            Assert.AreEqual(guidA, _relayA.ClientGuid);
            Assert.AreEqual(guidB, _relayB.ClientGuid);
            Assert.AreEqual(0, _listenerA.States.Count(state => state == RelayConnectionState.Disconnected));

            Guid senderSeenByA = Guid.Empty;
            Guid senderSeenByB = Guid.Empty;
            _listenerA.ChannelMessageReceived += (senderGuid, channel, _) => { if (channel == "resume") senderSeenByA = senderGuid; };
            _listenerB.ChannelMessageReceived += (senderGuid, channel, _) => { if (channel == "resume") senderSeenByB = senderGuid; };

            // Both clients resubscribe on their own, and a message sent before the other side resubscribed would be lost.
            yield return TestParameters.WaitForDuration(0.25f, PollAll);
            Assert.IsTrue(_relayA.Send("resume", new byte[] { 0x02 }));
            Assert.IsTrue(_relayB.Send("resume", new byte[] { 0x03 }));
            yield return TestParameters.WaitForCondition(() => senderSeenByA != Guid.Empty && senderSeenByB != Guid.Empty, TestParameters.ReceiveTimeoutThreshold, PollAll);

            Assert.AreEqual(guidA, senderSeenByB);
            Assert.AreEqual(guidB, senderSeenByA);
        }

        [UnityTest]
        public IEnumerator ChannelMessage_NotEchoedToSender()
        {
            yield return ConnectSubscribed("echo", clientCount: 2);

            int receivedByA = 0;
            int receivedByB = 0;
            _listenerA.ChannelMessageReceived += (_, channel, _) => { if (channel == "echo") receivedByA++; };
            _listenerB.ChannelMessageReceived += (_, channel, _) => { if (channel == "echo") receivedByB++; };

            _relayA.Send("echo", new byte[] { 0x01 });
            yield return TestParameters.WaitForDuration(0.5f, PollAll);

            Assert.AreEqual(1, receivedByB);
            Assert.AreEqual(0, receivedByA, "Sender received its own message.");
        }

        [UnityTest]
        public IEnumerator Heartbeat_MeasuresRoundTripTime()
        {
            _relayA = CreateRelay(out _listenerA, TestParameters.FastReconnectSettings());
            yield return Connect(_relayA);

            yield return TestParameters.WaitForCondition(() => _relayA.RoundTripTimeSeconds > 0d, TestParameters.ReceiveTimeoutThreshold, PollAll);

            Assert.Greater(_relayA.RoundTripTimeSeconds, 0d);
            Assert.Less(_relayA.RoundTripTimeSeconds, 0.5d);
            Assert.IsTrue(_relayA.IsLinkHealthy);
        }

        [UnityTest]
        public IEnumerator Connect_NothingListening_DisconnectsWithReason()
        {
            _listenerA = new TestRelayListener();
            _relayA = new RelayClient($"ws://127.0.0.1:{GetUnusedPort()}", _listenerA, Guid.NewGuid());
            _relayA.Connect();

            yield return TestParameters.WaitForCondition(() => _relayA.State == RelayConnectionState.Disconnected, TestParameters.ConnectTimeoutThreshold, PollAll);

            CollectionAssert.AreEqual(new[] { RelayConnectionState.Connecting, RelayConnectionState.Disconnected }, _listenerA.States);
            Assert.IsNotEmpty(_listenerA.LastReason);
        }

        private IEnumerator AssertChannelMessageReachesEveryOtherClient(int clientCount)
        {
            const string channel = "relay-100";
            yield return ConnectSubscribed(channel, clientCount);

            byte[] sent = GenerateRandomBytes(64);
            List<byte[]> received = new();
            _listenerB.ChannelMessageReceived += (_, receivedChannel, data) => { if (receivedChannel == channel) received.Add(data); };
            if (_listenerC != null)
                _listenerC.ChannelMessageReceived += (_, receivedChannel, data) => { if (receivedChannel == channel) received.Add(data); };

            _relayA.Send(channel, sent);
            yield return TestParameters.WaitForCondition(() => received.Count >= clientCount - 1, TestParameters.ReceiveTimeoutThreshold, PollAll);

            Assert.AreEqual(clientCount - 1, received.Count, $"Expected {clientCount - 1} receivers.");
            foreach (byte[] data in received)
                CollectionAssert.AreEqual(sent, data);
        }

        private IEnumerator ConnectSubscribed(string channel, int clientCount)
        {
            yield return ConnectRelays(clientCount);
            _relayA.SubscribeToChannel(channel);
            _relayB.SubscribeToChannel(channel);
            _relayC?.SubscribeToChannel(channel);
            yield return WaitForSubscriptionChanges();
        }

        private IEnumerator ConnectRelays(int clientCount)
        {
            _relayA = CreateRelay(out _listenerA);
            _relayB = CreateRelay(out _listenerB);
            if (clientCount >= 3)
                _relayC = CreateRelay(out _listenerC);

            yield return _relayC == null ? Connect(_relayA, _relayB) : Connect(_relayA, _relayB, _relayC);
        }

        private IEnumerator Connect(params RelayClient[] relays)
        {
            foreach (RelayClient relay in relays)
                relay.Connect();

            yield return TestParameters.WaitForCondition(() => relays.All(relay => relay.IsConnected), TestParameters.ConnectTimeoutThreshold, PollAll);
            Assert.IsTrue(relays.All(relay => relay.IsConnected), "Relay clients did not connect.");
        }

        // The relay does not acknowledge subscription changes, and messages from different
        // connections are not ordered, so give a change time to reach the relay before another client publishes.
        private IEnumerator WaitForSubscriptionChanges()
        {
            yield return TestParameters.WaitForDuration(0.1f, PollAll);
        }

        private void PollAll()
        {
            _relayA?.ProcessIncomingMessages();
            _relayB?.ProcessIncomingMessages();
            _relayC?.ProcessIncomingMessages();
        }

        private static RelayClient CreateRelay(out TestRelayListener listener, RelayConnectionSettings settings = null)
        {
            listener = new TestRelayListener();
            return new RelayClient(TestParameters.RelayServerAddress, listener, Guid.NewGuid(), settings: settings);
        }

        private static int GetUnusedPort()
        {
            System.Net.Sockets.TcpListener listener = new(System.Net.IPAddress.Loopback, 0);
            listener.Start();
            int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private static byte[] GenerateRandomBytes(int length)
        {
            System.Random random = new();
            byte[] bytes = new byte[length];
            random.NextBytes(bytes);
            return bytes;
        }
    }
}
