// SelectionSectionDrawer.cs - Draws the selection mode and action buttons
using UnityEditor;
using UnityEngine;
using Dennoko.UVTools.Data;
using Dennoko.UVTools.Services;

namespace Dennoko.UVTools.UI
{
    /// <summary>
    /// Draws the selection section with mode toggle and action buttons.
    /// </summary>
    public class SelectionSectionDrawer
    {
        private readonly LocalizationService _localization;

        public SelectionSectionDrawer(LocalizationService localization)
        {
            _localization = localization;
        }

        // Events
        public event System.Action OnAnalyzeClicked;
        public event System.Action OnInvertClicked;
        public event System.Action OnSelectAllClicked;
        public event System.Action OnClearClicked;
        public event System.Action<bool> OnModeChanged;
        public event System.Action<KeyCode> OnHotkeyChanged;

        /// <summary>
        /// Draws the selection section.
        /// </summary>
        public void Draw(MaskSettings settings, bool hasAnalysis)
        {
            EditorUIStyles.BeginCard(_localization["mode_label"]);

            // Mode toggle (Add/Remove)
            using (new EditorGUILayout.HorizontalScope())
            {
                int toolbar = GUILayout.Toolbar(settings.AddMode ? 0 : 1, new[] {
                    new GUIContent(_localization["mode_add"], _localization["mode_add_tooltip"]),
                    new GUIContent(_localization["mode_remove"], _localization["mode_remove_tooltip"])
                });
                bool newMode = toolbar == 0;
                if (newMode != settings.AddMode)
                {
                    OnModeChanged?.Invoke(newMode);
                }

                GUILayout.FlexibleSpace();

                // Hotkey field (compact)
                EditorGUILayout.LabelField(
                    new GUIContent(_localization["toggle_hotkey_label"], _localization["toggle_hotkey_tooltip"]),
                    GUILayout.Width(80));

                var toggleStr = EditorGUILayout.TextField(settings.ModeToggleHotkey.ToString(), GUILayout.Width(50));
                if (System.Enum.TryParse<KeyCode>(toggleStr, out var toggleParsed) && toggleParsed != settings.ModeToggleHotkey)
                {
                    OnHotkeyChanged?.Invoke(toggleParsed);
                }
            }

            EditorGUILayout.Space(6);

            // Action buttons
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(
                    new GUIContent(_localization["analyze_uvs"], _localization["analyze_uvs_tooltip"]),
                    EditorUIStyles.SmallButtonStyle))
                {
                    OnAnalyzeClicked?.Invoke();
                }

                GUI.enabled = hasAnalysis;
                if (GUILayout.Button(
                    new GUIContent(_localization["invert"], _localization["invert_tooltip"]),
                    EditorUIStyles.SmallButtonStyle))
                {
                    OnInvertClicked?.Invoke();
                }
                if (GUILayout.Button(
                    new GUIContent(_localization["select_all"], _localization["select_all_tooltip"]),
                    EditorUIStyles.SmallButtonStyle))
                {
                    OnSelectAllClicked?.Invoke();
                }
                if (GUILayout.Button(
                    new GUIContent(_localization["clear_selection"], _localization["clear_selection_tooltip"]),
                    EditorUIStyles.SmallButtonStyle))
                {
                    OnClearClicked?.Invoke();
                }
                GUI.enabled = true;
            }

            EditorUIStyles.EndCard();
        }
    }
}
