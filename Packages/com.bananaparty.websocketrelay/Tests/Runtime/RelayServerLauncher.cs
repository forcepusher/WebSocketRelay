using System;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BananaParty.WebSocketRelay.Transport;

namespace BananaParty.WebSocketRelay.Tests
{
    /// <summary>
    /// Runs the bundled relay server for tests. On WebGL players, where processes cannot be started, the server is expected to run already.
    /// </summary>
    public static class RelayServerLauncher
    {
#if !UNITY_WEBGL || UNITY_EDITOR
        private const int ServerStartupTimeoutMs = 15000;
        private const int ResponseTimeoutMs = 500;

        private static readonly SemaphoreSlim s_gate = new(1, 1);
        private static RelayServerProcess s_serverProcess;
#endif

        public static IEnumerator StartCoroutine()
        {
            Task<bool> task = EnsureRunningAsync();
            while (!task.IsCompleted)
                yield return null;

            if (task.IsFaulted)
                throw task.Exception;

            if (!task.Result)
                throw new InvalidOperationException("Relay server is not reachable.");
        }

        public static IEnumerator StopCoroutine()
        {
            Task task = StopAsync();
            while (!task.IsCompleted)
                yield return null;

            if (task.IsFaulted)
                throw task.Exception;
        }

        public static Task<bool> EnsureRunningAsync()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return Task.FromResult(true);
#else
            return EnsureRunningInternalAsync();
#endif
        }

        public static Task StopAsync()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return Task.CompletedTask;
#else
            return StopInternalAsync();
#endif
        }

#if !UNITY_WEBGL || UNITY_EDITOR
        // A server that dies before answering, which the bundled Bun does now and then on Windows, is started again.
        private static async Task<bool> EnsureRunningInternalAsync()
        {
            await s_gate.WaitAsync().ConfigureAwait(false);
            try
            {
                DateTime deadline = DateTime.Now.AddMilliseconds(ServerStartupTimeoutMs);
                while (DateTime.Now < deadline)
                {
                    if (s_serverProcess == null || !s_serverProcess.IsRunning)
                        StartServerProcess();

                    if (await IsServerRespondingAsync(TestParameters.RelayServerPort).ConfigureAwait(false))
                        return true;

                    await Task.Delay(100).ConfigureAwait(false);
                }

                UnityEngine.Debug.LogError($"Relay server did not answer on port {TestParameters.RelayServerPort} in time.");
                return false;
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogError($"Failed to start the relay server: {exception.Message}");
                return false;
            }
            finally
            {
                s_gate.Release();
            }
        }

        private static async Task StopInternalAsync()
        {
            await s_gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (s_serverProcess == null)
                    return;

                s_serverProcess.Stop();
                s_serverProcess = null;
                UnityEngine.Debug.Log("Stopped local relay server.");
            }
            finally
            {
                s_gate.Release();
            }
        }

        private static void StartServerProcess()
        {
            s_serverProcess?.Stop();
            s_serverProcess = new RelayServerProcess();
            s_serverProcess.Start(verboseDebug: true, createNoWindow: true, relayPort: TestParameters.RelayServerPort);
            UnityEngine.Debug.Log($"Started local relay server on port {TestParameters.RelayServerPort}.");
        }

        // Asks for a response rather than only connecting, because a port can stay open without a server answering behind it.
        // Plain HTTP over a socket, so no proxy lookup gets in the way.
        private static async Task<bool> IsServerRespondingAsync(int port)
        {
            try
            {
                using TcpClient client = new();
                using CancellationTokenSource timeoutTokenSource = new(ResponseTimeoutMs);
                using CancellationTokenRegistration timeoutRegistration = timeoutTokenSource.Token.Register(client.Dispose);

                await client.ConnectAsync(IPAddress.Loopback, port).ConfigureAwait(false);
                NetworkStream stream = client.GetStream();
                byte[] request = Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\nHost: 127.0.0.1\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(request, 0, request.Length).ConfigureAwait(false);

                byte[] response = new byte[12];
                int responseLength = await stream.ReadAsync(response, 0, response.Length).ConfigureAwait(false);
                return Encoding.ASCII.GetString(response, 0, responseLength).StartsWith("HTTP/1.1 200", StringComparison.Ordinal);
            }
            catch (Exception)
            {
                // Refused, reset, or cut off by the timeout. Any of them means not ready yet.
                return false;
            }
        }
#endif
    }
}
