// TargetSectionDrawer.cs - Draws the target object selection section
using UnityEditor;
using UnityEngine;
using Dennoko.UVTools.Data;
using Dennoko.UVTools.Services;

namespace Dennoko.UVTools.UI
{
    /// <summary>
    /// Draws the target model selection section of the UV Mask Maker window.
    /// Handles object field, drag-and-drop, and baked mesh option.
    /// </summary>
    public class TargetSectionDrawer
    {
        private readonly LocalizationService _localization;

        public TargetSectionDrawer(LocalizationService localization)
        {
            _localization = localization;
        }

        /// <summary>
        /// Event fired when target object is changed.
        /// </summary>
        public event System.Action<GameObject> OnTargetChanged;

        /// <summary>
        /// Event fired when baked mesh option is changed.
        /// </summary>
        public event System.Action<bool> OnBakedMeshChanged;

        /// <summary>
        /// Draws the target section.
        /// </summary>
        public void Draw(GameObject currentTarget, Renderer targetRenderer, MaskSettings settings)
        {
            EditorUIStyles.BeginCard(_localization["target_model"]);

            // Drag-and-drop area
            var dropRect = GUILayoutUtility.GetRect(0, 36, GUILayout.ExpandWidth(true));
            GUI.Box(dropRect, _localization["drag_drop_hint"], EditorStyles.helpBox);
            HandleDragAndDrop(dropRect);

            EditorGUILayout.Space(4);

            // Object field + Clear button
            using (new EditorGUILayout.HorizontalScope())
            {
                var newObj = EditorGUILayout.ObjectField(
                    new GUIContent("Object", _localization["object_field_tooltip"]),
                    currentTarget, typeof(GameObject), true) as GameObject;

                if (newObj != currentTarget)
                {
                    OnTargetChanged?.Invoke(newObj);
                }

                GUI.enabled = currentTarget != null;
                if (GUILayout.Button(
                    new GUIContent(_localization["clear_button"], _localization["clear_button_tooltip"]),
                    EditorUIStyles.SmallButtonStyle, GUILayout.Width(60)))
                {
                    OnTargetChanged?.Invoke(null);
                }
                GUI.enabled = true;
            }

            // Baked mesh option (only for SkinnedMeshRenderer)
            if (targetRenderer is SkinnedMeshRenderer)
            {
                EditorGUILayout.Space(2);
                bool useBaked = EditorUIStyles.DrawToggle(
                    settings.UseBakedMesh,
                    _localization["use_baked_mesh"],
                    _localization["use_baked_mesh_tooltip"]);

                if (useBaked != settings.UseBakedMesh)
                {
                    OnBakedMeshChanged?.Invoke(useBaked);
                }
            }

            EditorUIStyles.EndCard();
        }

        private void HandleDragAndDrop(Rect dropRect)
        {
            var evt = Event.current;
            if (!dropRect.Contains(evt.mousePosition)) return;

            if (evt.type == EventType.DragUpdated || evt.type == EventType.DragPerform)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                if (evt.type == EventType.DragPerform)
                {
                    DragAndDrop.AcceptDrag();
                    foreach (var obj in DragAndDrop.objectReferences)
                    {
                        if (obj is GameObject go)
                        {
                            OnTargetChanged?.Invoke(go);
                            break;
                        }
                    }
                }
                evt.Use();
            }
        }
    }
}
