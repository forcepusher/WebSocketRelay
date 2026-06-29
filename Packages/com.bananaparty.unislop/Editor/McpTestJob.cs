using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;

namespace UniSlop.MCP
{
    // Runs Unity tests as a non-blocking job. run_tests_start kicks it off and returns immediately;
    // the MCP server polls run_tests_status. State, results AND the pending request are mirrored
    // into SessionState so they survive the domain reload a Play Mode run triggers mid-run.
    //
    // Results are recorded by the persistent listener in McpTestRunState (RecordResult), not by a
    // per-run callback — a transient callback would be lost across the Play Mode reload, leaving the
    // run stuck "running" forever (which also blocks every later run).
    [InitializeOnLoad]
    static class McpTestJob
    {
        const string StateKey = "unislop.tests.state";
        const string DataKey = "unislop.tests.data";
        const string MessageKey = "unislop.tests.message";
        const string PendingModeKey = "unislop.tests.pendingMode";
        const string PendingFilterKey = "unislop.tests.pendingFilter";
        const string StartTimeKey = "unislop.tests.startTime";

        const string StateIdle = "idle";
        public const string StateRunning = "running";
        public const string StateDone = "done";

        // A run with no RunFinished after this long (editor seconds) is treated as dead so a new run
        // can start. Comfortably longer than the MCP server's per-job budget.
        const double StaleRunSeconds = 360.0;

        static volatile bool _pending;
        static Filter[] _pendingFilters;

        static readonly object CacheLock = new object();
        static string _state;
        static string _data;
        static string _message;

        static McpTestJob()
        {
            if (!McpEditorProcess.IsMainEditor) return;

            _state = SessionState.GetString(StateKey, StateIdle);
            _data = SessionState.GetString(DataKey, "");
            _message = SessionState.GetString(MessageKey, "");

            // Re-arm a request that was queued just before a domain reload (e.g. Play Mode tests:
            // RequestStart persisted the intent, the reload wiped the in-memory _pending flag).
            string pendingMode = SessionState.GetString(PendingModeKey, "");
            if (!string.IsNullOrEmpty(pendingMode))
            {
                string pendingFilter = SessionState.GetString(PendingFilterKey, "");
                if (TryBuildFilters(pendingMode, pendingFilter, out Filter[] filters, out _))
                {
                    _pendingFilters = filters;
                    _pending = true;
                }
                else
                {
                    ClearPending();
                }
            }
            else if (_state == StateRunning && !McpTestRunState.IsRunActive)
            {
                // "running" with no active run and nothing queued is a dead run left by an earlier
                // reload — reset so status reads honestly and new runs aren't blocked.
                Persist(StateIdle, "", "");
            }

            EditorApplication.update += Tick;
        }

        // Thread-safe reads for the MCP poller (must not touch Unity API off the main thread).
        public static string State { get { lock (CacheLock) return _state; } }
        public static bool IsActive => State == StateRunning;
        public static string Message { get { lock (CacheLock) return _message; } }

        // Call on the main thread.
        // mode: "all" (default) runs Edit Mode + Play Mode tests, "editmode" / "playmode" run one.
        public static bool RequestStart(string mode, string filter, out string error)
        {
            error = null;

            if (!TryBuildFilters(mode, filter, out Filter[] filters, out error))
                return false;

            foreach (Filter f in filters)
            {
                if (f.testMode == TestMode.EditMode && EditorApplication.isPlaying)
                {
                    error = "Cannot run Edit Mode tests while Play Mode is active";
                    return false;
                }
            }

            if (IsRunInProgress())
            {
                error = "A test run is already in progress";
                return false;
            }

            McpTestRunState.ClearActive();
            Persist(StateRunning, "", "");

            SessionState.SetString(PendingModeKey, string.IsNullOrEmpty(mode) ? "all" : mode);
            SessionState.SetString(PendingFilterKey, filter ?? "");
            SessionState.SetFloat(StartTimeKey, (float)EditorApplication.timeSinceStartup);

            _pendingFilters = filters;
            _pending = true;

            McpEditorPump.NotifyWork();
            return true;
        }

        // A run counts as in progress only while it is genuinely active and not stale. A hung run
        // (no RunFinished, e.g. an editor crash mid-run) is allowed to be superseded.
        static bool IsRunInProgress()
        {
            if (_pending)
                return true;
            if (!McpTestRunState.IsRunActive)
                return false;

            float start = SessionState.GetFloat(StartTimeKey, 0f);
            double elapsed = EditorApplication.timeSinceStartup - start;
            return elapsed >= 0 && elapsed < StaleRunSeconds;
        }

        // Merges the persisted result data with the current state for run_tests_status.
        public static string BuildStatusData()
        {
            string state, data;
            lock (CacheLock) { state = _state; data = _data; }

            if (state != StateDone)
                return "{\"state\":\"" + state + "\"}";

            // Done with no payload means the run never produced a result (aborted). Report it as
            // such instead of fabricating a zero-failure (which reads as "all passed").
            if (string.IsNullOrEmpty(data) || data[0] != '{')
                return "{\"state\":\"done\",\"passed\":0,\"failed\":0,\"total\":0,\"aborted\":true}";

            return "{\"state\":\"done\"," + data.Substring(1);
        }

        static void Tick()
        {
            if (!_pending)
                return;

            if (EditorApplication.isCompiling || EditorApplication.isUpdating || McpTestRunState.IsRunActive)
                return;

            Filter[] filters = _pendingFilters;
            ClearPending();
            StartRun(filters);
        }

        static void StartRun(Filter[] filters)
        {
            try
            {
                // The persistent McpTestRunState listener records RunFinished; do not register a
                // transient callback here (it would not survive a Play Mode domain reload).
                McpTestRunState.Api.Execute(new ExecutionSettings(filters));
            }
            catch (Exception e)
            {
                McpTestRunState.ClearActive();
                Finish("Failed to start tests: " + e.Message, null);
            }
        }

        static void ClearPending()
        {
            _pending = false;
            _pendingFilters = null;
            SessionState.EraseString(PendingModeKey);
            SessionState.EraseString(PendingFilterKey);
        }

        // Called by McpTestRunState's persistent listener when a run finishes (main thread).
        public static void RecordResult(ITestResultAdaptor result)
        {
            if (result == null)
            {
                Finish("Tests aborted without a result (likely a domain reload during the run)", null);
                return;
            }

            int passed = result.PassCount;
            int failed = result.FailCount;
            int skipped = result.SkipCount;
            int total = passed + failed + skipped;

            var sb = new StringBuilder();
            sb.Append("{\"passed\":").Append(passed);
            sb.Append(",\"failed\":").Append(failed);
            sb.Append(",\"skipped\":").Append(skipped);
            sb.Append(",\"total\":").Append(total);
            sb.Append(",\"durationMs\":").Append((long)(result.Duration * 1000));

            var failures = new List<string>();
            CollectFailures(result, failures);
            if (failures.Count > 0)
            {
                sb.Append(",\"failures\":[");
                sb.Append(string.Join(",", failures));
                sb.Append(']');
            }

            sb.Append('}');

            string message;
            if (total == 0)
                message = "No tests matched the requested mode/filter";
            else if (result.TestStatus == TestStatus.Passed)
                message = $"Tests passed ({passed}/{total})";
            else
                message = $"Tests failed ({failed} failure(s), {passed}/{total} passed)";

            Finish(message, sb.ToString());
        }

        static void Finish(string message, string dataJson)
        {
            Persist(StateDone, dataJson ?? "", message);
        }

        // Writes both durable SessionState (survives reload) and the thread-safe cache (read by
        // the background poller). Always called on the main thread.
        static void Persist(string state, string data, string message)
        {
            SessionState.SetString(StateKey, state);
            SessionState.SetString(DataKey, data);
            SessionState.SetString(MessageKey, message);
            lock (CacheLock)
            {
                _state = state;
                _data = data;
                _message = message;
            }
        }

        static bool TryBuildFilters(string mode, string filter, out Filter[] filters, out string error)
        {
            error = null;
            filters = null;

            string m = string.IsNullOrEmpty(mode) ? "all" : mode.ToLowerInvariant();
            bool runEdit = m == "all" || m == "editmode";
            bool runPlay = m == "all" || m == "playmode";

            if (!runEdit && !runPlay)
            {
                error = $"Unknown test mode '{mode}' (expected 'all', 'editmode' or 'playmode')";
                return false;
            }

            var list = new List<Filter>();
            if (runEdit) list.Add(BuildTestFilter(TestMode.EditMode, filter));
            if (runPlay) list.Add(BuildTestFilter(TestMode.PlayMode, filter));
            filters = list.ToArray();
            return true;
        }

        static Filter BuildTestFilter(TestMode testMode, string filter)
        {
            var settings = new Filter { testMode = testMode };
            if (string.IsNullOrWhiteSpace(filter))
                return settings;

            string trimmed = filter.Trim();
            if (trimmed.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase)
                || trimmed.EndsWith(".Tests.dll", StringComparison.OrdinalIgnoreCase))
            {
                settings.assemblyNames = new[]
                {
                    trimmed.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? trimmed : trimmed + ".dll"
                };
                return settings;
            }

            settings.testNames = new[] { trimmed };
            return settings;
        }

        static void CollectFailures(ITestResultAdaptor result, List<string> failures)
        {
            if (result.TestStatus == TestStatus.Failed && !result.HasChildren)
            {
                failures.Add("{\"name\":" + McpUnityBridge.JsonStr(result.FullName)
                    + ",\"message\":" + McpUnityBridge.JsonStr(result.Message)
                    + ",\"stackTrace\":" + McpUnityBridge.JsonStr(Truncate(result.StackTrace, 2000)) + "}");
            }

            if (!result.HasChildren) return;
            foreach (var child in result.Children)
                CollectFailures(child, failures);
        }

        static string Truncate(string value, int max)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= max) return value;
            return value.Substring(0, max) + "...";
        }
    }
}
