using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BananaParty.WebSocketRelay.Transport;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace BananaParty.WebSocketRelay.Tests
{
    public class RelayClientTests
    {
        private RelayClient _relayA;
        private RelayClient _relayB;
        private RelayClient _relayC;
        private TestRelayListener _listenerA;
        private TestRelayListener _listenerB;
        private TestRelayListener _listenerC;

        [UnitySetUp]
        public IEnumerator Setup()
        {
            yield return RelayServerLauncher.StartCoroutine();
        }

        [UnityTest] public IEnumerator ClientHasGuidOnCreation() => TestClientHasGuidOnCreation();
        [UnityTest] public IEnumerator ClientUsesProvidedGuid() => TestClientUsesProvidedGuid();
        [UnityTest] public IEnumerator Connect_NoServerHandshakeMessages() => TestConnectNoServerHandshakeMessages();
        [UnityTest] public IEnumerator Subscribe_NoServerConfirmation() => TestSubscribeNoServerConfirmation();
        [UnityTest] public IEnumerator TopicMessageIncludesSenderGuid() => TestTopicMessageIncludesSenderGuid();
        [UnityTest] public IEnumerator TwoClients_MessageRelay() => TestTopicMessage("relay-100", 2);
        [UnityTest] public IEnumerator ThreeClients_AllReceive() => TestTopicMessage("relay-100", 3);
        [UnityTest] public IEnumerator DifferentTopics_Isolated() => TestTopicIsolation();
        [UnityTest] public IEnumerator MultipleTopics_SubscribeAndSwitch() => TestMultiTopicSubscribe();
        [UnityTest] public IEnumerator SameTopicDifferentNames_AreIsolated() => TestDifferentTopicNames();
        [UnityTest] public IEnumerator UnsubscribeStopsReceiving() => TestUnsubscribeStopsReceiving();
        [UnityTest] public IEnumerator SendAfterUnsubscribe_ThrowsKeyNotFoundException() => TestSendAfterUnsubscribeThrows();
        [UnityTest] public IEnumerator EmptyPayload_Relays() => TestEmptyMessage();
        [UnityTest] public IEnumerator LargePayload_Relays() => TestLargeMessage();
        [UnityTest] public IEnumerator RapidMessages_AllDelivered() => TestRapidMessages(50);
        [UnityTest] public IEnumerator DisposeWhileConnected_CallsDisconnected() => TestDisposeWhileConnectedCallsDisconnected();
        [UnityTest] public IEnumerator DisposeBeforeConnect_DoesNotCallDisconnected() => TestDisposeBeforeConnectDoesNotCallDisconnected();
        [UnityTest] public IEnumerator ServerStop_CallsDisconnected() => TestServerStopCallsDisconnected();
        [UnityTest] public IEnumerator Disconnect_NotCalledTwice() => TestDisconnectNotCalledTwice();

        private IEnumerator TestClientHasGuidOnCreation()
        {
            _relayA = CreateRelay(out _listenerA);
            Assert.AreNotEqual(Guid.Empty, _relayA.ClientGuid);

            Cleanup();
            yield return null;
        }

        private IEnumerator TestClientUsesProvidedGuid()
        {
            Guid expectedGuid = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
            _listenerA = new TestRelayListener();
            _relayA = new RelayClient($"ws://localhost:{TestParameters.RelayServerPort}", _listenerA, expectedGuid);

            Assert.AreEqual(expectedGuid, _relayA.ClientGuid);

            Cleanup();
            yield return null;
        }

        private IEnumerator TestConnectNoServerHandshakeMessages()
        {
            bool connectedCallback = false;
            bool topicMessageReceived = false;

            _listenerA = new TestRelayListener();
            _listenerA.Connected += () => connectedCallback = true;
            _listenerA.TopicMessageReceived += (_, _, _) => topicMessageReceived = true;

            _relayA = new RelayClient($"ws://localhost:{TestParameters.RelayServerPort}", _listenerA, Guid.NewGuid());
            _relayA.Connect();

            yield return new WaitWhile(() => !_relayA.IsConnected, TestParameters.ConnectTimeoutThreshold);

            yield return TestParameters.WaitForDuration(0.25f, () => _relayA.ProcessIncomingMessages());

            Assert.IsFalse(connectedCallback);
            Assert.IsFalse(topicMessageReceived);

            Cleanup();
            yield return null;
        }

        private IEnumerator TestSubscribeNoServerConfirmation()
        {
            bool topicMessageReceived = false;

            _relayA = CreateRelay(out _listenerA);
            _listenerA.TopicMessageReceived += (_, _, _) => topicMessageReceived = true;
            _relayA.Connect();

            yield return new WaitWhile(() => !_relayA.IsConnected, TestParameters.ConnectTimeoutThreshold);

            _relayA.SubscribeToTopic("no-ack");
            Assert.IsTrue(_relayA.SubscribedTopics.Contains("no-ack"));

            yield return TestParameters.WaitForDuration(0.25f, () => _relayA.ProcessIncomingMessages());

            Assert.IsFalse(topicMessageReceived);

            Cleanup();
            yield return null;
        }

        private IEnumerator TestTopicMessageIncludesSenderGuid()
        {
            _relayA = CreateRelay(out _listenerA);
            _relayB = CreateRelay(out _listenerB);
            _relayA.Connect();
            _relayB.Connect();
            yield return new WaitWhile(() => !_relayA.IsConnected || !_relayB.IsConnected, TestParameters.ConnectTimeoutThreshold);

            _relayA.SubscribeToTopic("guid-test");
            _relayB.SubscribeToTopic("guid-test");
            _relayA.ProcessIncomingMessages();
            _relayB.ProcessIncomingMessages();
            yield return null;

            Guid receivedSenderId = Guid.Empty;
            _listenerB.TopicMessageReceived += (senderId, topic, _) =>
            {
                if (topic != "guid-test")
                    return;

                receivedSenderId = senderId;
            };

            _relayA.Send("guid-test", new byte[] { 0x01 });
            yield return TestParameters.WaitForCondition(
                () => receivedSenderId != Guid.Empty,
                TestParameters.ReceiveTimeoutThreshold,
                () => _relayB.ProcessIncomingMessages());

            Assert.AreEqual(_relayA.ClientGuid, receivedSenderId);

            Cleanup();
        }

        private IEnumerator TestTopicMessage(string topic, int clientCount)
        {
            _relayA = CreateRelay(out _listenerA);
            _relayB = CreateRelay(out _listenerB);
            if (clientCount >= 3) _relayC = CreateRelay(out _listenerC);

            _relayA.Connect();
            _relayB.Connect();
            if (clientCount >= 3) _relayC.Connect();

            yield return new WaitWhile(() => !_relayA.IsConnected || !_relayB.IsConnected, TestParameters.ConnectTimeoutThreshold);
            if (clientCount >= 3)
                yield return new WaitWhile(() => !_relayC.IsConnected, TestParameters.ConnectTimeoutThreshold);

            _relayA.SubscribeToTopic(topic);
            _relayB.SubscribeToTopic(topic);
            if (clientCount >= 3) _relayC.SubscribeToTopic(topic);

            _relayA.ProcessIncomingMessages();
            _relayB.ProcessIncomingMessages();
            if (clientCount >= 3) _relayC.ProcessIncomingMessages();
            yield return null;

            byte[] sent = GenerateRandomBytes(64);
            int recvCount = 0;
            byte[] receivedData = null;

            _listenerB.TopicMessageReceived += (_, receivedTopic, data) =>
            {
                if (receivedTopic != topic) return;
                recvCount++;
                receivedData = data;
            };
            if (clientCount >= 3)
            {
                _listenerC.TopicMessageReceived += (_, receivedTopic, data) =>
                {
                    if (receivedTopic != topic) return;
                    recvCount++;
                    receivedData = data;
                };
            }

            _relayA.Send(topic, sent);

            yield return TestParameters.WaitForCondition(
                () => recvCount >= clientCount - 1,
                TestParameters.ReceiveTimeoutThreshold,
                () =>
                {
                    _relayB.ProcessIncomingMessages();
                    if (clientCount >= 3)
                        _relayC.ProcessIncomingMessages();
                });

            Assert.AreEqual(clientCount - 1, recvCount, $"Expected {clientCount - 1} receivers, got {recvCount}.");
            Assert.IsNotNull(receivedData);
            Assert.True(sent.SequenceEqual(receivedData));

            Cleanup();
        }

        private IEnumerator TestTopicIsolation()
        {
            _relayA = CreateRelay(out _listenerA);
            _relayB = CreateRelay(out _listenerB);
            _relayA.Connect();
            _relayB.Connect();
            yield return new WaitWhile(() => !_relayA.IsConnected || !_relayB.IsConnected, TestParameters.ConnectTimeoutThreshold);

            _relayA.SubscribeToTopic("alpha");
            _relayB.SubscribeToTopic("beta");

            _relayA.ProcessIncomingMessages();
            _relayB.ProcessIncomingMessages();
            yield return null;

            bool bReceived = false;
            _listenerB.TopicMessageReceived += (_, topic, _) => { if (topic == "beta") bReceived = true; };

            _relayA.Send("alpha", new byte[] { 0xAA });

            yield return TestParameters.WaitForDuration(1f, () => _relayB.ProcessIncomingMessages());
            Assert.IsFalse(bReceived, "Client B received message from a topic it is not subscribed to.");

            Cleanup();
        }

        private IEnumerator TestMultiTopicSubscribe()
        {
            _relayA = CreateRelay(out _listenerA);
            _relayB = CreateRelay(out _listenerB);
            _relayA.Connect();
            _relayB.Connect();
            yield return new WaitWhile(() => !_relayA.IsConnected || !_relayB.IsConnected, TestParameters.ConnectTimeoutThreshold);

            _relayA.SubscribeToTopic("topic-a");
            _relayB.SubscribeToTopic("topic-a");
            _relayB.SubscribeToTopic("topic-b");

            _relayA.ProcessIncomingMessages();
            _relayB.ProcessIncomingMessages();
            yield return null;

            bool bGotA = false;
            _listenerB.TopicMessageReceived += (_, topic, _) => { if (topic == "topic-a") bGotA = true; };
            _relayA.Send("topic-a", new byte[] { 0xCC });
            yield return TestParameters.WaitForCondition(
                () => bGotA,
                TestParameters.ReceiveTimeoutThreshold,
                () => _relayB.ProcessIncomingMessages());
            Assert.IsTrue(bGotA, "B did not receive topic-a message.");

            bool aGotB = false;
            _listenerA.TopicMessageReceived += (_, topic, _) => { if (topic == "topic-b") aGotB = true; };
            _relayB.Send("topic-b", new byte[] { 0xDD });
            yield return TestParameters.WaitForDuration(1f, () => _relayA.ProcessIncomingMessages());
            Assert.IsFalse(aGotB, "A received message from a topic it is not subscribed to.");

            _relayA.SubscribeToTopic("topic-b");
            _relayA.ProcessIncomingMessages();
            yield return null;

            bool aGotFromB = false;
            _listenerA.TopicMessageReceived += (_, topic, _) => { if (topic == "topic-b") aGotFromB = true; };
            _relayB.Send("topic-b", new byte[] { 0xEE });
            yield return TestParameters.WaitForCondition(
                () => aGotFromB,
                TestParameters.ReceiveTimeoutThreshold,
                () => _relayA.ProcessIncomingMessages());
            Assert.IsTrue(aGotFromB, "A did not receive topic-b message after subscribing.");

            Cleanup();
        }

        private IEnumerator TestDifferentTopicNames()
        {
            _relayA = CreateRelay(out _listenerA);
            _relayB = CreateRelay(out _listenerB);
            _relayA.Connect();
            _relayB.Connect();
            yield return new WaitWhile(() => !_relayA.IsConnected || !_relayB.IsConnected, TestParameters.ConnectTimeoutThreshold);

            _relayA.SubscribeToTopic("one");
            _relayB.SubscribeToTopic("two");

            _relayA.ProcessIncomingMessages();
            _relayB.ProcessIncomingMessages();
            yield return null;

            bool bReceived = false;
            _listenerB.TopicMessageReceived += (_, topic, _) => { if (topic == "two") bReceived = true; };
            _relayA.Send("one", new byte[] { 0xDD });

            yield return TestParameters.WaitForDuration(1f, () => _relayB.ProcessIncomingMessages());
            Assert.IsFalse(bReceived);

            Cleanup();
        }

        private IEnumerator TestUnsubscribeStopsReceiving()
        {
            _relayA = CreateRelay(out _listenerA);
            _relayB = CreateRelay(out _listenerB);
            _relayA.Connect();
            _relayB.Connect();
            yield return new WaitWhile(() => !_relayA.IsConnected || !_relayB.IsConnected, TestParameters.ConnectTimeoutThreshold);

            _relayA.SubscribeToTopic("shared");
            _relayB.SubscribeToTopic("shared");

            _relayA.ProcessIncomingMessages();
            _relayB.ProcessIncomingMessages();
            yield return null;

            bool bReceivedFirst = false;
            _listenerB.TopicMessageReceived += (_, topic, _) => { if (topic == "shared") bReceivedFirst = true; };
            _relayA.Send("shared", new byte[] { 0xEE });
            yield return TestParameters.WaitForCondition(
                () => bReceivedFirst,
                TestParameters.ReceiveTimeoutThreshold,
                () => _relayB.ProcessIncomingMessages());
            Assert.IsTrue(bReceivedFirst, "B did not receive before unsubscribe.");

            _relayB.UnsubscribeToTopic("shared");
            _relayB.ProcessIncomingMessages();
            yield return null;

            bool bReceivedAfterUnsubscribe = false;
            _listenerB.TopicMessageReceived += (_, topic, _) => { if (topic == "shared") bReceivedAfterUnsubscribe = true; };
            _relayA.Send("shared", new byte[] { 0xFF });
            yield return TestParameters.WaitForDuration(1f, () => _relayB.ProcessIncomingMessages());
            Assert.IsFalse(bReceivedAfterUnsubscribe, "B received payload after unsubscribing.");

            Cleanup();
        }

        private IEnumerator TestSendAfterUnsubscribeThrows()
        {
            _relayA = CreateRelay(out _listenerA);
            _relayB = CreateRelay(out _listenerB);
            _relayA.Connect();
            _relayB.Connect();
            yield return new WaitWhile(() => !_relayA.IsConnected || !_relayB.IsConnected, TestParameters.ConnectTimeoutThreshold);

            _relayB.SubscribeToTopic("temp");
            _relayB.ProcessIncomingMessages();
            yield return null;

            _relayB.UnsubscribeToTopic("temp");
            _relayB.ProcessIncomingMessages();
            yield return null;

            Assert.Throws<KeyNotFoundException>(() => _relayB.Send("temp", new byte[] { 0x01 }));

            Cleanup();
        }

        private IEnumerator TestEmptyMessage()
        {
            _relayA = CreateRelay(out _listenerA);
            _relayB = CreateRelay(out _listenerB);
            _relayA.Connect();
            _relayB.Connect();
            yield return new WaitWhile(() => !_relayA.IsConnected || !_relayB.IsConnected, TestParameters.ConnectTimeoutThreshold);

            _relayA.SubscribeToTopic("empty");
            _relayB.SubscribeToTopic("empty");

            _relayA.ProcessIncomingMessages();
            _relayB.ProcessIncomingMessages();
            yield return null;

            byte[] received = null;
            _listenerB.TopicMessageReceived += (_, topic, data) => { if (topic == "empty") received = data; };
            _relayA.Send("empty", new byte[0]);

            yield return TestParameters.WaitForCondition(
                () => received != null,
                TestParameters.ReceiveTimeoutThreshold,
                () => _relayB.ProcessIncomingMessages());

            Assert.IsNotNull(received, "Empty message was not received.");
            Assert.AreEqual(0, received.Length);

            Cleanup();
        }

        private IEnumerator TestLargeMessage()
        {
            _relayA = CreateRelay(out _listenerA);
            _relayB = CreateRelay(out _listenerB);
            _relayA.Connect();
            _relayB.Connect();
            yield return new WaitWhile(() => !_relayA.IsConnected || !_relayB.IsConnected, TestParameters.ConnectTimeoutThreshold);

            _relayA.SubscribeToTopic("large");
            _relayB.SubscribeToTopic("large");

            _relayA.ProcessIncomingMessages();
            _relayB.ProcessIncomingMessages();
            yield return null;

            byte[] sent = GenerateRandomBytes(40_000);
            byte[] received = null;
            _listenerB.TopicMessageReceived += (_, topic, data) => { if (topic == "large") received = data; };

            _relayA.Send("large", sent);
            yield return TestParameters.WaitForCondition(
                () => received != null,
                TestParameters.ReceiveTimeoutThreshold,
                () => _relayB.ProcessIncomingMessages());

            Assert.IsNotNull(received);
            Assert.True(sent.SequenceEqual(received));

            Cleanup();
        }

        private IEnumerator TestRapidMessages(int count)
        {
            _relayA = CreateRelay(out _listenerA);
            _relayB = CreateRelay(out _listenerB);
            _relayA.Connect();
            _relayB.Connect();
            yield return new WaitWhile(() => !_relayA.IsConnected || !_relayB.IsConnected, TestParameters.ConnectTimeoutThreshold);

            _relayA.SubscribeToTopic("rapid");
            _relayB.SubscribeToTopic("rapid");

            _relayA.ProcessIncomingMessages();
            _relayB.ProcessIncomingMessages();
            yield return null;

            for (int i = 0; i < count; i++)
                _relayA.Send("rapid", new byte[] { (byte)i });

            int receivedCount = 0;
            _listenerB.TopicMessageReceived += (_, topic, _) => { if (topic == "rapid") receivedCount++; };

            yield return TestParameters.WaitForCondition(
                () => receivedCount >= count,
                TestParameters.ReceiveTimeoutThreshold,
                () => _relayB.ProcessIncomingMessages());

            Assert.AreEqual(count, receivedCount, $"Expected {count} messages, received {receivedCount}.");

            Cleanup();
        }

        private IEnumerator TestDisposeWhileConnectedCallsDisconnected()
        {
            int disconnectCount = 0;

            _listenerA = new TestRelayListener();
            _listenerA.Disconnected += () => disconnectCount++;
            _relayA = new RelayClient($"ws://localhost:{TestParameters.RelayServerPort}", _listenerA, Guid.NewGuid());
            _relayA.Connect();

            yield return new WaitWhile(() => !_relayA.IsConnected, TestParameters.ConnectTimeoutThreshold);
            _relayA.ProcessIncomingMessages();

            _relayA.Dispose();
            _relayA = null;

            Assert.AreEqual(1, disconnectCount);
            yield return null;
        }

        private IEnumerator TestDisposeBeforeConnectDoesNotCallDisconnected()
        {
            int disconnectCount = 0;

            _listenerA = new TestRelayListener();
            _listenerA.Disconnected += () => disconnectCount++;
            _relayA = new RelayClient($"ws://localhost:{TestParameters.RelayServerPort}", _listenerA, Guid.NewGuid());

            _relayA.Dispose();
            _relayA = null;

            Assert.AreEqual(0, disconnectCount);
            yield return null;
        }

        private IEnumerator TestServerStopCallsDisconnected()
        {
            int disconnectCount = 0;

            _listenerA = new TestRelayListener();
            _listenerA.Disconnected += () => disconnectCount++;
            _relayA = new RelayClient($"ws://localhost:{TestParameters.RelayServerPort}", _listenerA, Guid.NewGuid());
            _relayA.Connect();

            yield return new WaitWhile(() => !_relayA.IsConnected, TestParameters.ConnectTimeoutThreshold);
            _relayA.ProcessIncomingMessages();

            yield return RelayServerLauncher.StopCoroutine();

            yield return TestParameters.WaitForCondition(
                () => disconnectCount > 0,
                TestParameters.DisconnectTimeoutThreshold,
                () => _relayA.ProcessIncomingMessages());

            Assert.AreEqual(1, disconnectCount);

            _relayA.ProcessIncomingMessages();
            Assert.AreEqual(1, disconnectCount, "Disconnect callback should not fire again while polling.");

            _relayA = null;
            yield return null;
        }

        private IEnumerator TestDisconnectNotCalledTwice()
        {
            int disconnectCount = 0;

            _listenerA = new TestRelayListener();
            _listenerA.Disconnected += () => disconnectCount++;
            _relayA = new RelayClient($"ws://localhost:{TestParameters.RelayServerPort}", _listenerA, Guid.NewGuid());
            _relayA.Connect();

            yield return new WaitWhile(() => !_relayA.IsConnected, TestParameters.ConnectTimeoutThreshold);
            _relayA.ProcessIncomingMessages();

            yield return RelayServerLauncher.StopCoroutine();

            yield return TestParameters.WaitForCondition(
                () => disconnectCount > 0,
                TestParameters.DisconnectTimeoutThreshold,
                () => _relayA.ProcessIncomingMessages());

            _relayA.ProcessIncomingMessages();
            _relayA.ProcessIncomingMessages();

            Assert.AreEqual(1, disconnectCount);

            _relayA = null;
            yield return null;
        }

        private RelayClient CreateRelay(out TestRelayListener listener)
        {
            listener = new TestRelayListener();
            return new RelayClient($"ws://localhost:{TestParameters.RelayServerPort}", listener, Guid.NewGuid());
        }

        private byte[] GenerateRandomBytes(int length)
        {
            var random = new System.Random();
            byte[] bytes = new byte[length];
            for (int i = 0; i < length; i++)
                bytes[i] = (byte)random.Next(byte.MaxValue + 1);
            return bytes;
        }

        private void Cleanup()
        {
            if (_relayA != null) { _relayA.Dispose(); _relayA = null; }
            if (_relayB != null) { _relayB.Dispose(); _relayB = null; }
            if (_relayC != null) { _relayC.Dispose(); _relayC = null; }
        }

        [UnityTearDown]
        public IEnumerator Teardown()
        {
            Cleanup();
            yield return null;
        }
    }
}
