using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace BananaParty.WebSocketRelay.Tests
{
    public class RoomConnectionTests
    {
        private RelayConnection _relayA;
        private RelayConnection _relayB;
        private RelayConnection _relayC;

        [UnitySetUp]
        public IEnumerator Setup()
        {
            yield return RelayServerLauncher.StartCoroutine();
        }

        [UnityTest] public IEnumerator TwoClients_MessageRelay() => TestRoomMessage(100, 2);
        [UnityTest] public IEnumerator ThreeClients_AllReceive() => TestRoomMessage(100, 3);
        [UnityTest] public IEnumerator DifferentRooms_Isolated() => TestRoomIsolation();
        [UnityTest] public IEnumerator MultipleRooms_JoinAndSwitch() => TestMultiRoomJoin();
        [UnityTest] public IEnumerator SameRoomDifferentIds_AreIsolated() => TestSameRoomDifferentIds();
        [UnityTest] public IEnumerator LeaveStopsReceiving() => TestLeaveStopsReceiving();
        [UnityTest] public IEnumerator SendAfterLeave_ThrowsKeyNotFoundException() => TestSendAfterLeaveThrows();
        [UnityTest] public IEnumerator EmptyPayload_Relays() => TestEmptyMessage();
        [UnityTest] public IEnumerator LargePayload_Relays() => TestLargeMessage();
        [UnityTest] public IEnumerator RapidMessages_AllDelivered() => TestRapidMessages(50);

        private IEnumerator TestRoomMessage(int roomId, int clientCount)
        {
            _relayA = CreateRelay();
            _relayB = CreateRelay();
            if (clientCount >= 3) _relayC = CreateRelay();

            _relayA.Connect();
            _relayB.Connect();
            if (clientCount >= 3) _relayC.Connect();

            yield return new WaitWhile(() => !_relayA.IsConnected || !_relayB.IsConnected, TestParameters.ConnectTimeoutThreshold);
            if (clientCount >= 3)
                yield return new WaitWhile(() => !_relayC.IsConnected, TestParameters.ConnectTimeoutThreshold);

            _relayA.JoinRoom(roomId);
            _relayB.JoinRoom(roomId);
            if (clientCount >= 3) _relayC.JoinRoom(roomId);

            _relayA.ProcessIncomingMessages();
            _relayB.ProcessIncomingMessages();
            if (clientCount >= 3) _relayC.ProcessIncomingMessages();
            yield return null;

            byte[] sent = GenerateRandomBytes(64);
            int recvCount = 0;
            byte[] receivedData = null;

            _relayB.OnRoomMessage += (id, data) =>
            {
                if (id != roomId) return;
                recvCount++;
                receivedData = data;
            };
            if (clientCount >= 3)
            {
                _relayC.OnRoomMessage += (id, data) =>
                {
                    if (id != roomId) return;
                    recvCount++;
                    receivedData = data;
                };
            }

            _relayA.Send(roomId, sent);

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

        private IEnumerator TestRoomIsolation()
        {
            _relayA = CreateRelay();
            _relayB = CreateRelay();
            _relayA.Connect();
            _relayB.Connect();
            yield return new WaitWhile(() => !_relayA.IsConnected || !_relayB.IsConnected, TestParameters.ConnectTimeoutThreshold);

            _relayA.JoinRoom(100);
            _relayB.JoinRoom(200);

            _relayA.ProcessIncomingMessages();
            _relayB.ProcessIncomingMessages();
            yield return null;

            bool bReceived = false;
            _relayB.OnRoomMessage += (id, _) => { if (id == 200) bReceived = true; };

            _relayA.Send(100, new byte[] { 0xAA });

            yield return TestParameters.WaitForDuration(1f, () => _relayB.ProcessIncomingMessages());
            Assert.IsFalse(bReceived, "Client B received message from room it is not in.");

            Cleanup();
        }

        private IEnumerator TestMultiRoomJoin()
        {
            _relayA = CreateRelay();
            _relayB = CreateRelay();
            _relayA.Connect();
            _relayB.Connect();
            yield return new WaitWhile(() => !_relayA.IsConnected || !_relayB.IsConnected, TestParameters.ConnectTimeoutThreshold);

            _relayA.JoinRoom(100);
            _relayB.JoinRoom(100);
            _relayB.JoinRoom(200);

            _relayA.ProcessIncomingMessages();
            _relayB.ProcessIncomingMessages();
            yield return null;

            bool bGot100 = false;
            _relayB.OnRoomMessage += (id, _) => { if (id == 100) bGot100 = true; };
            _relayA.Send(100, new byte[] { 0xCC });
            yield return TestParameters.WaitForCondition(
                () => bGot100,
                TestParameters.ReceiveTimeoutThreshold,
                () => _relayB.ProcessIncomingMessages());
            Assert.IsTrue(bGot100, "B did not receive room 100 message.");

            bool aGot200 = false;
            _relayA.OnRoomMessage += (id, _) => { if (id == 200) aGot200 = true; };
            _relayB.Send(200, new byte[] { 0xDD });
            yield return TestParameters.WaitForDuration(1f, () => _relayA.ProcessIncomingMessages());
            Assert.IsFalse(aGot200, "A received message from room it is not in.");

            _relayA.JoinRoom(200);
            _relayA.ProcessIncomingMessages();
            yield return null;

            bool aGotFromB = false;
            _relayA.OnRoomMessage += (id, _) => { if (id == 200) aGotFromB = true; };
            _relayB.Send(200, new byte[] { 0xEE });
            yield return TestParameters.WaitForCondition(
                () => aGotFromB,
                TestParameters.ReceiveTimeoutThreshold,
                () => _relayA.ProcessIncomingMessages());
            Assert.IsTrue(aGotFromB, "A did not receive room 200 message after joining.");

            Cleanup();
        }

        private IEnumerator TestSameRoomDifferentIds()
        {
            _relayA = CreateRelay();
            _relayB = CreateRelay();
            _relayA.Connect();
            _relayB.Connect();
            yield return new WaitWhile(() => !_relayA.IsConnected || !_relayB.IsConnected, TestParameters.ConnectTimeoutThreshold);

            _relayA.JoinRoom(100);
            _relayB.JoinRoom(200);

            _relayA.ProcessIncomingMessages();
            _relayB.ProcessIncomingMessages();
            yield return null;

            bool bReceived = false;
            _relayB.OnRoomMessage += (id, _) => { if (id == 200) bReceived = true; };
            _relayA.Send(100, new byte[] { 0xDD });

            yield return TestParameters.WaitForDuration(1f, () => _relayB.ProcessIncomingMessages());
            Assert.IsFalse(bReceived);

            Cleanup();
        }

        private IEnumerator TestLeaveStopsReceiving()
        {
            _relayA = CreateRelay();
            _relayB = CreateRelay();
            _relayA.Connect();
            _relayB.Connect();
            yield return new WaitWhile(() => !_relayA.IsConnected || !_relayB.IsConnected, TestParameters.ConnectTimeoutThreshold);

            _relayA.JoinRoom(100);
            _relayB.JoinRoom(100);

            _relayA.ProcessIncomingMessages();
            _relayB.ProcessIncomingMessages();
            yield return null;

            bool bReceivedFirst = false;
            _relayB.OnRoomMessage += (id, _) => { if (id == 100) bReceivedFirst = true; };
            _relayA.Send(100, new byte[] { 0xEE });
            yield return TestParameters.WaitForCondition(
                () => bReceivedFirst,
                TestParameters.ReceiveTimeoutThreshold,
                () => _relayB.ProcessIncomingMessages());
            Assert.IsTrue(bReceivedFirst, "B did not receive before leave.");

            _relayB.LeaveRoom(100);
            _relayB.ProcessIncomingMessages();
            yield return null;

            bool bReceivedAfterLeave = false;
            _relayB.OnRoomMessage += (id, _) => { if (id == 100) bReceivedAfterLeave = true; };
            _relayA.Send(100, new byte[] { 0xFF });
            yield return TestParameters.WaitForDuration(1f, () => _relayB.ProcessIncomingMessages());
            Assert.IsFalse(bReceivedAfterLeave, "B received payload after leaving.");

            Cleanup();
        }

        private IEnumerator TestSendAfterLeaveThrows()
        {
            _relayA = CreateRelay();
            _relayB = CreateRelay();
            _relayA.Connect();
            _relayB.Connect();
            yield return new WaitWhile(() => !_relayA.IsConnected || !_relayB.IsConnected, TestParameters.ConnectTimeoutThreshold);

            _relayB.JoinRoom(400);
            _relayB.ProcessIncomingMessages();
            yield return null;

            _relayB.LeaveRoom(400);
            _relayB.ProcessIncomingMessages();
            yield return null;

            Assert.Throws<KeyNotFoundException>(() => _relayB.Send(400, new byte[] { 0x01 }));

            Cleanup();
        }

        private IEnumerator TestEmptyMessage()
        {
            _relayA = CreateRelay();
            _relayB = CreateRelay();
            _relayA.Connect();
            _relayB.Connect();
            yield return new WaitWhile(() => !_relayA.IsConnected || !_relayB.IsConnected, TestParameters.ConnectTimeoutThreshold);

            _relayA.JoinRoom(300);
            _relayB.JoinRoom(300);

            _relayA.ProcessIncomingMessages();
            _relayB.ProcessIncomingMessages();
            yield return null;

            byte[] received = null;
            _relayB.OnRoomMessage += (id, data) => { if (id == 300) received = data; };
            _relayA.Send(300, new byte[0]);

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
            _relayA = CreateRelay();
            _relayB = CreateRelay();
            _relayA.Connect();
            _relayB.Connect();
            yield return new WaitWhile(() => !_relayA.IsConnected || !_relayB.IsConnected, TestParameters.ConnectTimeoutThreshold);

            _relayA.JoinRoom(301);
            _relayB.JoinRoom(301);

            _relayA.ProcessIncomingMessages();
            _relayB.ProcessIncomingMessages();
            yield return null;

            byte[] sent = GenerateRandomBytes(40_000);
            byte[] received = null;
            _relayB.OnRoomMessage += (id, data) => { if (id == 301) received = data; };

            _relayA.Send(301, sent);
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
            _relayA = CreateRelay();
            _relayB = CreateRelay();
            _relayA.Connect();
            _relayB.Connect();
            yield return new WaitWhile(() => !_relayA.IsConnected || !_relayB.IsConnected, TestParameters.ConnectTimeoutThreshold);

            _relayA.JoinRoom(302);
            _relayB.JoinRoom(302);

            _relayA.ProcessIncomingMessages();
            _relayB.ProcessIncomingMessages();
            yield return null;

            for (int i = 0; i < count; i++)
                _relayA.Send(302, new byte[] { (byte)i });

            int receivedCount = 0;
            _relayB.OnRoomMessage += (id, _) => { if (id == 302) receivedCount++; };

            yield return TestParameters.WaitForCondition(
                () => receivedCount >= count,
                TestParameters.ReceiveTimeoutThreshold,
                () => _relayB.ProcessIncomingMessages());

            Assert.AreEqual(count, receivedCount, $"Expected {count} messages, received {receivedCount}.");

            Cleanup();
        }

        private RelayConnection CreateRelay()
            => new($"ws://localhost:{TestParameters.RelayServerPort}");

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
