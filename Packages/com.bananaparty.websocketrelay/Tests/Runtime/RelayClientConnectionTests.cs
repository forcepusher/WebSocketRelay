using System;
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
    /// Connection state machine tests driven by fake sockets and a manual clock, so every timing is exact.
    /// </summary>
    public class RelayClientConnectionTests
    {
        private const double StepSeconds = 0.02;

        private static readonly Guid ClientGuid = Guid.Parse("11111111-2222-3333-4444-555555555555");
        private static readonly Guid PeerGuid = Guid.Parse("99999999-8888-7777-6666-555555555555");

        private ManualClock _clock;
        private FakeSocketFactory _factory;
        private TestRelayListener _listener;
        private RelayClient _client;

        [SetUp]
        public void SetUp()
        {
            _clock = new ManualClock();
            _factory = new FakeSocketFactory();
            _listener = new TestRelayListener();
        }

        [TearDown]
        public void TearDown()
        {
            _client?.Dispose();
            _client = null;
        }

        private static RelayConnectionSettings CreateSettings() => new()
        {
            ConnectTimeoutSeconds = 2f,
            HeartbeatIntervalSeconds = 1f,
            HeartbeatTimeoutSeconds = 5f,
            ReconnectTimeoutSeconds = 30f,
            ReconnectInitialDelaySeconds = 0.25f,
            ReconnectMaxDelaySeconds = 4f,
            SendBacklogLimitBytes = 1000,
        };

        private RelayClient CreateClient(RelayConnectionSettings settings = null)
        {
            _client = new RelayClient(_factory.Create, _listener, ClientGuid, settings ?? CreateSettings(), _clock.GetTime);
            return _client;
        }

        private FakeSocket ConnectAndOpen()
        {
            _client.Connect();
            FakeSocket socket = _factory.Latest;
            socket.Open();
            Poll();
            Assert.AreEqual(RelayConnectionState.Connected, _client.State);
            return socket;
        }

        private void Poll(double advanceSeconds = StepSeconds)
        {
            _clock.Advance(advanceSeconds);
            _client.ProcessIncomingMessages();
        }

        private void Run(double seconds)
        {
            int steps = (int)Math.Round(seconds / StepSeconds);
            for (int step = 0; step < steps; step++)
                Poll();
        }

        /// <returns>Seconds until the condition held.</returns>
        private double RunUntil(Func<bool> condition, double timeoutSeconds, double stepSeconds = StepSeconds)
        {
            double elapsed = 0d;
            while (!condition())
            {
                if (elapsed > timeoutSeconds)
                    Assert.Fail($"Condition not met within {timeoutSeconds} s.");

                Poll(stepSeconds);
                elapsed += stepSeconds;
            }

            return elapsed;
        }

        private static byte[] LastPing(FakeSocket socket) => socket.SentOfType(RelayMessageType.Ping).Last();

        // Connecting

        [Test]
        public void Connect_ReportsConnectingUntilSocketOpens()
        {
            CreateClient();

            _client.Connect();

            Assert.AreEqual(RelayConnectionState.Connecting, _client.State);
            Assert.IsFalse(_client.IsConnected);
            Assert.IsTrue(_factory.Latest.IsConnectCalled);
            CollectionAssert.AreEqual(new[] { RelayConnectionState.Connecting }, _listener.States);

            Run(1);
            Assert.AreEqual(RelayConnectionState.Connecting, _client.State);

            _factory.Latest.Open();
            Poll();

            Assert.IsTrue(_client.IsConnected);
            CollectionAssert.AreEqual(new[] { RelayConnectionState.Connecting, RelayConnectionState.Connected }, _listener.States);
            Assert.IsNull(_listener.LastReason);
        }

        [Test]
        public void Connect_SocketOpensSynchronously_ConnectsRightAway()
        {
            _factory.OnCreate = createdSocket => createdSocket.Open();
            CreateClient();

            _client.Connect();

            Assert.AreEqual(RelayConnectionState.Connected, _client.State);
        }

        [Test]
        public void Connect_OfflineSocket_StaysConnectedAndHealthy()
        {
            _client = new RelayClient(() => new OfflineSocket(), _listener, ClientGuid, CreateSettings(), _clock.GetTime);

            _client.Connect();
            Assert.AreEqual(RelayConnectionState.Connected, _client.State);

            Run(60);

            Assert.AreEqual(RelayConnectionState.Connected, _client.State);
            Assert.IsTrue(_client.IsLinkHealthy);
            CollectionAssert.AreEqual(new[] { RelayConnectionState.Connecting, RelayConnectionState.Connected }, _listener.States);
        }

        [Test]
        public void Connect_WhileConnectingOrConnected_Throws()
        {
            CreateClient();
            _client.Connect();

            Assert.Throws<InvalidOperationException>(() => _client.Connect());

            _factory.Latest.Open();
            Poll();
            Assert.Throws<InvalidOperationException>(() => _client.Connect());
        }

        [Test]
        public void Connect_AfterDispose_ThrowsObjectDisposedException()
        {
            CreateClient();
            _client.Dispose();

            Assert.Throws<ObjectDisposedException>(() => _client.Connect());
        }

        [Test]
        public void ConnectAttemptFails_ReportsDisconnectedWithoutRetrying()
        {
            CreateClient();
            _client.Connect();

            _factory.Latest.Close("Connection refused");
            Poll();

            Assert.AreEqual(RelayConnectionState.Disconnected, _client.State);
            Assert.AreEqual("Connection refused", _listener.LastReason);
            Assert.IsTrue(_factory.Latest.IsDisposed);

            Run(10);
            Assert.AreEqual(1, _factory.Sockets.Count);
        }

        [Test]
        public void ConnectAttempt_TimesOutAfterConnectTimeout()
        {
            CreateClient();
            _client.Connect();

            Run(1.9);
            Assert.AreEqual(RelayConnectionState.Connecting, _client.State);

            Run(0.2);
            Assert.AreEqual(RelayConnectionState.Disconnected, _client.State);
            StringAssert.Contains("timed out", _listener.LastReason);
            Assert.IsTrue(_factory.Latest.IsDisposed);
        }

        [Test]
        public void Connect_AfterFailedAttempt_StartsFreshAttempt()
        {
            CreateClient();
            _client.Connect();
            _factory.Latest.Close();
            Poll();

            _client.Connect();
            _factory.Latest.Open();
            Poll();

            Assert.AreEqual(2, _factory.Sockets.Count);
            Assert.AreEqual(RelayConnectionState.Connected, _client.State);
        }

        [Test]
        public void ProcessIncomingMessages_BeforeConnect_DoesNothing()
        {
            CreateClient();

            Run(1);

            Assert.AreEqual(RelayConnectionState.Disconnected, _client.State);
            Assert.AreEqual(0, _factory.Sockets.Count);
            Assert.IsEmpty(_listener.States);
        }

        [Test]
        public void SubscriptionsMadeWhileConnecting_AreSentOnceConnected()
        {
            CreateClient();
            _client.Connect();
            _client.SubscribeToChannel("a");
            _client.SubscribeToChannel("b");

            Assert.IsEmpty(_factory.Latest.SentMessages);

            _factory.Latest.Open();
            Poll();

            CollectionAssert.AreEquivalent(new[] { "a", "b" }, _factory.Latest.SentSubscriptions());
            Assert.AreEqual(1, _factory.Latest.SentOfType(RelayMessageType.Ping).Count());
        }

        // Reconnecting

        [Test]
        public void ConnectionClosed_ReconnectsWithSameGuidAndSubscriptions()
        {
            CreateClient();
            FakeSocket firstSocket = ConnectAndOpen();
            _client.SubscribeToChannel("a");
            _client.SubscribeToChannel("b");

            firstSocket.Close("Server went away");
            Poll();

            Assert.AreEqual(RelayConnectionState.Reconnecting, _client.State);
            Assert.AreEqual("Server went away", _listener.LastReason);
            Assert.IsTrue(firstSocket.IsDisposed);

            double reconnectDelay = RunUntil(() => _factory.Sockets.Count == 2, 1);
            Assert.LessOrEqual(reconnectDelay, 0.25 + StepSeconds);

            FakeSocket secondSocket = _factory.Latest;
            secondSocket.Open();
            Poll();

            Assert.AreEqual(RelayConnectionState.Connected, _client.State);
            CollectionAssert.AreEquivalent(new[] { "a", "b" }, secondSocket.SentSubscriptions());
            CollectionAssert.AreEquivalent(new[] { "a", "b" }, _client.SubscribedChannels);
            CollectionAssert.AreEqual(
                new[] { RelayConnectionState.Connecting, RelayConnectionState.Connected, RelayConnectionState.Reconnecting, RelayConnectionState.Connected },
                _listener.States);

            Assert.IsTrue(_client.Send("a", new byte[] { 7 }));
            byte[] channelMessage = secondSocket.SentOfType(RelayMessageType.ChannelMessage).Single();
            Assert.AreEqual(ClientGuid, RelayMessageCodec.ReadGuid(channelMessage, RelayMessageCodec.ChannelMessageGuidOffset));
        }

        [Test]
        public void ReconnectDelays_GrowExponentiallyWithJitterUpToMaximum()
        {
            RelayConnectionSettings settings = CreateSettings();
            settings.ReconnectInitialDelaySeconds = 0.25f;
            settings.ReconnectMaxDelaySeconds = 2f;
            settings.ReconnectTimeoutSeconds = 1000f;
            CreateClient(settings);
            FakeSocket socket = ConnectAndOpen();

            List<double> attemptTimes = new();
            _factory.OnCreate = attempt =>
            {
                attemptTimes.Add(_clock.Now);
                attempt.Close("Connection refused");
            };

            double lossTime = _clock.Now + 0.005;
            socket.Close();
            RunUntil(() => attemptTimes.Count >= 12, 30, stepSeconds: 0.005);

            List<double> delays = new() { attemptTimes[0] - lossTime };
            for (int attemptIndex = 1; attemptIndex < attemptTimes.Count; attemptIndex++)
                delays.Add(attemptTimes[attemptIndex] - attemptTimes[attemptIndex - 1]);

            for (int attemptIndex = 0; attemptIndex < delays.Count; attemptIndex++)
            {
                double exponentialDelay = Math.Min(2d, 0.25d * Math.Pow(2d, attemptIndex));
                Assert.GreaterOrEqual(delays[attemptIndex], exponentialDelay / 2d - 0.006, $"Attempt {attemptIndex} came too early.");
                Assert.LessOrEqual(delays[attemptIndex], exponentialDelay + 0.006, $"Attempt {attemptIndex} came too late.");
            }

            List<double> cappedDelays = delays.Skip(4).ToList();
            Assert.Greater(cappedDelays.Max() - cappedDelays.Min(), 0.01, "Delays are not jittered.");
            Assert.AreEqual(RelayConnectionState.Reconnecting, _client.State);
        }

        [Test]
        public void Reconnect_GivesUpAfterReconnectTimeout()
        {
            RelayConnectionSettings settings = CreateSettings();
            settings.ReconnectTimeoutSeconds = 3f;
            CreateClient(settings);
            FakeSocket socket = ConnectAndOpen();

            socket.Close("Server went away");
            double giveUpTime = RunUntil(() => _client.State == RelayConnectionState.Disconnected, 10);

            Assert.AreEqual(3d, giveUpTime, 3 * StepSeconds);
            StringAssert.Contains("Could not reconnect within 3 s", _listener.LastReason);
            Assert.AreEqual(RelayConnectionState.Reconnecting, _listener.States[_listener.States.Count - 2]);
            Assert.IsTrue(_factory.Sockets.All(createdSocket => createdSocket.IsDisposed));

            int socketCount = _factory.Sockets.Count;
            Run(10);
            Assert.AreEqual(socketCount, _factory.Sockets.Count);
        }

        [Test]
        public void ReconnectAttempt_TimesOutAndIsRetried()
        {
            CreateClient();
            FakeSocket socket = ConnectAndOpen();

            socket.Close();
            RunUntil(() => _factory.Sockets.Count == 2, 1);
            FakeSocket hangingSocket = _factory.Latest;

            Run(1.9);
            Assert.IsFalse(hangingSocket.IsDisposed);

            RunUntil(() => _factory.Sockets.Count == 3, 1);
            Assert.IsTrue(hangingSocket.IsDisposed);

            _factory.Latest.Open();
            Poll();
            Assert.AreEqual(RelayConnectionState.Connected, _client.State);
        }

        [Test]
        public void ReconnectDisabled_ConnectionLossDisconnects()
        {
            RelayConnectionSettings settings = CreateSettings();
            settings.ReconnectTimeoutSeconds = 0f;
            CreateClient(settings);
            FakeSocket socket = ConnectAndOpen();

            socket.Close("Server went away");
            Poll();

            Assert.AreEqual(RelayConnectionState.Disconnected, _client.State);
            Assert.AreEqual("Server went away", _listener.LastReason);
            CollectionAssert.AreEqual(
                new[] { RelayConnectionState.Connecting, RelayConnectionState.Connected, RelayConnectionState.Disconnected },
                _listener.States);

            Run(5);
            Assert.AreEqual(1, _factory.Sockets.Count);
        }

        [Test]
        public void SuccessfulReconnect_ResetsBackoff()
        {
            CreateClient();
            FakeSocket socket = ConnectAndOpen();

            _factory.OnCreate = attempt => attempt.Close("Connection refused");
            socket.Close();
            RunUntil(() => _factory.Sockets.Count >= 5, 10);

            _factory.OnCreate = null;
            RunUntil(() => _factory.Latest.IsConnectCalled && !_factory.Latest.IsClosed, 10);
            _factory.Latest.Open();
            Poll();
            Assert.AreEqual(RelayConnectionState.Connected, _client.State);

            int socketCount = _factory.Sockets.Count;
            _factory.Latest.Close();
            Poll();
            double reconnectDelay = RunUntil(() => _factory.Sockets.Count > socketCount, 1);

            Assert.LessOrEqual(reconnectDelay, 0.25 + StepSeconds);
        }

        [Test]
        public void SubscriptionChangesWhileReconnecting_AreAppliedOnReconnect()
        {
            CreateClient();
            FakeSocket socket = ConnectAndOpen();
            _client.SubscribeToChannel("a");
            _client.SubscribeToChannel("b");

            socket.Close();
            Poll();
            _client.UnsubscribeFromChannel("a");
            _client.SubscribeToChannel("c");

            RunUntil(() => _factory.Sockets.Count == 2, 1);
            _factory.Latest.Open();
            Poll();

            CollectionAssert.AreEquivalent(new[] { "b", "c" }, _factory.Latest.SentSubscriptions());
            Assert.IsEmpty(_factory.Latest.SentUnsubscriptions());
        }

        // Heartbeat

        [Test]
        public void Heartbeat_PingsOncePerInterval()
        {
            CreateClient();
            FakeSocket socket = ConnectAndOpen();

            Run(10);

            int pingCount = socket.SentOfType(RelayMessageType.Ping).Count();
            Assert.That(pingCount, Is.InRange(10, 12));
            Assert.AreEqual(RelayConnectionState.Connected, _client.State);
        }

        [Test]
        public void Heartbeat_AnsweredPingsKeepConnectionAlive()
        {
            CreateClient();
            ConnectAndOpen();

            Run(60);

            Assert.AreEqual(RelayConnectionState.Connected, _client.State);
            Assert.IsTrue(_client.IsLinkHealthy);
            Assert.AreEqual(1, _factory.Sockets.Count);
        }

        [Test]
        public void Heartbeat_SilentConnectionTimesOutAndReconnects()
        {
            _factory.OnCreate = createdSocket => createdSocket.AnswersPings = false;
            CreateClient();
            FakeSocket socket = ConnectAndOpen();

            Run(4.9);
            Assert.AreEqual(RelayConnectionState.Connected, _client.State);

            LogAssert.Expect(LogType.Warning, new Regex("did not answer any heartbeat"));
            Run(0.2);

            Assert.AreEqual(RelayConnectionState.Reconnecting, _client.State);
            StringAssert.Contains("Nothing received for 5 s", _listener.LastReason);
            Assert.IsTrue(socket.IsDisposed);
        }

        [Test]
        public void Heartbeat_OutdatedServerWarningIsLoggedOnce()
        {
            int warningCount = 0;
            void CountWarnings(string message, string stackTrace, LogType type)
            {
                if (type == LogType.Warning && message.Contains("did not answer any heartbeat"))
                    warningCount++;
            }

            Application.logMessageReceived += CountWarnings;
            try
            {
                _factory.OnCreate = createdSocket => createdSocket.AnswersPings = false;
                CreateClient();
                ConnectAndOpen();

                for (int lossCount = 0; lossCount < 3; lossCount++)
                {
                    RunUntil(() => _client.State == RelayConnectionState.Reconnecting, 10);
                    RunUntil(() => _factory.Latest.IsConnectCalled && !_factory.Latest.IsClosed, 10);
                    _factory.Latest.Open();
                    Poll();
                }
            }
            finally
            {
                Application.logMessageReceived -= CountWarnings;
            }

            Assert.AreEqual(1, warningCount);
        }

        [Test]
        public void Heartbeat_AnyIncomingMessageKeepsConnectionAlive()
        {
            CreateClient();
            FakeSocket socket = ConnectAndOpen();
            socket.AnswersPings = false;
            _client.SubscribeToChannel("room");

            for (int second = 0; second < 20; second++)
            {
                socket.ReceiveChannelMessage(PeerGuid, "room", new byte[] { 1 });
                Run(1);
            }

            Assert.AreEqual(RelayConnectionState.Connected, _client.State);
        }

        [Test]
        public void IsLinkHealthy_FalseAfterTwoIntervalsOfSilenceAndRecovers()
        {
            _factory.OnCreate = createdSocket => createdSocket.AnswersPings = false;
            CreateClient();
            FakeSocket socket = ConnectAndOpen();

            Run(1.9);
            Assert.IsTrue(_client.IsLinkHealthy);

            Run(0.2);
            Assert.IsFalse(_client.IsLinkHealthy);
            Assert.AreEqual(RelayConnectionState.Connected, _client.State);

            socket.Receive(FakeSocket.CreatePong(LastPing(socket)));
            Poll();
            Assert.IsTrue(_client.IsLinkHealthy);
        }

        [Test]
        public void IsLinkHealthy_FalseWhileNotConnected()
        {
            CreateClient();
            Assert.IsFalse(_client.IsLinkHealthy);

            _client.Connect();
            Assert.IsFalse(_client.IsLinkHealthy);

            _factory.Latest.Open();
            Poll();
            Assert.IsTrue(_client.IsLinkHealthy);

            _factory.Latest.Close();
            Poll();
            Assert.IsFalse(_client.IsLinkHealthy);
        }

        [Test]
        public void FrameHitch_IsNotCountedAsSilence()
        {
            _factory.OnCreate = createdSocket => createdSocket.AnswersPings = false;
            CreateClient();
            ConnectAndOpen();

            Run(3);
            Poll(advanceSeconds: 4);
            Assert.AreEqual(RelayConnectionState.Connected, _client.State, "A 4 s hitch counts as a single short poll.");

            Run(1.68);
            Assert.AreEqual(RelayConnectionState.Connected, _client.State);

            LogAssert.Expect(LogType.Warning, new Regex("did not answer any heartbeat"));
            Run(0.2);
            Assert.AreEqual(RelayConnectionState.Reconnecting, _client.State);
        }

        [Test]
        public void PollGapLongerThanHeartbeatTimeout_ReconnectsRightAway()
        {
            CreateClient();
            ConnectAndOpen();

            Poll(advanceSeconds: 6);

            Assert.AreEqual(RelayConnectionState.Reconnecting, _client.State);
            StringAssert.Contains("Not updated for 6.0 s", _listener.LastReason);
        }

        [Test]
        public void PollGapLongerThanHeartbeatTimeout_DeliversQueuedMessagesBeforeReconnecting()
        {
            CreateClient();
            FakeSocket socket = ConnectAndOpen();
            _client.SubscribeToChannel("room");
            List<RelayConnectionState> statesAtDelivery = new();
            _listener.ChannelMessageReceived += (_, _, _) => statesAtDelivery.Add(_client.State);

            socket.ReceiveChannelMessage(PeerGuid, "room", new byte[] { 1 });
            Poll(advanceSeconds: 6);

            CollectionAssert.AreEqual(new[] { RelayConnectionState.Connected }, statesAtDelivery);
            Assert.AreEqual(RelayConnectionState.Reconnecting, _client.State);
        }

        // Round trip time

        [Test]
        public void RoundTripTime_MeasuresTimeUntilPongAndSmoothsSamples()
        {
            _factory.OnCreate = createdSocket => createdSocket.AnswersPings = false;
            CreateClient();
            FakeSocket socket = ConnectAndOpen();
            Assert.AreEqual(0d, _client.RoundTripTimeSeconds);

            _clock.Advance(0.1);
            socket.Receive(FakeSocket.CreatePong(LastPing(socket)));
            _client.ProcessIncomingMessages();
            Assert.AreEqual(0.1, _client.RoundTripTimeSeconds, 1e-9);

            int pingCount = socket.SentOfType(RelayMessageType.Ping).Count();
            RunUntil(() => socket.SentOfType(RelayMessageType.Ping).Count() > pingCount, 2);
            _clock.Advance(0.2);
            socket.Receive(FakeSocket.CreatePong(LastPing(socket)));
            _client.ProcessIncomingMessages();

            Assert.AreEqual(0.1 + 0.125 * (0.2 - 0.1), _client.RoundTripTimeSeconds, 1e-9);
        }

        [Test]
        public void RoundTripTime_IgnoresPongReadAfterFrameHitch()
        {
            _factory.OnCreate = createdSocket => createdSocket.AnswersPings = false;
            CreateClient();
            FakeSocket socket = ConnectAndOpen();

            _clock.Advance(0.5);
            socket.Receive(FakeSocket.CreatePong(LastPing(socket)));
            _client.ProcessIncomingMessages();

            Assert.AreEqual(0d, _client.RoundTripTimeSeconds);
            Assert.IsTrue(_client.IsLinkHealthy);
        }

        [Test]
        public void RoundTripTime_ResetsOnReconnect()
        {
            _factory.OnCreate = createdSocket => createdSocket.AnswersPings = false;
            CreateClient();
            FakeSocket socket = ConnectAndOpen();
            _clock.Advance(0.1);
            socket.Receive(FakeSocket.CreatePong(LastPing(socket)));
            _client.ProcessIncomingMessages();
            Assert.Greater(_client.RoundTripTimeSeconds, 0d);

            socket.Close();
            RunUntil(() => _factory.Sockets.Count == 2, 1);
            _factory.Latest.Open();
            Poll();

            Assert.AreEqual(RelayConnectionState.Connected, _client.State);
            Assert.AreEqual(0d, _client.RoundTripTimeSeconds);
        }

        [Test]
        public void MalformedPong_IsIgnoredButCountsAsLiveness()
        {
            _factory.OnCreate = createdSocket => createdSocket.AnswersPings = false;
            CreateClient();
            FakeSocket socket = ConnectAndOpen();
            int channelMessageCount = 0;
            _listener.ChannelMessageReceived += (_, _, _) => channelMessageCount++;

            for (int second = 0; second < 10; second++)
            {
                socket.Receive(new[] { RelayMessageType.Pong });
                socket.Receive(new[] { RelayMessageType.Pong, (byte)1, (byte)2 });
                Run(1);
            }

            Assert.AreEqual(RelayConnectionState.Connected, _client.State);
            Assert.AreEqual(0d, _client.RoundTripTimeSeconds);
            Assert.AreEqual(0, channelMessageCount);
        }

        // Sending

        [Test]
        public void Send_WhileReconnecting_ReturnsFalse()
        {
            CreateClient();
            FakeSocket socket = ConnectAndOpen();
            _client.SubscribeToChannel("room");

            socket.Close();
            Poll();

            Assert.IsFalse(_client.Send("room", new byte[] { 1 }));
        }

        [Test]
        public void Send_AfterSocketClosedSinceLastPoll_ReturnsFalse()
        {
            CreateClient();
            FakeSocket socket = ConnectAndOpen();
            _client.SubscribeToChannel("room");

            socket.Close();

            Assert.IsTrue(_client.IsConnected, "Closure is only noticed by the next poll.");
            Assert.IsFalse(_client.Send("room", new byte[] { 1 }));
            Assert.DoesNotThrow(() => _client.SubscribeToChannel("other"));
            Assert.DoesNotThrow(() => _client.UnsubscribeFromChannel("other"));
        }

        [Test]
        public void Send_ToChannelNotSubscribed_ThrowsKeyNotFoundException()
        {
            CreateClient();
            ConnectAndOpen();

            Assert.Throws<KeyNotFoundException>(() => _client.Send("room", new byte[] { 1 }));
        }

        [Test]
        public void IsSendBacklogged_FollowsPendingSendBytes()
        {
            CreateClient();
            FakeSocket socket = ConnectAndOpen();

            socket.PendingSendBytes = 1000;
            Assert.AreEqual(1000, _client.PendingSendBytes);
            Assert.IsFalse(_client.IsSendBacklogged);

            socket.PendingSendBytes = 1001;
            Assert.IsTrue(_client.IsSendBacklogged);

            socket.Close();
            Poll();
            Assert.AreEqual(0, _client.PendingSendBytes);
            Assert.IsFalse(_client.IsSendBacklogged);
        }

        // Dispatching

        [Test]
        public void PongsAreNotDispatchedAsChannelMessages()
        {
            CreateClient();
            ConnectAndOpen();
            _client.SubscribeToChannel("room");
            int channelMessageCount = 0;
            _listener.ChannelMessageReceived += (_, _, _) => channelMessageCount++;

            Run(5);

            Assert.AreEqual(0, channelMessageCount);
        }

        [Test]
        public void ChannelMessageHandlerException_IsLoggedAndLaterMessagesStillDelivered()
        {
            CreateClient();
            FakeSocket socket = ConnectAndOpen();
            _client.SubscribeToChannel("room");

            int deliveredCount = 0;
            _listener.ChannelMessageReceived += (_, _, _) =>
            {
                deliveredCount++;
                if (deliveredCount == 1)
                    throw new InvalidOperationException("Handler failed");
            };

            socket.ReceiveChannelMessage(PeerGuid, "room", new byte[] { 1 });
            socket.ReceiveChannelMessage(PeerGuid, "room", new byte[] { 2 });

            LogAssert.Expect(LogType.Exception, new Regex("Handler failed"));
            Poll();

            Assert.AreEqual(2, deliveredCount);
            Assert.AreEqual(RelayConnectionState.Connected, _client.State);
        }

        [Test]
        public void MessageQueuedAsSocketCloses_IsDeliveredBeforeReconnecting()
        {
            CreateClient();
            FakeSocket socket = ConnectAndOpen();
            _client.SubscribeToChannel("room");
            List<RelayConnectionState> statesAtDelivery = new();
            _listener.ChannelMessageReceived += (_, _, _) => statesAtDelivery.Add(_client.State);

            socket.OnQueueFoundEmpty = () =>
            {
                socket.ReceiveChannelMessage(PeerGuid, "room", new byte[] { 1 });
                socket.Close("Server went away");
            };
            Poll();
            Poll();

            CollectionAssert.AreEqual(new[] { RelayConnectionState.Connected }, statesAtDelivery);
            Assert.AreEqual(RelayConnectionState.Reconnecting, _client.State);
        }

        [Test]
        public void MalformedChannelMessage_IsLoggedAndConnectionContinues()
        {
            CreateClient();
            FakeSocket socket = ConnectAndOpen();
            _client.SubscribeToChannel("room");
            int deliveredCount = 0;
            _listener.ChannelMessageReceived += (_, _, _) => deliveredCount++;

            socket.Receive(new byte[] { RelayMessageType.ChannelMessage, 1, 2 });
            socket.ReceiveChannelMessage(PeerGuid, "room", new byte[] { 1 });

            LogAssert.Expect(LogType.Exception, new Regex("Incomplete channel message"));
            Poll();

            Assert.AreEqual(1, deliveredCount);
            Assert.AreEqual(RelayConnectionState.Connected, _client.State);
        }

        [Test]
        public void StateChangeHandlerException_IsLoggedAndStateStillChanges()
        {
            CreateClient();
            _listener.ConnectionStateChanged += (_, state, _) =>
            {
                if (state == RelayConnectionState.Connected)
                    throw new InvalidOperationException("State handler failed");
            };

            _client.Connect();
            _factory.Latest.Open();
            LogAssert.Expect(LogType.Exception, new Regex("State handler failed"));
            Poll();

            Assert.AreEqual(RelayConnectionState.Connected, _client.State);
        }

        // Disposing

        [Test]
        public void Dispose_WhileReconnecting_StopsAttemptsWithoutCallbacks()
        {
            CreateClient();
            FakeSocket socket = ConnectAndOpen();
            socket.Close();
            Poll();
            int stateChangeCount = _listener.States.Count;

            _client.Dispose();
            Run(10);

            Assert.AreEqual(RelayConnectionState.Disconnected, _client.State);
            Assert.AreEqual(1, _factory.Sockets.Count);
            Assert.AreEqual(stateChangeCount, _listener.States.Count);
        }

        [Test]
        public void Dispose_WhileConnecting_DisposesSocket()
        {
            CreateClient();
            _client.Connect();

            _client.Dispose();

            Assert.IsTrue(_factory.Latest.IsDisposed);
            CollectionAssert.AreEqual(new[] { RelayConnectionState.Connecting }, _listener.States);
        }

        [Test]
        public void Dispose_FromChannelMessageHandler_StopsDispatching()
        {
            CreateClient();
            FakeSocket socket = ConnectAndOpen();
            _client.SubscribeToChannel("room");
            int deliveredCount = 0;
            _listener.ChannelMessageReceived += (_, _, _) =>
            {
                deliveredCount++;
                _client.Dispose();
            };

            socket.ReceiveChannelMessage(PeerGuid, "room", new byte[] { 1 });
            socket.ReceiveChannelMessage(PeerGuid, "room", new byte[] { 2 });
            Poll();

            Assert.AreEqual(1, deliveredCount);
            Assert.AreEqual(RelayConnectionState.Disconnected, _client.State);
            Assert.IsTrue(socket.IsDisposed);
        }

        [Test]
        public void Dispose_FromStateChangeHandler_StopsReconnecting()
        {
            CreateClient();
            FakeSocket socket = ConnectAndOpen();
            _listener.ConnectionStateChanged += (_, state, _) =>
            {
                if (state == RelayConnectionState.Reconnecting)
                    _client.Dispose();
            };

            socket.Close();
            Poll();
            Run(5);

            Assert.AreEqual(RelayConnectionState.Disconnected, _client.State);
            Assert.AreEqual(1, _factory.Sockets.Count);
        }

        [Test]
        public void Connect_FromDisconnectedHandler_StartsNewAttempt()
        {
            RelayConnectionSettings settings = CreateSettings();
            settings.ReconnectTimeoutSeconds = 0f;
            CreateClient(settings);
            FakeSocket socket = ConnectAndOpen();
            _listener.ConnectionStateChanged += (_, state, _) =>
            {
                if (state == RelayConnectionState.Disconnected)
                    _client.Connect();
            };

            socket.Close();
            Poll();

            Assert.AreEqual(RelayConnectionState.Connecting, _client.State);
            Assert.AreEqual(2, _factory.Sockets.Count);
        }

        [Test]
        public void ReconnectAttempt_SocketFactoryException_IsLoggedAndRetried()
        {
            CreateClient();
            FakeSocket socket = ConnectAndOpen();

            void StopThrowingAfterFirstLog(string message, string stackTrace, LogType type)
            {
                if (type == LogType.Exception)
                    _factory.ExceptionToThrow = null;
            }

            Application.logMessageReceived += StopThrowingAfterFirstLog;
            try
            {
                _factory.ExceptionToThrow = new InvalidOperationException("Socket factory failed");
                LogAssert.Expect(LogType.Exception, new Regex("Socket factory failed"));
                socket.Close();
                RunUntil(() => _factory.Sockets.Count == 2, 2);
            }
            finally
            {
                Application.logMessageReceived -= StopThrowingAfterFirstLog;
            }

            _factory.Latest.Open();
            Poll();
            Assert.AreEqual(RelayConnectionState.Connected, _client.State);
        }

        // Settings

        [Test]
        public void Settings_InvalidValues_Throw()
        {
            AssertInvalid(settings => settings.ConnectTimeoutSeconds = 0f);
            AssertInvalid(settings => settings.HeartbeatIntervalSeconds = 0f);
            AssertInvalid(settings => settings.HeartbeatTimeoutSeconds = settings.HeartbeatIntervalSeconds);
            AssertInvalid(settings => settings.ReconnectTimeoutSeconds = -1f);
            AssertInvalid(settings => settings.ReconnectInitialDelaySeconds = 0f);
            AssertInvalid(settings => settings.ReconnectMaxDelaySeconds = settings.ReconnectInitialDelaySeconds / 2f);
            AssertInvalid(settings => settings.SendBacklogLimitBytes = -1);
            AssertInvalid(settings => settings.HeartbeatTimeoutSeconds = float.NaN);

            void AssertInvalid(Action<RelayConnectionSettings> makeInvalid)
            {
                RelayConnectionSettings settings = CreateSettings();
                makeInvalid(settings);
                Assert.Throws<ArgumentOutOfRangeException>(
                    () => new RelayClient(_factory.Create, _listener, ClientGuid, settings, _clock.GetTime));
            }
        }

        [Test]
        public void Settings_DefaultsAreValid()
        {
            Assert.DoesNotThrow(() => new RelayConnectionSettings().Validate());
        }

        [Test]
        public void Settings_AreCopiedOnConstruction()
        {
            RelayConnectionSettings settings = CreateSettings();
            CreateClient(settings);
            settings.ReconnectTimeoutSeconds = 0f;
            FakeSocket socket = ConnectAndOpen();

            socket.Close();
            Poll();

            Assert.AreEqual(RelayConnectionState.Reconnecting, _client.State);
        }
    }
}
