using System;
using UnityEngine;

namespace BananaParty.WebSocketRelay.Transport
{
    [Serializable]
    public class RelayConnectionSettings
    {
        [SerializeField]
        [Tooltip("How long a single connection attempt may take before it is abandoned.")]
        private float _connectTimeoutSeconds = 10f;

        [SerializeField]
        [Tooltip("How often the relay server is pinged to measure round trip time and keep the connection alive.")]
        private float _heartbeatIntervalSeconds = 1f;

        [SerializeField]
        [Tooltip("How long the connection may stay silent before it is considered lost. Must be longer than the heartbeat interval.")]
        private float _heartbeatTimeoutSeconds = 5f;

        [SerializeField]
        [Tooltip("How long to keep trying to restore a lost connection before giving up. 0 disables reconnecting.")]
        private float _reconnectTimeoutSeconds = 30f;

        [SerializeField]
        [Tooltip("Delay before the first reconnect attempt. Doubles after every failed attempt.")]
        private float _reconnectInitialDelaySeconds = 0.25f;

        [SerializeField]
        [Tooltip("Upper bound for the delay between reconnect attempts.")]
        private float _reconnectMaxDelaySeconds = 5f;

        [SerializeField]
        [Tooltip("Unsent bytes above which the connection counts as backlogged and state syncs are skipped.")]
        private int _sendBacklogLimitBytes = 16 * 1024;

        public float ConnectTimeoutSeconds
        {
            get => _connectTimeoutSeconds;
            set => _connectTimeoutSeconds = value;
        }

        public float HeartbeatIntervalSeconds
        {
            get => _heartbeatIntervalSeconds;
            set => _heartbeatIntervalSeconds = value;
        }

        public float HeartbeatTimeoutSeconds
        {
            get => _heartbeatTimeoutSeconds;
            set => _heartbeatTimeoutSeconds = value;
        }

        public float ReconnectTimeoutSeconds
        {
            get => _reconnectTimeoutSeconds;
            set => _reconnectTimeoutSeconds = value;
        }

        public float ReconnectInitialDelaySeconds
        {
            get => _reconnectInitialDelaySeconds;
            set => _reconnectInitialDelaySeconds = value;
        }

        public float ReconnectMaxDelaySeconds
        {
            get => _reconnectMaxDelaySeconds;
            set => _reconnectMaxDelaySeconds = value;
        }

        public int SendBacklogLimitBytes
        {
            get => _sendBacklogLimitBytes;
            set => _sendBacklogLimitBytes = value;
        }

        public RelayConnectionSettings Clone() => (RelayConnectionSettings)MemberwiseClone();

        /// <exception cref="ArgumentOutOfRangeException">Thrown when a value is out of its valid range.</exception>
        public void Validate()
        {
            RequirePositive(_connectTimeoutSeconds, nameof(ConnectTimeoutSeconds));
            RequirePositive(_heartbeatIntervalSeconds, nameof(HeartbeatIntervalSeconds));
            RequirePositive(_reconnectInitialDelaySeconds, nameof(ReconnectInitialDelaySeconds));

            if (!(_heartbeatTimeoutSeconds > _heartbeatIntervalSeconds))
                throw new ArgumentOutOfRangeException(nameof(HeartbeatTimeoutSeconds), _heartbeatTimeoutSeconds, $"Must be greater than {nameof(HeartbeatIntervalSeconds)} ({_heartbeatIntervalSeconds}).");

            if (!(_reconnectTimeoutSeconds >= 0f))
                throw new ArgumentOutOfRangeException(nameof(ReconnectTimeoutSeconds), _reconnectTimeoutSeconds, "Must not be negative.");

            if (!(_reconnectMaxDelaySeconds >= _reconnectInitialDelaySeconds))
                throw new ArgumentOutOfRangeException(nameof(ReconnectMaxDelaySeconds), _reconnectMaxDelaySeconds, $"Must not be less than {nameof(ReconnectInitialDelaySeconds)} ({_reconnectInitialDelaySeconds}).");

            if (_sendBacklogLimitBytes < 0)
                throw new ArgumentOutOfRangeException(nameof(SendBacklogLimitBytes), _sendBacklogLimitBytes, "Must not be negative.");
        }

        private static void RequirePositive(float value, string name)
        {
            if (!(value > 0f))
                throw new ArgumentOutOfRangeException(name, value, "Must be greater than zero.");
        }
    }
}
