using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BananaParty.WebSocketRelay.Transport;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace BananaParty.WebSocketRelay.Tests
{
    public class RelayConnectionTests
    {
        private RelayConnection _relayA;
        private RelayConnection _relayB;
        private RelayConnection _relayC;
        private TestRelayListener _listenerA;
        private TestRelayListener _listenerB;
        private TestRelayListener _listenerC;

        [UnitySetUp]
        public IEnumerator Setup()
        {
            yield return RelayServerLauncher.StartCoroutine();
        }

        [UnityTest] public IEnumerator ClientReceivesGuidOnConnect() => TestClientReceivesGuidOnConnect();
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

        private IEnumerator TestClientReceivesGuidOnConnect()
        {
            _relayA = CreateRelay(out _listenerA);
            _relayA.Connect();
            yield return new WaitWhile(() => !_relayA.IsConnected, TestParameters.ConnectTimeoutThreshold);

            yield return TestParameters.WaitForCondition(
                () => _relayA.ClientId != Guid.Empty,
                TestParameters.ConnectTimeoutThreshold,
                () => _relayA.ProcessIncomingMessages());

            Assert.AreNotEqual(Guid.Empty, _relayA.ClientId);

            Cleanup();
        }

        private IEnumerator TestTopicMessageIncludesSenderGuid()
        {
            _relayA = CreateRelay(out _listenerA);
            _relayB = CreateRelay(out _listenerB);
            _relayA.Connect();
            _relayB.Connect();
            yield return new WaitWhile(() => !_relayA.IsConnected || !_relayB.IsConnected, TestParameters.ConnectTimeoutThreshold);

            yield return TestParameters.WaitForCondition(
                () => _relayA.ClientId != Guid.Empty && _relayB.ClientId != Guid.Empty,
                TestParameters.ConnectTimeoutThreshold,
                () =>
                {
                    _relayA.ProcessIncomingMessages();
                    _relayB.ProcessIncomingMessages();
                });

            _relayA.Subscribe("guid-test");
            _relayB.Subscribe("guid-test");
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

            Assert.AreEqual(_relayA.ClientId, receivedSenderId);

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

            _relayA.Subscribe(topic);
            _relayB.Subscribe(topic);
            if (clientCount >= 3) _relayC.Subscribe(topic);

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

            _relayA.Subscribe("alpha");
            _relayB.Subscribe("beta");

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

            _relayA.Subscribe("topic-a");
            _relayB.Subscribe("topic-a");
            _relayB.Subscribe("topic-b");

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

            _relayA.Subscribe("topic-b");
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

            _relayA.Subscribe("one");
            _relayB.Subscribe("two");

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

            _relayA.Subscribe("shared");
            _relayB.Subscribe("shared");

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

            _relayB.Unsubscribe("shared");
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

            _relayB.Subscribe("temp");
            _relayB.ProcessIncomingMessages();
            yield return null;

            _relayB.Unsubscribe("temp");
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

            _relayA.Subscribe("empty");
            _relayB.Subscribe("empty");

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

            _relayA.Subscribe("large");
            _relayB.Subscribe("large");

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

            _relayA.Subscribe("rapid");
            _relayB.Subscribe("rapid");

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

        private RelayConnection CreateRelay(out TestRelayListener listener)
        {
            listener = new TestRelayListener();
            return new RelayConnection($"ws://localhost:{TestParameters.RelayServerPort}", listener);
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
