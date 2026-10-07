#if !UNITY_WEBGL || UNITY_EDITOR
using System.IO;
using BananaParty.WebSocketRelay.Transport;
using NUnit.Framework;

namespace BananaParty.WebSocketRelay.Tests
{
    public class RelayServerProcessTests
    {
        [Test]
        public void ServerDirectory_ContainsServerAndBundledRuntime()
        {
            string serverDirectory = RelayServerProcess.GetServerDirectory();

            FileAssert.Exists(Path.Combine(serverDirectory, "Source", "index.ts"));
            FileAssert.Exists(RelayServerProcess.GetBunPath());
        }
    }
}
#endif
