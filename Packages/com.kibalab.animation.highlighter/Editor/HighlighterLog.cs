#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace K13A.AnimationEditor.PropertyHighlighter
{
    static class HighlighterLog
    {
        static readonly Dictionary<int, double> _nextLogTimeByWindowId = new Dictionary<int, double>();
        static double _nextGlobalLogTime;

        public static void Reset()
        {
            _nextGlobalLogTime = 0d;
            _nextLogTimeByWindowId.Clear();
        }

        public static void ForgetWindow(int id)
        {
            _nextLogTimeByWindowId.Remove(id);
        }

        public static void LogGlobal(bool enabled, string message, bool force = false)
        {
            if (!enabled)
                return;

            double now = EditorApplication.timeSinceStartup;
            if (!force)
            {
                if (now < _nextGlobalLogTime)
                    return;
                _nextGlobalLogTime = now + 2.0;
            }

            Debug.LogWarning($"[AnimationTimelineHighlighter] {message}");
        }

        public static void LogWindow(bool enabled, EditorWindow window, string message, bool force = false)
        {
            if (!enabled || window == null)
                return;

            int id = window.GetInstanceID();
            double now = EditorApplication.timeSinceStartup;
            if (!force)
            {
                if (_nextLogTimeByWindowId.TryGetValue(id, out var next) && now < next)
                    return;
                _nextLogTimeByWindowId[id] = now + 1.0;
            }

            Debug.Log($"[AnimationTimelineHighlighter:{id}] {message}", window);
        }
    }
}
#endif