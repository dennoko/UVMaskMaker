// SelectionSectionDrawer.cs - Draws the selection mode toggle (Add/Remove)
using UnityEditor;
using UnityEngine;
using Dennoko.UVTools.Data;
using Dennoko.UVTools.Services;

namespace Dennoko.UVTools.UI
{
    /// <summary>
    /// Draws the edit mode section with Add/Remove toggle.
    /// Simplified to focus only on mode selection.
    /// </summary>
    public class SelectionSectionDrawer
    {
        private readonly LocalizationService _localization;

        public SelectionSectionDrawer(LocalizationService localization)
        {
            _localization = localization;
        }

        // Events
        public event System.Action<bool> OnModeChanged;

        /// <summary>
        /// Draws the edit mode section.
        /// </summary>
        public void Draw(MaskSettings settings)
        {
            EditorUIStyles.BeginCard(_localization.Get("edit_mode", "編集モード"));

            // Mode toggle (Add/Remove) - centered toolbar
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                
                int toolbar = GUILayout.Toolbar(settings.AddMode ? 0 : 1, new[] {
                    new GUIContent(_localization["mode_add"], _localization["mode_add_tooltip"]),
                    new GUIContent(_localization["mode_remove"], _localization["mode_remove_tooltip"])
                }, GUILayout.Width(200));
                
                bool newMode = toolbar == 0;
                if (newMode != settings.AddMode)
                {
                    OnModeChanged?.Invoke(newMode);
                }

                GUILayout.FlexibleSpace();
            }

            EditorUIStyles.EndCard();
        }
    }
}

