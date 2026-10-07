using System;
using BananaParty.WebSocketRelay.Transport;
using UnityEngine;

namespace BananaParty.WebSocketRelay
{
    public class Network : IRelayListener, IDisposable
    {
        private readonly NetworkContext _networkContext;
        private readonly string _serverAddress;
        private readonly bool _offlineMode;
        private readonly Func<ISocket> _socketFactory;
        private readonly Func<double> _timeSource;
        private readonly RelayConnectionSettings _connectionSettings;

        private RelayServerProcess _relayServerProcess;
        private RelayClient _relayClient;

        public bool IsConnected => _relayClient != null && _relayClient.IsConnected;
        public bool HasRelayClient => _relayClient != null;

        /// <summary>
        /// While <see cref="RelayConnectionState.Reconnecting"/>, the session is kept and resumes once connected.
        /// </summary>
        public RelayConnectionState ConnectionState => _relayClient?.State ?? RelayConnectionState.Disconnected;

        /// <inheritdoc cref="RelayClient.RoundTripTimeSeconds"/>
        public double RoundTripTimeSeconds => _relayClient?.RoundTripTimeSeconds ?? 0d;

        public Network(string address, NetworkContext context, bool offlineMode = false, RelayConnectionSettings connectionSettings = null)
        {
            _serverAddress = address;
            _networkContext = context;
            _offlineMode = offlineMode;
            _connectionSettings = connectionSettings?.Clone() ?? new RelayConnectionSettings();
            _connectionSettings.Validate();
        }

        /// <summary>
        /// Connects through sockets created by <paramref name="socketFactory"/>, e.g. to simulate an unreliable network.
        /// </summary>
        /// <param name="timeSource">Monotonic time in seconds for connection timeouts. Defaults to a stopwatch.</param>
        public Network(Func<ISocket> socketFactory, NetworkContext context, RelayConnectionSettings connectionSettings = null, Func<double> timeSource = null)
            : this((string)null, context, offlineMode: false, connectionSettings)
        {
            _socketFactory = socketFactory ?? throw new ArgumentNullException(nameof(socketFactory));
            _timeSource = timeSource;
        }

        public void StartServer()
        {
            // Offline mode has no relay server to launch.
            if (_offlineMode)
                return;

            if (_relayServerProcess != null)
                throw new InvalidOperationException("Server already running");

            _relayServerProcess = new RelayServerProcess();
            _relayServerProcess.Start();
        }

        public void StopServer()
        {
            if (_offlineMode)
                return;

            if (_relayServerProcess == null)
                throw new InvalidOperationException("Server not started to stop it");

            _relayServerProcess.Stop();
            _relayServerProcess = null;
            Debug.Log("Relay server stopped.");
        }

        public void Connect(Guid clientGuid)
        {
            if (_relayClient != null)
                throw new InvalidOperationException("Already connected");

            WarnIfPlayerTimeoutIsTooShort();

            _networkContext.LocalClientIdentity = clientGuid;

            _relayClient = _socketFactory != null
                ? new RelayClient(_socketFactory, this, clientGuid, _connectionSettings, _timeSource)
                : new RelayClient(_serverAddress, this, clientGuid, _offlineMode, _connectionSettings);

            Debug.Log(_offlineMode
                ? "Started offline mode session"
                : $"Connecting to relay server at {_serverAddress}");

            try
            {
                _relayClient.Connect();
            }
            catch
            {
                _relayClient.Dispose();
                _relayClient = null;
                _networkContext.LocalClientIdentity = Guid.Empty;
                throw;
            }
        }

        public void Disconnect(bool clearSession = true)
        {
            if (_relayClient == null)
                throw new InvalidOperationException("Not connected to disconnect");

            RelayClient relayClient = _relayClient;
            _relayClient = null;
            _networkContext.IsConnectionInterrupted = false;

            if (clearSession)
                _networkContext.ClearNetworkSession();

            relayClient.Dispose();
        }

        public void ManualUpdate(float unscaledDeltaTime)
        {
            _relayClient?.ProcessIncomingMessages();
            _networkContext.IsConnectionInterrupted = _relayClient != null && !_relayClient.IsLinkHealthy;
            _networkContext.ManualUpdate(unscaledDeltaTime);
            SendQueuedRpcMessages();
        }

        public void SendSyncIdentities()
        {
            if (!IsConnected)
                return;

            // Every sync carries the full owned state, so skipping one while the connection catches up loses nothing.
            if (_relayClient.IsSendBacklogged)
                return;

            foreach (string channel in _relayClient.SubscribedChannels)
            {
                byte[] payload = _networkContext.GetOwnedNetworkIdentitiesPayload(channel);
                byte[] message = new byte[payload.Length + 1];
                message[0] = NetworkMessage.SyncIdentities;
                payload.CopyTo(message, 1);
                _relayClient.SendState(channel, message);
            }
        }

        public void SubscribeToChannel(string channel)
        {
            if (_relayClient == null)
                throw new InvalidOperationException("Not connected to subscribe to a channel");

            _relayClient.SubscribeToChannel(channel);
        }

        public void UnsubscribeFromChannel(string channel)
        {
            if (_relayClient == null)
                throw new InvalidOperationException("Not connected to unsubscribe from a channel");

            _relayClient.UnsubscribeFromChannel(channel);
        }

        public void Dispose()
        {
            _relayServerProcess?.Stop();
            _relayServerProcess = null;

            if (_relayClient != null)
                Disconnect();
        }

        void IRelayListener.OnConnectionStateChanged(RelayConnectionState previousState, RelayConnectionState state, string reason)
        {
            switch (state)
            {
                case RelayConnectionState.Connected:
                    if (!_offlineMode)
                        Debug.Log(previousState == RelayConnectionState.Reconnecting ? "Reconnected to relay server" : "Connected to relay server");
                    break;

                case RelayConnectionState.Reconnecting:
                    Debug.LogWarning($"Lost connection to relay server: {reason}. Reconnecting.");
                    break;

                // The relay client is kept after a failed first attempt, so callers can tell it apart from not connecting at all.
                case RelayConnectionState.Disconnected when previousState == RelayConnectionState.Connecting:
                    Debug.LogWarning($"Could not connect to relay server: {reason}");
                    break;

                case RelayConnectionState.Disconnected:
                    Debug.LogWarning($"Disconnected from relay server: {reason}");
                    Disconnect();
                    break;
            }
        }

        void IRelayListener.OnChannelMessage(Guid senderGuid, string channel, byte[] data)
        {
            _networkContext.ProcessChannelMessage(senderGuid, channel, data);
        }

        private void SendQueuedRpcMessages()
        {
            // RPCs stay queued while reconnecting and are delivered once the connection is back.
            if (!IsConnected)
                return;

            while (_networkContext.TryDequeueOutgoingRpcMessage(out string channel, out byte[] message))
            {
                // The connection closed since the last poll. Later messages stay queued for the reconnect.
                if (!_relayClient.Send(channel, message))
                    break;
            }
        }

        private void WarnIfPlayerTimeoutIsTooShort()
        {
            if (_offlineMode || _networkContext.PlayerTimeoutSeconds > _connectionSettings.HeartbeatTimeoutSeconds)
                return;

            Debug.LogWarning($"Player timeout ({_networkContext.PlayerTimeoutSeconds} s) is not longer than the heartbeat timeout "
                + $"({_connectionSettings.HeartbeatTimeoutSeconds} s), so other players drop this client before it notices "
                + "a lost connection and reconnects. Increase the player timeout on the network context.");
        }
    }
}
