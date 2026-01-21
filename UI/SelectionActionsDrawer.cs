// SelectionActionsDrawer.cs - Draws selection action buttons (Analyze, Invert, Select All, Clear)
using UnityEditor;
using UnityEngine;
using Dennoko.UVTools.Data;
using Dennoko.UVTools.Services;

namespace Dennoko.UVTools.UI
{
    /// <summary>
    /// Draws the selection actions section with action buttons.
    /// Separated from mode toggle for clearer UI organization.
    /// </summary>
    public class SelectionActionsDrawer
    {
        private readonly LocalizationService _localization;

        public SelectionActionsDrawer(LocalizationService localization)
        {
            _localization = localization;
        }

        // Events
        public event System.Action OnAnalyzeClicked;
        public event System.Action OnInvertClicked;
        public event System.Action OnSelectAllClicked;
        public event System.Action OnClearClicked;

        /// <summary>
        /// Draws the selection actions section.
        /// </summary>
        public void Draw(bool hasAnalysis)
        {
            EditorUIStyles.BeginCard(_localization.Get("selection_actions", "選択操作"));

            // Action buttons in a single row
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
