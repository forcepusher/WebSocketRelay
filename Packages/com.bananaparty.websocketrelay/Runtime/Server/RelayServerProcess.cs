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
            string scriptPath = GetScriptPath(serverDirectory);

            if (!File.Exists(scriptPath))
                throw new FileNotFoundException($"Server launch script not found at: {scriptPath}");

            ProcessStartInfo startInfo = CreateStartInfo(scriptPath, serverDirectory, createNoWindow, verboseDebug, relayPort);
            Process process = Process.Start(startInfo);

            process.EnableRaisingEvents = true;
            process.OutputDataReceived += (_, e) => ForwardLine(e.Data, isError: false);
            process.ErrorDataReceived += (_, e) => ForwardLine(e.Data, isError: true);
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            return process;
        }

        private static ProcessStartInfo CreateStartInfo(
            string scriptPath,
            string serverDirectory,
            bool createNoWindow,
            bool verboseDebug,
            int? relayPort)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
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

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                startInfo.FileName = "cmd.exe";
                startInfo.Arguments = $"/c \"{scriptPath}\"";
            }
            else
            {
                startInfo.FileName = "/bin/bash";
                startInfo.Arguments = $"\"{scriptPath}\"";
            }

            return startInfo;
        }

        private static string GetScriptPath(string serverDirectory)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return Path.Combine(serverDirectory, "LaunchServer-Windows.bat");
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return Path.Combine(serverDirectory, "LaunchServer-MacOS.sh");
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return Path.Combine(serverDirectory, "LaunchServer-Linux.sh");

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
