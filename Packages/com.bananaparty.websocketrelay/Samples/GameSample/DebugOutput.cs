using System.Collections.Generic;
using UnityEngine;

namespace BananaParty.WebSocketRelay.Samples
{
    /// <summary>
    /// Shows the latest log messages on screen, toggled with the back quote key.
    /// </summary>
    public class DebugOutput : MonoBehaviour
    {
        [SerializeField]
        private int _maxLogs = 100;

        private readonly List<string> _logs = new();
        private readonly object _logsLock = new();

        private Vector2 _scrollPosition;
        private bool _isVisible;

        private void OnEnable()
        {
            Application.logMessageReceivedThreaded += HandleLog;
        }

        private void OnDisable()
        {
            Application.logMessageReceivedThreaded -= HandleLog;
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.BackQuote))
                _isVisible = !_isVisible;
        }

        private void OnGUI()
        {
            if (!_isVisible)
                return;

            GUILayout.BeginArea(new Rect(0, 0, Screen.width, Screen.height));
            GUILayout.BeginVertical("box");
            _scrollPosition = GUILayout.BeginScrollView(_scrollPosition);

            lock (_logsLock)
            {
                foreach (string log in _logs)
                    GUILayout.Label(log);
            }

            GUILayout.EndScrollView();
            GUILayout.EndVertical();
            GUILayout.EndArea();
        }

        // Called from any thread, because relay server output is logged from background threads.
        private void HandleLog(string condition, string stackTrace, LogType type)
        {
            lock (_logsLock)
            {
                _logs.Add($"[{type}] {condition}");
                if (_logs.Count > _maxLogs)
                    _logs.RemoveAt(0);
            }
        }
    }
}
