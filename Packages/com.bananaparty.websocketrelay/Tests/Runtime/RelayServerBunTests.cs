#if !UNITY_WEBGL || UNITY_EDITOR
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BananaParty.WebSocketRelay.Tests
{
    [TestFixture]
    [Category("Bun")]
    public class RelayServerBunTests
    {
        private static BunTestRunReport _report;

        [OneTimeSetUp]
        public void RunBunTestSuite()
        {
            _report = BunTestRunner.RunRelayServerTests();

            if (!string.IsNullOrEmpty(_report.LaunchError))
                Assert.Fail(_report.LaunchError);

            if (!string.IsNullOrEmpty(_report.StandardOutput))
                Debug.Log(_report.StandardOutput);

            if (!string.IsNullOrEmpty(_report.StandardError))
                Debug.LogWarning(_report.StandardError);

            Assert.AreNotEqual(-1, _report.ExitCode, BuildProcessFailureMessage(_report));
            Assert.IsNotEmpty(_report.Cases, "Bun JUnit report contained no test cases.");
        }

        private static IEnumerable<string> BunTestCaseNames()
        {
            yield return "RelayServer > join sends JOINED_ROOM confirmation";
            yield return "RelayServer > duplicate join does not send another confirmation";
            yield return "RelayServer > relays room messages to other members";
            yield return "RelayServer > does not relay to clients in other rooms";
            yield return "RelayServer > rejects send from client that left the room";
            yield return "RelayServer > leave sends LEFT_ROOM only when client was in the room";
        }

        [TestCaseSource(nameof(BunTestCaseNames))]
        public void RelayServer_Bun(string testName)
        {
            BunTestCaseResult result = _report.FindByName(testName);
            Assert.IsNotNull(result, $"Bun test '{testName}' was not found in the JUnit report.");

            Assert.IsTrue(
                result.Passed,
                string.IsNullOrEmpty(result.FailureMessage)
                    ? $"Bun test failed: {testName}"
                    : $"Bun test failed: {testName}\n{result.FailureMessage}");
        }

        [Test]
        public void RelayServer_BunTestSuite_ExitCodeIsZero()
        {
            Assert.AreEqual(0, _report.ExitCode, BuildProcessFailureMessage(_report));
        }

        private static string BuildProcessFailureMessage(BunTestRunReport report)
        {
            return $"Bun test process failed with exit code {report.ExitCode}.\n{report.StandardError}\n{report.StandardOutput}";
        }
    }
}
#endif
