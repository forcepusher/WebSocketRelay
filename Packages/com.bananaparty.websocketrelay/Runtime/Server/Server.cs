using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

namespace BananaParty.WebSocketRelay
{
    public class Server
    {
        private Process _process;
        private readonly string _basePath;

        public Server(string basePath)
        {
            if (string.IsNullOrEmpty(basePath)) throw new ArgumentException("Base path cannot be null or empty", nameof(basePath));
            _basePath = basePath;
        }

        public void Start()
        {
            if (_process != null && !_process.HasExited) return;

            string scriptName = GetScriptName();
            string fullPath = Path.Combine(_basePath, scriptName);

            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException($"Server launch script not found at: {fullPath}");
            }

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = fullPath,
                UseShellExecute = true,
                CreateNoWindow = false
            };

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                startInfo.FileName = "/bin/bash";
                startInfo.Arguments = $"\"{fullPath}\"";
                startInfo.UseShellExecute = false;
            }

            _process = Process.Start(startInfo);
        }

        public void Stop()
        {
            if (_process == null) return;

            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill();
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Failed to stop server process: {e.Message}");
            }
            finally
            {
                _process = null;
            }
        }

        private string GetScriptName()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return "LaunchServer-Windows.bat";
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return "LaunchServer-Linux.sh";
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return "LaunchServer-MacOS.sh";
            throw new PlatformNotSupportedException("Unsupported operating system");
        }
    }
}
