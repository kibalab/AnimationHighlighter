#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace K13A.AnimationEditor.PropertyHighlighter
{
    class SettingsWindow : EditorWindow
    {
        bool _debugLogsEnabled;
        Color _highlightFill;
        Color _highlightBorder;

        [MenuItem("K13A/Animation Highlighter/Settings")]
        static void Open()
        {
            var window = GetWindow<SettingsWindow>("Animation Highlighter");
            window.minSize = new Vector2(320f, 160f);
            window.LoadFromSettings();
            window.Show();
        }

        void OnEnable()
        {
            LoadFromSettings();
            Settings.Changed += Repaint;
        }

        void OnDisable()
        {
            Settings.Changed -= Repaint;
        }

        void OnGUI()
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Animation Timeline Highlighter", EditorStyles.boldLabel);
            EditorGUILayout.Space(4f);

            EditorGUI.BeginChangeCheck();
            _debugLogsEnabled = EditorGUILayout.ToggleLeft("Enable debug logs", _debugLogsEnabled);
            _highlightFill = EditorGUILayout.ColorField(new GUIContent("Highlight Fill"), _highlightFill, true, true, false);
            _highlightBorder = EditorGUILayout.ColorField(new GUIContent("Highlight Border"), _highlightBorder, true, true, false);
            if (EditorGUI.EndChangeCheck())
                ApplyToSettings();

            EditorGUILayout.Space(8f);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Reset Defaults"))
                {
                    Settings.ResetToDefaults();
                    LoadFromSettings();
                }
            }
        }

        void LoadFromSettings()
        {
            _debugLogsEnabled = Settings.DebugLogsEnabled;
            _highlightFill = Settings.HighlightFill;
            _highlightBorder = Settings.HighlightBorder;
        }

        void ApplyToSettings()
        {
            Settings.DebugLogsEnabled = _debugLogsEnabled;
            Settings.HighlightFill = _highlightFill;
            Settings.HighlightBorder = _highlightBorder;
        }
    }
}
#endif