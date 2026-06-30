using System;
using System.Diagnostics;
using UnityEngine;

namespace BananaParty.WebSocketRelay
{
    public class RelayServer
    {
        private Process _process;

        public void Start(bool verboseDebug = true)
        {
            if (_process != null && !_process.HasExited)
                return;

            _process = RelayServerProcess.Start(createNoWindow: false, verboseDebug: verboseDebug);
        }

        public void Stop()
        {
            if (_process == null)
                return;

            try
            {
                RelayServerProcess.Stop(_process);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning($"Failed to stop server process: {e.Message}");
            }
            finally
            {
                _process.Dispose();
                _process = null;
            }
        }
    }
}
