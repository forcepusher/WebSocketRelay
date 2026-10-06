using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Debug = UnityEngine.Debug;

namespace BananaParty.WebSocketRelay.Transport
{
    /// <summary>
    /// Relay protocol client. Detects silently dropped connections with heartbeats
    /// and restores lost connections with the same client guid and channel subscriptions.
    /// </summary>
    public class RelayClient : IDisposable
    {
        // Longer gaps between polls count as this long, so a frame hitch is not mistaken for a silent connection.
        private const double MaxPollIntervalSeconds = 0.25;

        private const double RoundTripTimeSmoothingFactor = 0.125;

        private readonly Func<ISocket> _socketFactory;
        private readonly IRelayListener _relayListener;
        private readonly RelayConnectionSettings _settings;
        private readonly Func<double> _timeSource;
        private readonly Random _random = new();

        private ISocket _socket;
        private bool _isDisposed;

        // Advances only while polled, by at most MaxPollIntervalSeconds per poll. All timeouts are measured with it.
        private double _clock;
        private double _lastPollTime;

        private double _attemptStartTime;
        private double _nextAttemptTime;
        private double _reconnectDeadline;
        private int _reconnectAttemptCount;
        private string _lastFailureReason;

        private double _lastReceiveTime;
        private double _lastPingTime;
        private bool _hasRoundTripTime;
        private bool _hasReceivedPong;
        private bool _hasWarnedAboutMissingPong;

        public RelayClient(string serverAddress, IRelayListener relayListener, Guid clientGuid, bool offlineMode = false, RelayConnectionSettings settings = null)
            : this(CreateSocketFactory(serverAddress, offlineMode), relayListener, clientGuid, settings)
        {
        }

        /// <param name="socketFactory">Creates a fresh socket for every connection attempt.</param>
        /// <param name="timeSource">Monotonic time in seconds. Defaults to <see cref="Stopwatch"/>.</param>
        public RelayClient(Func<ISocket> socketFactory, IRelayListener relayListener, Guid clientGuid, RelayConnectionSettings settings = null, Func<double> timeSource = null)
        {
            _socketFactory = socketFactory ?? throw new ArgumentNullException(nameof(socketFactory));
            _relayListener = relayListener ?? throw new ArgumentNullException(nameof(relayListener));
            _settings = settings?.Clone() ?? new RelayConnectionSettings();
            _settings.Validate();
            _timeSource = timeSource ?? GetStopwatchSeconds;
            ClientGuid = clientGuid;
        }

        public Guid ClientGuid { get; }

        /// <summary>
        /// Channels to receive messages from. Kept while reconnecting and subscribed again once connected.
        /// </summary>
        public HashSet<string> SubscribedChannels { get; } = new();

        public RelayConnectionState State { get; private set; } = RelayConnectionState.Disconnected;

        public bool IsConnected => State == RelayConnectionState.Connected;

        /// <summary>
        /// False when nothing arrived for longer than two heartbeat intervals, which means the connection
        /// is likely interrupted even though it has not timed out yet.
        /// </summary>
        public bool IsLinkHealthy => IsConnected && _clock - _lastReceiveTime <= 2d * _settings.HeartbeatIntervalSeconds;

        /// <summary>
        /// Smoothed time for a heartbeat to reach the relay server and come back.
        /// 0 until the first heartbeat is answered on the current connection.
        /// </summary>
        public double RoundTripTimeSeconds { get; private set; }

        public int PendingSendBytes => _socket?.PendingSendBytes ?? 0;

        /// <summary>
        /// True when sent data piles up faster than the connection delivers it.
        /// </summary>
        public bool IsSendBacklogged => PendingSendBytes > _settings.SendBacklogLimitBytes;

        /// <summary>
        /// Starts a single connection attempt. If it fails, the state returns to
        /// <see cref="RelayConnectionState.Disconnected"/> and <see cref="Connect"/> can be called again.
        /// </summary>
        public void Connect()
        {
            ThrowIfDisposed();

            if (State != RelayConnectionState.Disconnected)
                throw new InvalidOperationException($"Trying to use {nameof(Connect)} while {State}.");

            _lastPollTime = _timeSource();
            OpenSocket();
            SetState(RelayConnectionState.Connecting, null);

            // Some sockets, like the offline one, open synchronously.
            if (State == RelayConnectionState.Connecting)
                UpdatePendingConnection();
        }

        /// <summary>
        /// Drains queued frames, dispatches channel messages, keeps the connection alive and restores it when lost.
        /// Call this every frame.
        /// </summary>
        public void ProcessIncomingMessages()
        {
            if (_isDisposed || State == RelayConnectionState.Disconnected)
                return;

            double pollGapSeconds = AdvanceClock();

            // The relay or a router in between has likely dropped the connection while the application was suspended,
            // so reconnecting right away is faster than waiting for the heartbeat to time out.
            // Messages that did arrive meanwhile are delivered first rather than thrown away with the socket.
            if (State == RelayConnectionState.Connected && pollGapSeconds > _settings.HeartbeatTimeoutSeconds)
            {
                DrainPayloadQueue(pollGapSeconds);
                if (State == RelayConnectionState.Connected)
                    LoseConnection($"Not updated for {pollGapSeconds:0.0} s");
            }

            if (State == RelayConnectionState.Connecting || State == RelayConnectionState.Reconnecting)
                UpdatePendingConnection();

            if (State == RelayConnectionState.Connected)
                UpdateConnected(pollGapSeconds);
        }

        public void SubscribeToChannel(string channel)
        {
            ThrowIfDisposed();

            if (!SubscribedChannels.Add(channel))
                return;

            if (IsConnected)
                TrySend(RelayMessageCodec.CreateProtocolMessage(RelayMessageType.Subscribe, channel));
        }

        public void UnsubscribeFromChannel(string channel)
        {
            ThrowIfDisposed();

            if (!SubscribedChannels.Remove(channel))
                throw new KeyNotFoundException($"Not subscribed to channel '{channel}'.");

            if (IsConnected)
                TrySend(RelayMessageCodec.CreateProtocolMessage(RelayMessageType.Unsubscribe, channel));
        }

        /// <returns>False when the message was dropped because the client is not connected.</returns>
        public bool Send(string channel, byte[] data)
        {
            ThrowIfDisposed();

            if (!SubscribedChannels.Contains(channel))
                throw new KeyNotFoundException($"Not subscribed to channel '{channel}'.");

            if (!IsConnected)
                return false;

            return TrySend(RelayMessageCodec.CreateChannelMessage(ClientGuid, channel, data));
        }

        /// <summary>
        /// Closes the connection without notifying the listener.
        /// </summary>
        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            DisposeSocket();
            State = RelayConnectionState.Disconnected;
        }

        private static Func<ISocket> CreateSocketFactory(string serverAddress, bool offlineMode)
        {
            if (offlineMode)
                return () => new OfflineSocket();

            return () => new Socket(serverAddress);
        }

        private static double GetStopwatchSeconds() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

        private double AdvanceClock()
        {
            double now = _timeSource();
            double pollGapSeconds = Math.Max(0d, now - _lastPollTime);
            _lastPollTime = now;
            _clock += Math.Min(pollGapSeconds, MaxPollIntervalSeconds);
            return pollGapSeconds;
        }

        private void OpenSocket()
        {
            ISocket socket = _socketFactory();
            try
            {
                socket.Connect();
            }
            catch
            {
                socket.Dispose();
                throw;
            }

            _socket = socket;
            _attemptStartTime = _clock;
        }

        private void UpdatePendingConnection()
        {
            if (_socket == null)
            {
                if (_clock >= _reconnectDeadline)
                {
                    GiveUp();
                    return;
                }

                if (_clock < _nextAttemptTime || !TryStartReconnectAttempt())
                    return;
            }

            if (_socket.IsConnected)
            {
                OnSocketOpened();
                return;
            }

            if (_socket.IsClosed)
            {
                FailAttempt(_socket.CloseReason ?? "Connection closed");
                return;
            }

            if (State == RelayConnectionState.Reconnecting && _clock >= _reconnectDeadline)
            {
                GiveUp();
                return;
            }

            if (_clock - _attemptStartTime >= _settings.ConnectTimeoutSeconds)
                FailAttempt($"Connection attempt timed out after {_settings.ConnectTimeoutSeconds} s");
        }

        private bool TryStartReconnectAttempt()
        {
            try
            {
                OpenSocket();
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                FailAttempt($"Could not open socket: {exception.Message}");
                return false;
            }
        }

        private void FailAttempt(string reason)
        {
            DisposeSocket();

            if (State == RelayConnectionState.Connecting)
            {
                SetState(RelayConnectionState.Disconnected, reason);
                return;
            }

            _lastFailureReason = reason;
            ScheduleReconnectAttempt();
        }

        private void ScheduleReconnectAttempt()
        {
            double exponentialDelay = Math.Min(
                _settings.ReconnectMaxDelaySeconds,
                _settings.ReconnectInitialDelaySeconds * Math.Pow(2d, Math.Min(_reconnectAttemptCount, 30)));
            _reconnectAttemptCount++;

            // Equal jitter keeps a minimum wait while spreading out clients that lost the connection at the same moment.
            _nextAttemptTime = _clock + exponentialDelay * (0.5d + 0.5d * _random.NextDouble());
        }

        private void GiveUp()
        {
            DisposeSocket();
            SetState(RelayConnectionState.Disconnected, $"Could not reconnect within {_settings.ReconnectTimeoutSeconds} s: {_lastFailureReason}");
        }

        private void OnSocketOpened()
        {
            _reconnectAttemptCount = 0;
            _lastFailureReason = null;
            _lastReceiveTime = _clock;
            _hasReceivedPong = false;
            _hasRoundTripTime = false;
            RoundTripTimeSeconds = 0d;

            foreach (string channel in SubscribedChannels)
                TrySend(RelayMessageCodec.CreateProtocolMessage(RelayMessageType.Subscribe, channel));

            SendPing();
            SetState(RelayConnectionState.Connected, null);
        }

        private void UpdateConnected(double pollGapSeconds)
        {
            // Read before draining, so messages that arrived right before the socket closed are still delivered.
            bool isSocketOpen = _socket.IsConnected;

            DrainPayloadQueue(pollGapSeconds);

            // The listener may have disposed this client while handling a message.
            if (State != RelayConnectionState.Connected)
                return;

            if (!isSocketOpen)
            {
                LoseConnection(_socket.CloseReason ?? "Connection closed");
                return;
            }

            if (_clock - _lastReceiveTime >= _settings.HeartbeatTimeoutSeconds)
            {
                WarnIfHeartbeatsWereNeverAnswered();
                LoseConnection($"Nothing received for {_settings.HeartbeatTimeoutSeconds} s");
                return;
            }

            if (_clock - _lastPingTime >= _settings.HeartbeatIntervalSeconds)
                SendPing();
        }

        private void DrainPayloadQueue(double pollGapSeconds)
        {
            while (State == RelayConnectionState.Connected && _socket.HasUnreadPayloadQueue)
            {
                byte[] payloadBytes = _socket.ReadPayloadQueue();
                _lastReceiveTime = _clock;
                HandlePayload(payloadBytes, pollGapSeconds);
            }
        }

        private void HandlePayload(byte[] payloadBytes, double pollGapSeconds)
        {
            if (payloadBytes.Length > 0 && payloadBytes[0] == RelayMessageType.Pong)
            {
                HandlePong(payloadBytes, pollGapSeconds);
                return;
            }

            try
            {
                DispatchChannelMessage(payloadBytes);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private void DispatchChannelMessage(byte[] payloadBytes)
        {
            if (payloadBytes.Length == 0 || payloadBytes[0] != RelayMessageType.ChannelMessage)
                return;

            int channelLength = RelayMessageCodec.ReadChannelLength(payloadBytes, RelayMessageCodec.ChannelMessageChannelLengthOffset);
            if (channelLength < 0)
                throw new InvalidDataException("Incomplete channel message.");

            int payloadOffset = RelayMessageCodec.GetChannelMessagePayloadOffset(channelLength);
            if (payloadBytes.Length < payloadOffset)
                throw new InvalidDataException("Incomplete channel message.");

            string channel = RelayMessageCodec.ReadChannel(payloadBytes, RelayMessageCodec.ChannelMessageChannelLengthOffset);
            if (!SubscribedChannels.Contains(channel))
                return;

            Guid senderGuid = RelayMessageCodec.ReadGuid(payloadBytes, RelayMessageCodec.ChannelMessageGuidOffset);
            byte[] messageData = new byte[payloadBytes.Length - payloadOffset];
            Array.Copy(payloadBytes, payloadOffset, messageData, 0, messageData.Length);
            _relayListener.OnChannelMessage(senderGuid, channel, messageData);
        }

        private void HandlePong(byte[] payloadBytes, double pollGapSeconds)
        {
            _hasReceivedPong = true;

            // A pong read after a frame hitch has waited in the queue, so its round trip would include the hitch.
            if (pollGapSeconds > MaxPollIntervalSeconds)
                return;

            if (!RelayMessageCodec.TryReadPongSentTime(payloadBytes, out double sentTime))
                return;

            double roundTripTime = _lastPollTime - sentTime;
            if (!(roundTripTime >= 0d))
                return;

            RoundTripTimeSeconds = _hasRoundTripTime
                ? RoundTripTimeSeconds + RoundTripTimeSmoothingFactor * (roundTripTime - RoundTripTimeSeconds)
                : roundTripTime;
            _hasRoundTripTime = true;
        }

        private void SendPing()
        {
            _lastPingTime = _clock;
            TrySend(RelayMessageCodec.CreatePingMessage(_lastPollTime));
        }

        private void WarnIfHeartbeatsWereNeverAnswered()
        {
            if (_hasReceivedPong || _hasWarnedAboutMissingPong)
                return;

            _hasWarnedAboutMissingPong = true;
            Debug.LogWarning("Relay server did not answer any heartbeat on this connection. "
                + "If connections keep timing out, the relay server may be outdated and not support heartbeats.");
        }

        private void LoseConnection(string reason)
        {
            DisposeSocket();

            if (_settings.ReconnectTimeoutSeconds <= 0f)
            {
                SetState(RelayConnectionState.Disconnected, reason);
                return;
            }

            _lastFailureReason = reason;
            _reconnectAttemptCount = 0;
            _reconnectDeadline = _clock + _settings.ReconnectTimeoutSeconds;
            ScheduleReconnectAttempt();
            SetState(RelayConnectionState.Reconnecting, reason);
        }

        private bool TrySend(byte[] messageBytes)
        {
            try
            {
                _socket.Send(messageBytes);
                return true;
            }
            catch (InvalidOperationException) when (!_socket.IsConnected)
            {
                // Closed since the last poll. The next poll notices and handles it.
                return false;
            }
        }

        private void SetState(RelayConnectionState state, string reason)
        {
            RelayConnectionState previousState = State;
            State = state;

            try
            {
                _relayListener.OnConnectionStateChanged(previousState, state, reason);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private void DisposeSocket()
        {
            ISocket socket = _socket;
            _socket = null;
            socket?.Dispose();
        }

        private void ThrowIfDisposed()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(RelayClient));
        }
    }
}
