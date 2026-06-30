using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace BananaParty.WebSocketRelay
{
    public class RelayServerProcessTerminator
    {
        public const string ProcessMarker = "-relay-server";

        public static void KillAll()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                KillAllWindows();
            else
                KillAllUnix();
        }

        private static void KillAllWindows()
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = "wmic",
                Arguments = $"process where \"name='bun.exe' and CommandLine like '%{ProcessMarker}%'\" call terminate",
                CreateNoWindow = true,
                UseShellExecute = false,
            };

            using Process process = Process.Start(startInfo);
            process?.WaitForExit(5000);
        }

        private static void KillAllUnix()
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = "pkill",
                Arguments = $"-f Source/index.ts {ProcessMarker}",
                CreateNoWindow = true,
                UseShellExecute = false,
            };

            using Process process = Process.Start(startInfo);
            process?.WaitForExit(5000);
        }
    }
}
