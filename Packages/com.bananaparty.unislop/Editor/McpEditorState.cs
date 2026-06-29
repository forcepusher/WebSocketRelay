using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace UniSlop.MCP
{
    // Single shared TestRunnerApi for the editor session, plus a session-long listener that
    // tracks whether a run is active and funnels results to McpTestJob. The listener is registered
    // fresh on every domain load (InitializeOnLoad), which is the only way a Play Mode run — whose
    // domain reloads mid-run — still delivers RunFinished. A transient per-run callback would be
    // dropped by that reload and the result silently lost.
    [InitializeOnLoad]
    static class McpTestRunState
    {
        // SessionState survives domain reloads, so an in-progress run started before a Play Mode
        // reload is still recognized as active afterwards.
        const string ActiveKey = "unislop.tests.active";

        static TestRunnerApi _api;

        public static bool IsRunActive => SessionState.GetBool(ActiveKey, false);

        public static TestRunnerApi Api
        {
            get { return _api; }
        }

        public static void ClearActive() => SessionState.SetBool(ActiveKey, false);

        static McpTestRunState()
        {
            if (!McpEditorProcess.IsMainEditor) return;

            _api = ScriptableObject.CreateInstance<TestRunnerApi>();
            _api.RegisterCallbacks(new RunStateListener());
        }

        sealed class RunStateListener : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun) => SessionState.SetBool(ActiveKey, true);

            public void RunFinished(ITestResultAdaptor result)
            {
                SessionState.SetBool(ActiveKey, false);
                McpTestJob.RecordResult(result);
            }

            public void TestStarted(ITestAdaptor test) { }
            public void TestFinished(ITestResultAdaptor result) { }
        }
    }
}
