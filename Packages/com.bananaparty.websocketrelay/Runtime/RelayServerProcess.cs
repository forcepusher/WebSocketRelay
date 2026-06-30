using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace BananaParty.WebSocketRelay
{
    public class RelayServerProcess
    {
        private const string UnityPackageEntry = "com.bananaparty.websocketrelay/Runtime/Server/Source/index.ts";
        private const string StandaloneEntry = "Source/index.ts";

        public static string GetServerDirectory() =>
            Path.GetFullPath(Path.Combine(
                Application.dataPath,
                "..",
                "Packages",
                "com.bananaparty.websocketrelay",
                "Runtime",
                "Server"));

        public static Process Start(bool createNoWindow, bool verboseDebug, int? relayPort = null)
        {
            string serverDirectory = GetServerDirectory();
            RelayServerProcessTerminator.KillAll();

            string bunPath = GetBunPath(serverDirectory);
            if (!File.Exists(bunPath))
                throw new FileNotFoundException($"Bundled Bun runtime not found at: {bunPath}");

            (string workingDirectory, string entryScript) = GetLaunchPaths(serverDirectory);
            ProcessStartInfo startInfo = CreateBunStartInfo(bunPath, serverDirectory, workingDirectory, entryScript, createNoWindow, verboseDebug, relayPort);
            Process process = Process.Start(startInfo);

            process.EnableRaisingEvents = true;
            process.OutputDataReceived += (_, e) => ForwardLine(e.Data, isError: false);
            process.ErrorDataReceived += (_, e) => ForwardLine(e.Data, isError: true);
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            return process;
        }

        public static void Stop(Process process)
        {
            RelayServerProcessTerminator.KillAll();

            if (process == null || process.HasExited)
                return;

            process.Kill();
            process.WaitForExit(5000);
        }

        private static ProcessStartInfo CreateBunStartInfo(
            string bunPath,
            string serverDirectory,
            string workingDirectory,
            string entryScript,
            bool createNoWindow,
            bool verboseDebug,
            int? relayPort)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = bunPath,
                Arguments = $"--cwd \"{workingDirectory}\" {entryScript} {RelayServerProcessTerminator.ProcessMarker}",
                WorkingDirectory = serverDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = createNoWindow,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };

            startInfo.Environment["RELAY_DEBUG"] = verboseDebug ? "1" : "0";
            if (relayPort.HasValue)
                startInfo.Environment["RELAY_PORT"] = relayPort.Value.ToString();

            return startInfo;
        }

        private static (string WorkingDirectory, string EntryScript) GetLaunchPaths(string serverDirectory)
        {
            string unityPackageManifest = Path.GetFullPath(Path.Combine(serverDirectory, "..", "..", "package.json"));
            if (File.Exists(unityPackageManifest))
            {
                return (
                    Path.GetFullPath(Path.Combine(serverDirectory, "..", "..", "..")),
                    UnityPackageEntry);
            }

            return (serverDirectory, StandaloneEntry);
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
