using System;
using System.Collections;
using System.Threading.Tasks;
using BananaParty.WebSocketRelay.Transport;

namespace BananaParty.WebSocketRelay.Tests
{
    public static class TestParameters
    {
        public const int RelayServerPort = 23144; // Leet for RELAY

        // An IP literal, because "localhost" can try IPv6 first and spend seconds on the refused attempt.
        public static readonly string RelayServerAddress = $"ws://127.0.0.1:{RelayServerPort}";

        public const float ConnectTimeoutThreshold = 3f;
        public const float ReceiveTimeoutThreshold = 5f;
        public const float DisconnectTimeoutThreshold = 3f;

        public static RelayConnectionSettings ReconnectDisabledSettings() => new()
        {
            ReconnectTimeoutSeconds = 0f,
        };

        /// <summary>
        /// Short timeouts, so connection loss is noticed and repaired within a test.
        /// </summary>
        public static RelayConnectionSettings FastReconnectSettings(float reconnectTimeoutSeconds = 15f) => new()
        {
            ConnectTimeoutSeconds = 1f,
            HeartbeatIntervalSeconds = 0.25f,
            HeartbeatTimeoutSeconds = 1.5f,
            ReconnectTimeoutSeconds = reconnectTimeoutSeconds,
            ReconnectInitialDelaySeconds = 0.05f,
            ReconnectMaxDelaySeconds = 0.25f,
        };

        public static IEnumerator WaitForTask(Task task, Action poll)
        {
            while (!task.IsCompleted)
            {
                poll?.Invoke();
                yield return null;
            }

            if (task.IsFaulted)
                throw task.Exception;
        }

        public static IEnumerator StopRelayServer(Action poll) => WaitForTask(RelayServerLauncher.StopAsync(), poll);

        public static IEnumerator StartRelayServer(Action poll)
        {
            Task<bool> task = RelayServerLauncher.EnsureRunningAsync();
            yield return WaitForTask(task, poll);

            if (!task.Result)
                throw new InvalidOperationException("Relay server is not reachable.");
        }

        public static IEnumerator WaitForCondition(Func<bool> condition, float timeoutSeconds, Action poll)
        {
            float elapsed = 0f;
            while (!condition() && elapsed < timeoutSeconds)
            {
                poll?.Invoke();
                yield return null;
                elapsed += UnityEngine.Time.deltaTime;
            }
        }

        public static IEnumerator WaitForDuration(float durationSeconds, Action poll)
        {
            float elapsed = 0f;
            while (elapsed < durationSeconds)
            {
                poll?.Invoke();
                yield return null;
                elapsed += UnityEngine.Time.deltaTime;
            }
        }

        public static IEnumerator WaitUntilRelayConnected(RelayClient relay, float timeoutSeconds = ConnectTimeoutThreshold)
        {
            yield return WaitForCondition(
                () => relay.IsConnected,
                timeoutSeconds,
                () => relay.ProcessIncomingMessages());
        }

        public static IEnumerator WaitUntilRelayConnected(
            RelayClient relayA,
            RelayClient relayB,
            float timeoutSeconds = ConnectTimeoutThreshold)
        {
            yield return WaitForCondition(
                () => relayA.IsConnected && relayB.IsConnected,
                timeoutSeconds,
                () =>
                {
                    relayA.ProcessIncomingMessages();
                    relayB.ProcessIncomingMessages();
                });
        }
    }
}
