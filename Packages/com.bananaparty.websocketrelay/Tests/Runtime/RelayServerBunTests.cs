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
            yield return "RelayServer > connection does not send messages on open";
            yield return "RelayServer > subscribe does not send confirmation";
            yield return "RelayServer > duplicate subscribe does not send a message";
            yield return "RelayServer > relays channel messages with client-provided sender guid";
            yield return "RelayServer > does not relay to clients on other channels";
            yield return "RelayServer > relays channel message even when sender is not subscribed to channel";
            yield return "RelayServer > unsubscribe does not send confirmation";
            yield return "RelayServer > does not echo channel messages back to the sender";
            yield return "RelayServer > answers ping with pong carrying the same payload";
            yield return "RelayServer > answers ping without payload";
            yield return "RelayServer > ignores oversized ping";
            yield return "RelayServer > ignores empty frames and keeps the connection open";
            yield return "RelayServer backpressure > disconnects a subscriber that stops reading instead of buffering for it";
            yield return "RelayServer backpressure > keeps a subscriber that reads everything";
            yield return "BrowserSocket.jslib > relays channel messages between sockets";
            yield return "BrowserSocket.jslib > receives pong for ping";
            yield return "BrowserSocket.jslib > keeps a payload queued when the buffer is too small";
            yield return "BrowserSocket.jslib > reports buffered amount of an open socket";
            yield return "BrowserSocket.jslib > is not closed while connecting or open";
            yield return "BrowserSocket.jslib > reports close code after disconnecting";
            yield return "BrowserSocket.jslib > reports abnormal closure when the server drops the handshake";
            yield return "BrowserSocket.jslib > reports abnormal closure for an invalid address without throwing";
            yield return "BrowserSocket.jslib > drops sends while connecting instead of throwing";
            yield return "BrowserSocket.jslib > dispose frees the socket and later calls are harmless";
            yield return "BrowserSocket.jslib > never reuses ids of disposed sockets";
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
