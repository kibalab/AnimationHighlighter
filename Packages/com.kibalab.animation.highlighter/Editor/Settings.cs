#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace K13A.AnimationEditor.PropertyHighlighter
{
    static class Settings
    {
        const string KeyPrefix = "K13A.AnimationTimelineHighlighter.";
        const string DebugLogsKey = KeyPrefix + "DebugLogs";
        const string FillColorKey = KeyPrefix + "FillColor";
        const string BorderColorKey = KeyPrefix + "BorderColor";
        const string SettingsVersionKey = KeyPrefix + "SettingsVersion";
        const int CurrentSettingsVersion = 2;

        static readonly Color DefaultFill = new Color(0f, 0f, 0f, 0f);
        static readonly Color DefaultBorder = new Color(0.24f, 0.49f, 0.90f, 1f);

        static bool _loaded;
        static bool _debugLogsEnabled;
        static Color _highlightFill;
        static Color _highlightBorder;

        public static event Action Changed;

        public static bool DebugLogsEnabled
        {
            get
            {
                EnsureLoaded();
                return _debugLogsEnabled;
            }
            set
            {
                EnsureLoaded();
                if (_debugLogsEnabled == value)
                    return;

                _debugLogsEnabled = value;
                EditorPrefs.SetBool(DebugLogsKey, value);
                Changed?.Invoke();
            }
        }

        public static Color HighlightFill
        {
            get
            {
                EnsureLoaded();
                return _highlightFill;
            }
            set
            {
                EnsureLoaded();
                if (Approximately(_highlightFill, value))
                    return;

                _highlightFill = value;
                SaveColor(FillColorKey, value);
                Changed?.Invoke();
            }
        }

        public static Color HighlightBorder
        {
            get
            {
                EnsureLoaded();
                return _highlightBorder;
            }
            set
            {
                EnsureLoaded();
                if (Approximately(_highlightBorder, value))
                    return;

                _highlightBorder = value;
                SaveColor(BorderColorKey, value);
                Changed?.Invoke();
            }
        }

        public static void ResetToDefaults()
        {
            _loaded = true;
            _debugLogsEnabled = false;
            _highlightFill = DefaultFill;
            _highlightBorder = DefaultBorder;

            EditorPrefs.SetBool(DebugLogsKey, _debugLogsEnabled);
            SaveColor(FillColorKey, _highlightFill);
            SaveColor(BorderColorKey, _highlightBorder);
            EditorPrefs.SetInt(SettingsVersionKey, CurrentSettingsVersion);
            Changed?.Invoke();
        }

        static void EnsureLoaded()
        {
            if (_loaded)
                return;

            _loaded = true;

            bool hasVersion = EditorPrefs.HasKey(SettingsVersionKey);
            int version = hasVersion ? EditorPrefs.GetInt(SettingsVersionKey, 0) : 0;
            if (!hasVersion || version < CurrentSettingsVersion)
            {
                _debugLogsEnabled = false;
                _highlightFill = DefaultFill;
                _highlightBorder = DefaultBorder;
                EditorPrefs.SetBool(DebugLogsKey, _debugLogsEnabled);
                SaveColor(FillColorKey, _highlightFill);
                SaveColor(BorderColorKey, _highlightBorder);
                EditorPrefs.SetInt(SettingsVersionKey, CurrentSettingsVersion);
                return;
            }

            _debugLogsEnabled = EditorPrefs.GetBool(DebugLogsKey, false);
            _highlightFill = LoadColor(FillColorKey, DefaultFill);
            _highlightBorder = LoadColor(BorderColorKey, DefaultBorder);
        }

        static Color LoadColor(string key, Color fallback)
        {
            string s = EditorPrefs.GetString(key, string.Empty);
            if (string.IsNullOrEmpty(s))
                return fallback;

            if (ColorUtility.TryParseHtmlString(s, out Color c))
                return c;

            return fallback;
        }

        static void SaveColor(string key, Color c)
        {
            EditorPrefs.SetString(key, "#" + ColorUtility.ToHtmlStringRGBA(c));
        }

        static bool Approximately(Color a, Color b)
        {
            return Mathf.Approximately(a.r, b.r) &&
                   Mathf.Approximately(a.g, b.g) &&
                   Mathf.Approximately(a.b, b.b) &&
                   Mathf.Approximately(a.a, b.a);
        }
    }
}
#endif