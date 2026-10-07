using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using BananaParty.WebSocketRelay.Transport;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BananaParty.WebSocketRelay.Tests
{
    /// <summary>
    /// Session behavior across connection loss, driven by fake sockets and a manual clock.
    /// </summary>
    public class NetworkConnectionTests
    {
        private const float StepSeconds = 0.02f;
        private const string Channel = "room";

        private static readonly Guid LocalGuid = Guid.Parse("00000000-0000-0000-0000-0000000000aa");
        private static readonly Guid PeerGuid = Guid.Parse("00000000-0000-0000-0000-0000000000bb");

        private readonly List<GameObject> _createdObjects = new();

        private ManualClock _clock;
        private FakeSocketFactory _factory;
        private NetworkContext _context;
        private Network _network;

        [SetUp]
        public void SetUp()
        {
            _clock = new ManualClock();
            _factory = new FakeSocketFactory();
            _context = NetworkContextTestHelpers.CreateContext(playerTimeoutSeconds: 10f);
        }

        [TearDown]
        public void TearDown()
        {
            if (_network != null && _network.HasRelayClient)
                _network.Disconnect(clearSession: false);

            foreach (GameObject createdObject in _createdObjects)
            {
                if (createdObject != null)
                    UnityEngine.Object.DestroyImmediate(createdObject);
            }

            _createdObjects.Clear();
            UnityEngine.Object.DestroyImmediate(_context);
        }

        private static RelayConnectionSettings CreateSettings(float reconnectTimeoutSeconds = 30f) => new()
        {
            ConnectTimeoutSeconds = 2f,
            HeartbeatIntervalSeconds = 1f,
            HeartbeatTimeoutSeconds = 5f,
            ReconnectTimeoutSeconds = reconnectTimeoutSeconds,
            ReconnectInitialDelaySeconds = 0.25f,
            ReconnectMaxDelaySeconds = 1f,
            SendBacklogLimitBytes = 1000,
        };

        private void CreateNetwork(RelayConnectionSettings settings = null)
        {
            _network = new Network(_factory.Create, _context, settings ?? CreateSettings(), _clock.GetTime);
        }

        private FakeSocket ConnectAndOpen()
        {
            _network.Connect(LocalGuid);
            _factory.Latest.Open();
            Update(StepSeconds);
            Assert.AreEqual(RelayConnectionState.Connected, _network.ConnectionState);
            return _factory.Latest;
        }

        private FakeSocket ReconnectAndOpen()
        {
            int socketCount = _factory.Sockets.Count;
            UpdateUntil(() => _factory.Sockets.Count > socketCount, 5f);
            _factory.Latest.Open();
            Update(StepSeconds);
            Assert.AreEqual(RelayConnectionState.Connected, _network.ConnectionState);
            return _factory.Latest;
        }

        private void Update(float seconds)
        {
            int steps = Mathf.RoundToInt(seconds / StepSeconds);
            for (int step = 0; step < steps; step++)
            {
                _clock.Advance(StepSeconds);
                _network.ManualUpdate(StepSeconds);
            }
        }

        private void UpdateUntil(Func<bool> condition, float timeoutSeconds)
        {
            float elapsed = 0f;
            while (!condition())
            {
                if (elapsed > timeoutSeconds)
                    Assert.Fail($"Condition not met within {timeoutSeconds} s.");

                Update(StepSeconds);
                elapsed += StepSeconds;
            }
        }

        private GameObject RegisterLocalIdentity()
        {
            GameObject gameObject = new("LocalOwnedObject");
            _createdObjects.Add(gameObject);
            _context.RegisterNetworkIdentity(new StubNetworkIdentity(gameObject, "LocalPrefab", LocalGuid, Guid.NewGuid(), Channel));
            return gameObject;
        }

        private static void ReceivePeerSync(FakeSocket socket)
            => socket.ReceiveChannelMessage(PeerGuid, Channel, NetworkContextTestHelpers.CreateEmptySyncIdentitiesMessage());

        [Test]
        public void ConnectionLost_KeepsSessionWhileReconnectingAndResumesWithSameGuid()
        {
            CreateNetwork();
            FakeSocket socket = ConnectAndOpen();
            _network.SubscribeToChannel(Channel);
            GameObject localObject = RegisterLocalIdentity();
            ReceivePeerSync(socket);
            Update(StepSeconds);
            Assert.AreEqual(1, _context.NetworkPlayers.Count);

            socket.Close("Network changed");
            Update(StepSeconds);

            Assert.AreEqual(RelayConnectionState.Reconnecting, _network.ConnectionState);
            Assert.IsTrue(_network.HasRelayClient);
            Assert.IsFalse(_network.IsConnected);
            Assert.IsTrue(_context.IsConnectionInterrupted);
            Assert.AreEqual(LocalGuid, _context.LocalClientIdentity);
            Assert.AreEqual(1, _context.NetworkIdentities.Count);
            Assert.AreEqual(1, _context.NetworkPlayers.Count);
            Assert.IsFalse(localObject == null);

            FakeSocket newSocket = ReconnectAndOpen();

            Assert.IsFalse(_context.IsConnectionInterrupted);
            CollectionAssert.AreEqual(new[] { Channel }, newSocket.SentSubscriptions());

            _network.SendSyncIdentities();
            byte[] syncMessage = newSocket.SentOfType(RelayMessageType.ChannelState).Single();
            Assert.AreEqual(LocalGuid, RelayMessageCodec.ReadGuid(syncMessage, RelayMessageCodec.ChannelMessageGuidOffset));
        }

        [UnityTest]
        public IEnumerator ReconnectGivesUp_ClearsSessionAndReleasesRelayClient()
        {
            CreateNetwork(CreateSettings(reconnectTimeoutSeconds: 2f));
            FakeSocket socket = ConnectAndOpen();
            GameObject localObject = RegisterLocalIdentity();

            socket.Close();
            Update(1f);
            Assert.IsTrue(_network.HasRelayClient);

            Update(1.1f);
            yield return null;

            Assert.IsFalse(_network.HasRelayClient);
            Assert.AreEqual(RelayConnectionState.Disconnected, _network.ConnectionState);
            Assert.AreEqual(Guid.Empty, _context.LocalClientIdentity);
            Assert.AreEqual(0, _context.NetworkIdentities.Count);
            Assert.IsTrue(localObject == null);
        }

        [Test]
        public void FailedFirstAttempt_KeepsRelayClientUntilDisconnect()
        {
            CreateNetwork();
            _network.Connect(LocalGuid);

            _factory.Latest.Close("Connection refused");
            Update(StepSeconds);

            Assert.AreEqual(RelayConnectionState.Disconnected, _network.ConnectionState);
            Assert.IsTrue(_network.HasRelayClient);
            Assert.Throws<InvalidOperationException>(() => _network.Connect(LocalGuid));

            _network.Disconnect();
            Assert.IsFalse(_network.HasRelayClient);
        }

        [Test]
        public void Connect_SocketFactoryThrows_LeavesNetworkReadyToConnectAgain()
        {
            CreateNetwork();
            _factory.ExceptionToThrow = new InvalidOperationException("Socket factory failed");

            Assert.Throws<InvalidOperationException>(() => _network.Connect(LocalGuid));
            Assert.IsFalse(_network.HasRelayClient);
            Assert.AreEqual(Guid.Empty, _context.LocalClientIdentity);

            _factory.ExceptionToThrow = null;
            ConnectAndOpen();
        }

        [Test]
        public void RpcsQueuedWhileReconnecting_AreSentAfterReconnect()
        {
            CreateNetwork();
            FakeSocket socket = ConnectAndOpen();
            _network.SubscribeToChannel(Channel);

            socket.Close();
            Update(StepSeconds);
            _context.SendRpc(Guid.NewGuid(), "TestRpc", NetworkContextTestHelpers.CreateRpcParameters(1), Channel, invokeLocally: false);
            _context.SendRpc(Guid.NewGuid(), "TestRpc", NetworkContextTestHelpers.CreateRpcParameters(2), Channel, invokeLocally: false);
            Update(StepSeconds);

            FakeSocket newSocket = ReconnectAndOpen();

            List<byte[]> rpcMessages = newSocket.SentOfType(RelayMessageType.ChannelMessage).ToList();
            Assert.AreEqual(2, rpcMessages.Count);
            Assert.IsFalse(_context.TryDequeueOutgoingRpcMessage(out _, out _));
        }

        [Test]
        public void RpcFlush_SocketClosingMidway_KeepsRemainingRpcsForReconnect()
        {
            CreateNetwork();
            FakeSocket socket = ConnectAndOpen();
            _network.SubscribeToChannel(Channel);

            socket.CloseOnNextSend = true;
            _context.SendRpc(Guid.NewGuid(), "TestRpc", NetworkContextTestHelpers.CreateRpcParameters(1), Channel, invokeLocally: false);
            _context.SendRpc(Guid.NewGuid(), "TestRpc", NetworkContextTestHelpers.CreateRpcParameters(2), Channel, invokeLocally: false);
            _context.SendRpc(Guid.NewGuid(), "TestRpc", NetworkContextTestHelpers.CreateRpcParameters(3), Channel, invokeLocally: false);

            Update(StepSeconds);
            Assert.AreEqual(0, socket.SentOfType(RelayMessageType.ChannelMessage).Count());

            FakeSocket newSocket = ReconnectAndOpen();
            Assert.AreEqual(2, newSocket.SentOfType(RelayMessageType.ChannelMessage).Count(), "Only the RPC sent into the closing socket is lost.");
        }

        [Test]
        public void RpcsQueuedAfterConnectionClosed_AreAllDeliveredAfterReconnect()
        {
            CreateNetwork();
            FakeSocket socket = ConnectAndOpen();
            _network.SubscribeToChannel(Channel);

            socket.Close();
            _context.SendRpc(Guid.NewGuid(), "TestRpc", NetworkContextTestHelpers.CreateRpcParameters(1), Channel, invokeLocally: false);
            _context.SendRpc(Guid.NewGuid(), "TestRpc", NetworkContextTestHelpers.CreateRpcParameters(2), Channel, invokeLocally: false);

            // The relay client notices the closure in this update, before queued RPCs are flushed.
            Update(StepSeconds);
            Assert.AreEqual(RelayConnectionState.Reconnecting, _network.ConnectionState);

            FakeSocket newSocket = ReconnectAndOpen();
            Assert.AreEqual(2, newSocket.SentOfType(RelayMessageType.ChannelMessage).Count());
        }

        [Test]
        public void SendSyncIdentities_SkippedWhileSendBacklogged()
        {
            CreateNetwork();
            FakeSocket socket = ConnectAndOpen();
            _network.SubscribeToChannel(Channel);

            socket.PendingSendBytes = 1001;
            _network.SendSyncIdentities();
            Assert.AreEqual(0, socket.SentOfType(RelayMessageType.ChannelState).Count());

            socket.PendingSendBytes = 0;
            _network.SendSyncIdentities();
            Assert.AreEqual(1, socket.SentOfType(RelayMessageType.ChannelState).Count());
        }

        [Test]
        public void InterruptedLink_FreezesPlayerTimeoutsUntilDataFlowsAgain()
        {
            NetworkContextTestHelpers.SetPlayerTimeoutSeconds(_context, 6f);
            _factory.OnCreate = createdSocket => createdSocket.AnswersPings = false;
            CreateNetwork();
            FakeSocket socket = ConnectAndOpen();
            _network.SubscribeToChannel(Channel);
            ReceivePeerSync(socket);
            Update(StepSeconds);

            // Silent for 2 heartbeat intervals marks the link as interrupted, at 5 s it is dropped.
            Update(5.5f);
            Assert.AreEqual(RelayConnectionState.Reconnecting, _network.ConnectionState);
            Update(10f);

            Assert.AreEqual(1, _context.NetworkPlayers.Count, "Peer timed out while the local connection was down.");
            float frozenTimeSinceLastMessage = _context.NetworkPlayers[0].TimeSinceLastMessage;
            Assert.AreEqual(2f, frozenTimeSinceLastMessage, 0.1f);

            _factory.OnCreate = null;
            ReconnectAndOpen();

            Update(6f - frozenTimeSinceLastMessage - 0.5f);
            Assert.AreEqual(1, _context.NetworkPlayers.Count);

            Update(1f);
            Assert.AreEqual(0, _context.NetworkPlayers.Count, "Peer silent after the connection recovered should time out.");
        }

        [Test]
        public void HealthyLink_SilentPeerTimesOut()
        {
            NetworkContextTestHelpers.SetPlayerTimeoutSeconds(_context, 6f);
            CreateNetwork();
            FakeSocket socket = ConnectAndOpen();
            _network.SubscribeToChannel(Channel);
            ReceivePeerSync(socket);
            Update(StepSeconds);

            Update(5.8f);
            Assert.AreEqual(1, _context.NetworkPlayers.Count);

            Update(0.4f);
            Assert.AreEqual(0, _context.NetworkPlayers.Count);
            Assert.IsFalse(_context.IsConnectionInterrupted);
        }

        [Test]
        public void Disconnect_ClearsConnectionInterrupted()
        {
            _factory.OnCreate = createdSocket => createdSocket.AnswersPings = false;
            CreateNetwork();
            ConnectAndOpen();
            Update(2.5f);
            Assert.IsTrue(_context.IsConnectionInterrupted);

            _network.Disconnect(clearSession: false);

            Assert.IsFalse(_context.IsConnectionInterrupted);
        }

        [Test]
        public void RoundTripTime_ExposedFromRelayClient()
        {
            _factory.OnCreate = createdSocket => createdSocket.AnswersPings = false;
            CreateNetwork();
            Assert.AreEqual(0d, _network.RoundTripTimeSeconds);

            FakeSocket socket = ConnectAndOpen();
            byte[] ping = socket.SentOfType(RelayMessageType.Ping).Last();
            Update(0.1f);
            socket.Receive(FakeSocket.CreatePong(ping));
            Update(StepSeconds);

            Assert.AreEqual(0.1 + StepSeconds, _network.RoundTripTimeSeconds, 1e-4);
        }

        [Test]
        public void Connect_PlayerTimeoutNotAboveHeartbeatTimeout_LogsWarning()
        {
            NetworkContextTestHelpers.SetPlayerTimeoutSeconds(_context, 5f);
            CreateNetwork();

            LogAssert.Expect(LogType.Warning, new Regex("Player timeout"));
            _network.Connect(LocalGuid);
        }

        [Test]
        public void Constructor_InvalidSettings_Throws()
        {
            RelayConnectionSettings settings = CreateSettings();
            settings.HeartbeatTimeoutSeconds = settings.HeartbeatIntervalSeconds;

            Assert.Throws<ArgumentOutOfRangeException>(() => new Network(_factory.Create, _context, settings, _clock.GetTime));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Network("ws://127.0.0.1:1", _context, connectionSettings: settings));
        }
    }
}
