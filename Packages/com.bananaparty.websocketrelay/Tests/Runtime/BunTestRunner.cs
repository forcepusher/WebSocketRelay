using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Xml;
using BananaParty.WebSocketRelay.Transport;

namespace BananaParty.WebSocketRelay.Tests
{
    public static class BunTestRunner
    {
        private const int RunTimeoutMs = 60_000;
        private const int MaxAttempts = 3;
        private const string RelayServerTestFile = "Source/RelayServer.test.ts";
        private const string TestDirectoryFilter = "Source/";

        public static BunTestRunReport RunRelayServerTests()
        {
            // The bundled Bun crashes now and then on some Windows machines, which leaves no report behind.
            BunTestRunReport report = RunOnce();
            for (int attempt = 1; attempt < MaxAttempts && report.Cases.Count == 0 && string.IsNullOrEmpty(report.LaunchError); attempt++)
                report = RunOnce();

            return report;
        }

        private static BunTestRunReport RunOnce()
        {
            string serverDirectory = RelayServerProcess.GetServerDirectory();
            string bunExecutablePath = RelayServerProcess.GetBunPath();

            if (!File.Exists(bunExecutablePath))
            {
                return new BunTestRunReport(
                    Array.Empty<BunTestCaseResult>(),
                    -1,
                    string.Empty,
                    string.Empty,
                    $"Bun executable not found at: {bunExecutablePath}");
            }

            string testFilePath = Path.Combine(serverDirectory, RelayServerTestFile.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(testFilePath))
            {
                return new BunTestRunReport(
                    Array.Empty<BunTestCaseResult>(),
                    -1,
                    string.Empty,
                    string.Empty,
                    $"Bun test file not found at: {testFilePath}");
            }

            string junitReportPath = Path.Combine(Path.GetTempPath(), $"websocketrelay-bun-{Guid.NewGuid():N}.xml");

            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = bunExecutablePath,
                    Arguments = $"test {TestDirectoryFilter} --reporter=junit --reporter-outfile=\"{junitReportPath}\"",
                    WorkingDirectory = serverDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                };

                // Bun's crash reporter would keep the test ports open after a crash, see RelayServerProcess.
                startInfo.Environment["BUN_ENABLE_CRASH_REPORTING"] = "0";

                using Process process = Process.Start(startInfo);
                if (process == null)
                {
                    return new BunTestRunReport(
                        Array.Empty<BunTestCaseResult>(),
                        -1,
                        string.Empty,
                        string.Empty,
                        "Failed to start bun test process.");
                }

                // Read stdout/stderr concurrently. Sequential ReadToEnd deadlocks when either
                // pipe fills, and WaitForExit never runs so the timeout cannot recover.
                StringBuilder standardOutput = new();
                StringBuilder standardError = new();
                process.OutputDataReceived += (_, args) =>
                {
                    if (args.Data != null)
                        standardOutput.AppendLine(args.Data);
                };
                process.ErrorDataReceived += (_, args) =>
                {
                    if (args.Data != null)
                        standardError.AppendLine(args.Data);
                };
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                if (!process.WaitForExit(RunTimeoutMs))
                {
                    process.Kill();
                    process.WaitForExit();
                    return new BunTestRunReport(
                        Array.Empty<BunTestCaseResult>(),
                        -1,
                        standardOutput.ToString(),
                        standardError.ToString(),
                        $"Bun test process timed out after {RunTimeoutMs}ms.");
                }

                // Second WaitForExit drains async output handlers after exit.
                process.WaitForExit();

                IReadOnlyList<BunTestCaseResult> cases = File.Exists(junitReportPath)
                    ? ParseJUnitReport(junitReportPath)
                    : Array.Empty<BunTestCaseResult>();

                return new BunTestRunReport(
                    cases,
                    process.ExitCode,
                    standardOutput.ToString(),
                    standardError.ToString(),
                    null);
            }
            finally
            {
                if (File.Exists(junitReportPath))
                    File.Delete(junitReportPath);
            }
        }

        private static IReadOnlyList<BunTestCaseResult> ParseJUnitReport(string junitReportPath)
        {
            List<BunTestCaseResult> cases = new();
            XmlDocument document = new();
            document.Load(junitReportPath);

            XmlNodeList testCaseNodes = document.SelectNodes("//testcase");
            if (testCaseNodes == null)
                return cases;

            foreach (XmlNode testCaseNode in testCaseNodes)
            {
                string name = testCaseNode.Attributes?["name"]?.Value ?? "unknown";
                string className = testCaseNode.Attributes?["classname"]?.Value;
                string displayName = string.IsNullOrEmpty(className) ? name : $"{className} > {name}";

                XmlNode failureNode = testCaseNode.SelectSingleNode("failure");
                bool passed = failureNode == null;
                string failureMessage = passed
                    ? string.Empty
                    : failureNode.Attributes?["message"]?.Value ?? failureNode.InnerText;

                cases.Add(new BunTestCaseResult(displayName, passed, failureMessage));
            }

            return cases;
        }
    }
}
