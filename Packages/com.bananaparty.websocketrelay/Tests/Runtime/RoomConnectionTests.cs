using System;
using System.Collections;
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

        // === Single Room Tests ===

        [UnityTest] public IEnumerator TwoClients_MessageRelay() => TestRoomMessage(100, 2);
        [UnityTest] public IEnumerator ThreeClients_AllReceive() => TestRoomMessage(100, 3);

        // === Room Isolation Tests ===

        [UnityTest] public IEnumerator DifferentRooms_Isolated() => TestRoomIsolation();

        // === Multi-Room Tests ===

        [UnityTest] public IEnumerator MultipleRooms_JoinAndSwitch() => TestMultiRoomJoin();
        [UnityTest] public IEnumerator SameRoomDifferentIds_AreIsolated() => TestSameRoomDifferentIds();
        [UnityTest] public IEnumerator LeaveStopsReceiving() => TestLeaveStopsReceiving();
        [UnityTest] public IEnumerator SendAfterLeave_ThrowsObjectDisposedException() => TestSendAfterLeaveThrows();

        // === Edge Cases ===

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

            RoomConnection roomA = _relayA.JoinRoom(roomId);
            RoomConnection roomB = _relayB.JoinRoom(roomId);
            RoomConnection roomC = null;
            if (clientCount >= 3) roomC = _relayC.JoinRoom(roomId);

            // Drain JOINED_ROOM confirmations
            _relayA.ProcessIncomingMessages();
            _relayB.ProcessIncomingMessages();
            if (clientCount >= 3) _relayC.ProcessIncomingMessages();
            yield return null;

            byte[] sent = GenerateRandomBytes(64);
            int recvCount = 0;
            byte[] receivedData = null;

            roomB.OnMessageReceived += (data) => { recvCount++; receivedData = data; };
            if (roomC != null)
                roomC.OnMessageReceived += (data) => { recvCount++; receivedData = data; };

            roomA.Send(sent);

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

            RoomConnection aRoom100 = _relayA.JoinRoom(100);
            RoomConnection bRoom200 = _relayB.JoinRoom(200);

            _relayA.ProcessIncomingMessages();
            _relayB.ProcessIncomingMessages();
            yield return null;

            bool bReceived = false;
            bRoom200.OnMessageReceived += _ => bReceived = true;

            aRoom100.Send(new byte[] { 0xAA });

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

            RoomConnection aRoom100 = _relayA.JoinRoom(100);
            RoomConnection bRoom100 = _relayB.JoinRoom(100);
            RoomConnection bRoom200 = _relayB.JoinRoom(200);

            _relayA.ProcessIncomingMessages();
            _relayB.ProcessIncomingMessages();
            yield return null;

            // A sends in room 100 -> B's room100 receives
            bool bGot100 = false;
            bRoom100.OnMessageReceived += _ => bGot100 = true;
            aRoom100.Send(new byte[] { 0xCC });
            yield return TestParameters.WaitForCondition(
                () => bGot100,
                TestParameters.ReceiveTimeoutThreshold,
                () => _relayB.ProcessIncomingMessages());
            Assert.IsTrue(bGot100, "B did not receive room 100 message.");

            // B sends in room 200 -> A is NOT in room 200, no relay to A
            bool aGot200 = false;
            aRoom100.OnMessageReceived += _ => aGot200 = true;
            bRoom200.Send(new byte[] { 0xDD });
            yield return TestParameters.WaitForDuration(1f, () => _relayA.ProcessIncomingMessages());
            Assert.IsFalse(aGot200, "A received message from room it is not in.");

            // Now join A to room 200
            RoomConnection aRoom200 = _relayA.JoinRoom(200);
            _relayA.ProcessIncomingMessages();
            yield return null;

            bool aGotFromB = false;
            aRoom200.OnMessageReceived += _ => aGotFromB = true;
            bRoom200.Send(new byte[] { 0xEE });
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

            RoomConnection a100 = _relayA.JoinRoom(100);
            RoomConnection b200 = _relayB.JoinRoom(200);

            _relayA.ProcessIncomingMessages();
            _relayB.ProcessIncomingMessages();
            yield return null;

            bool bReceived = false;
            b200.OnMessageReceived += _ => bReceived = true;
            a100.Send(new byte[] { 0xDD });

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

            RoomConnection aRoom = _relayA.JoinRoom(100);
            RoomConnection bRoom = _relayB.JoinRoom(100);

            _relayA.ProcessIncomingMessages();
            _relayB.ProcessIncomingMessages();
            yield return null;

            // A sends -> B receives
            bool bReceivedFirst = false;
            bRoom.OnMessageReceived += _ => bReceivedFirst = true;
            aRoom.Send(new byte[] { 0xEE });
            yield return TestParameters.WaitForCondition(
                () => bReceivedFirst,
                TestParameters.ReceiveTimeoutThreshold,
                () => _relayB.ProcessIncomingMessages());
            Assert.IsTrue(bReceivedFirst, "B did not receive before leave.");

            // B leaves room
            _relayB.LeaveRoom(100);
            _relayB.ProcessIncomingMessages();
            yield return null;

            // A sends again -> B should NOT receive (no longer in room)
            bool bReceivedAfterLeave = false;
            bRoom.OnMessageReceived += _ => bReceivedAfterLeave = true;
            aRoom.Send(new byte[] { 0xFF });
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

            RoomConnection bRoom = _relayB.JoinRoom(400);
            _relayB.ProcessIncomingMessages();
            yield return null;

            _relayB.LeaveRoom(400);
            _relayB.ProcessIncomingMessages();
            yield return null;

            Assert.Throws<ObjectDisposedException>(() => bRoom.Send(new byte[] { 0x01 }));

            Cleanup();
        }

        private IEnumerator TestEmptyMessage()
        {
            _relayA = CreateRelay();
            _relayB = CreateRelay();
            _relayA.Connect();
            _relayB.Connect();
            yield return new WaitWhile(() => !_relayA.IsConnected || !_relayB.IsConnected, TestParameters.ConnectTimeoutThreshold);

            RoomConnection aRoom = _relayA.JoinRoom(300);
            RoomConnection bRoom = _relayB.JoinRoom(300);

            _relayA.ProcessIncomingMessages();
            _relayB.ProcessIncomingMessages();
            yield return null;

            byte[] received = null;
            bRoom.OnMessageReceived += (data) => received = data;
            aRoom.Send(new byte[0]);

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

            RoomConnection aRoom = _relayA.JoinRoom(301);
            RoomConnection bRoom = _relayB.JoinRoom(301);

            _relayA.ProcessIncomingMessages();
            _relayB.ProcessIncomingMessages();
            yield return null;

            byte[] sent = GenerateRandomBytes(40_000);
            byte[] received = null;
            bRoom.OnMessageReceived += (data) => received = data;

            aRoom.Send(sent);
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

            RoomConnection aRoom = _relayA.JoinRoom(302);
            RoomConnection bRoom = _relayB.JoinRoom(302);

            _relayA.ProcessIncomingMessages();
            _relayB.ProcessIncomingMessages();
            yield return null;

            for (int i = 0; i < count; i++)
                aRoom.Send(new byte[] { (byte)i });

            int receivedCount = 0;
            bRoom.OnMessageReceived += _ => receivedCount++;

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
