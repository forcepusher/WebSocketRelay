using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace BananaParty.WebSocketRelay.Transport
{
    /// <summary>
    /// Runs the relay server bundled with the package on the Bun runtime bundled with it.
    /// </summary>
    public class RelayServerProcess
    {
        private const string PackageName = "com.bananaparty.websocketrelay";
        private const string ProcessMarker = "-relay-server";
        private const string EntryScript = "Source/index.ts";

        private static string s_serverDirectory;

        private Process _process;

        public bool IsRunning => _process != null && !_process.HasExited;

        /// <summary>
        /// The package's RelayServer~ folder, wherever the package manager put the package.
        /// </summary>
        public static string GetServerDirectory() => s_serverDirectory ??= FindServerDirectory();

        public static string GetBunPath() => GetBunPath(GetServerDirectory());

#if UNITY_EDITOR
        // Package locations can only be looked up on the main thread, while servers are also started from background threads.
        [UnityEditor.InitializeOnLoadMethod]
        private static void CacheServerDirectory()
        {
            s_serverDirectory = FindServerDirectory();
        }
#endif

        private static string FindServerDirectory()
        {
#if UNITY_EDITOR
            // Packages installed from git or a registry live in Library/PackageCache rather than in the Packages folder.
            UnityEditor.PackageManager.PackageInfo packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(RelayServerProcess).Assembly);
            if (packageInfo != null)
                return Path.Combine(packageInfo.resolvedPath, "Runtime", "RelayServer~");
#endif
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Packages", PackageName, "Runtime", "RelayServer~"));
        }

        public void Start(bool verboseDebug = false, bool createNoWindow = false, int? relayPort = null)
        {
            if (IsRunning)
                return;

            KillAll();
            _process = Launch(verboseDebug, createNoWindow, relayPort);
        }

        public void Stop()
        {
            if (_process == null)
                return;

            try
            {
                KillAll();
                if (!_process.HasExited)
                {
                    _process.Kill();
                    _process.WaitForExit(5000);
                }
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogWarning($"Failed to stop server process: {exception.Message}");
            }
            finally
            {
                _process.Dispose();
                _process = null;
            }
        }

        /// <summary>
        /// Stops every relay server started from the bundled Bun runtime, including ones left behind by earlier sessions.
        /// </summary>
        public static void KillAll()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                KillAllWindows();
            else
                KillAllUnix();
        }

        private static void KillAllWindows()
        {
            string bunPath = Path.GetFullPath(GetBunPath());

            foreach (Process process in Process.GetProcessesByName("bun"))
            {
                using (process)
                {
                    try
                    {
                        if (process.HasExited || !Path.GetFullPath(process.MainModule.FileName).Equals(bunPath, StringComparison.OrdinalIgnoreCase))
                            continue;

                        process.Kill();
                        process.WaitForExit(5000);
                    }
                    catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
                    {
                        // Exited meanwhile, or belongs to another user or an elevated process, so it is not ours anyway.
                    }
                }
            }
        }

        private static void KillAllUnix()
        {
            ProcessStartInfo startInfo = new()
            {
                FileName = "pkill",
                Arguments = $"-f \"{Path.GetFullPath(GetBunPath())}.*{ProcessMarker}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
            };

            using Process process = Process.Start(startInfo);
            process?.WaitForExit(5000);
        }

        private static Process Launch(bool verboseDebug, bool createNoWindow, int? relayPort)
        {
            string serverDirectory = GetServerDirectory();
            string bunPath = GetBunPath(serverDirectory);
            if (!File.Exists(bunPath))
                throw new FileNotFoundException($"Bundled Bun runtime not found at: {bunPath}");

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                MakeExecutable(bunPath);

            ProcessStartInfo startInfo = new()
            {
                FileName = bunPath,
                Arguments = $"{EntryScript} {ProcessMarker}",
                WorkingDirectory = serverDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = createNoWindow,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };

            startInfo.Environment["RELAY_DEBUG"] = verboseDebug ? "1" : "0";

            // When Bun crashes on Windows, its crash reporter starts a PowerShell process that inherits the
            // listening socket and keeps the port open without answering, so no new server could take over.
            startInfo.Environment["BUN_ENABLE_CRASH_REPORTING"] = "0";

            if (relayPort.HasValue)
                startInfo.Environment["RELAY_PORT"] = relayPort.Value.ToString();

            Process process = Process.Start(startInfo);
            process.OutputDataReceived += (_, e) => ForwardLine(e.Data, isError: false);
            process.ErrorDataReceived += (_, e) => ForwardLine(e.Data, isError: true);
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            return process;
        }

        /// <summary>
        /// Copies of the package made on Windows lose the executable bit that Bun needs on macOS and Linux.
        /// </summary>
        private static void MakeExecutable(string path)
        {
            ProcessStartInfo startInfo = new()
            {
                FileName = "chmod",
                Arguments = $"+x \"{path}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
            };

            using Process process = Process.Start(startInfo);
            process?.WaitForExit(5000);
        }

        private static string GetBunPath(string serverDirectory)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return Path.Combine(serverDirectory, "Bun", "bun-windows-x64", "bun.exe");
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return Path.Combine(serverDirectory, "Bun", "bun-darwin-aarch64", "bun");
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return Path.Combine(serverDirectory, "Bun", "bun-linux-x64", "bun");

            throw new PlatformNotSupportedException("Unsupported operating system");
        }

        private static void ForwardLine(string line, bool isError)
        {
            if (string.IsNullOrEmpty(line))
                return;

            if (isError)
                UnityEngine.Debug.LogWarning($"[RelayServer] {line}");
            else
                UnityEngine.Debug.Log($"[RelayServer] {line}");
        }
    }
}
