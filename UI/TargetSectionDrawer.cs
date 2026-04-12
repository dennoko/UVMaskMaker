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
        public event System.Action OnSetupWorkCopyClicked;
        public event System.Action OnCleanupWorkCopyClicked;
        public event System.Action<int> OnTargetSubmeshChanged;
        public event System.Action<int> OnUVChannelChanged;

        /// <summary>
        /// Draws the target section.
        /// </summary>
        public void Draw(GameObject currentTarget, Renderer targetRenderer, MaskSettings settings, bool isWorkCopy)
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

            // Options for SkinnedMeshRenderer or Work Copy
            if (targetRenderer is SkinnedMeshRenderer || isWorkCopy)
            {
                if (!isWorkCopy && targetRenderer is SkinnedMeshRenderer)
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

                    EditorGUILayout.Space(2);
                    if (EditorUIStyles.DrawSecondaryButton(
                        _localization.Get("setup_work_copy", "Setup Work Copy"), 
                        _localization.Get("setup_work_copy_tooltip", "Create a temporary static mesh copy to work on, avoiding deformation issues.")))
                    {
                        OnSetupWorkCopyClicked?.Invoke();
                    }
                }
                else if (isWorkCopy)
                {
                    EditorGUILayout.Space(4);
                    EditorGUILayout.HelpBox(_localization.Get("work_copy_active_msg", "Work Copy Active. Original mesh is safe."), MessageType.Info);
                    
                    GUI.backgroundColor = new Color(1f, 0.7f, 0.7f); // Red tint for cleanup/destructive action
                    if (EditorUIStyles.DrawSecondaryButton(
                        _localization.Get("cleanup_work_copy", "Cleanup Work Copy"), 
                        _localization.Get("cleanup_work_copy_tooltip", "Delete work copy and revert to original target.")))
                    {
                        OnCleanupWorkCopyClicked?.Invoke();
                    }
                    GUI.backgroundColor = Color.white;
                }
            }

            if (targetRenderer != null)
            {
                EditorGUILayout.Space(2);
                EditorUIStyles.DrawSeparator();
                EditorGUILayout.Space(2);

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(
                        new GUIContent(_localization.Get("target_submesh", "対象マテリアル"), _localization.Get("target_submesh_tooltip", "Target material/submesh to extract. All submeshes by default.")),
                        GUILayout.Width(120));

                    var materials = targetRenderer.sharedMaterials;
                    string[] submeshNames;
                    if (materials == null || materials.Length == 0)
                    {
                        submeshNames = new[] { _localization.Get("submesh_all", "All Submeshes") };
                    }
                    else
                    {
                        submeshNames = new string[materials.Length + 1];
                        submeshNames[0] = _localization.Get("submesh_all", "All Submeshes");
                        for (int i = 0; i < materials.Length; i++)
                        {
                            string matName = materials[i] != null ? materials[i].name : "None";
                            submeshNames[i + 1] = $"[{i}] {matName}";
                        }
                    }

                    int currentIndex = settings.TargetSubmesh + 1;
                    if (currentIndex < 0 || currentIndex >= submeshNames.Length) currentIndex = 0;

                    EditorGUI.BeginChangeCheck();
                    int newIndex = EditorGUILayout.Popup(currentIndex, submeshNames);
                    if (EditorGUI.EndChangeCheck() || newIndex != currentIndex)
                    {
                        if (OnTargetSubmeshChanged != null) OnTargetSubmeshChanged.Invoke(newIndex - 1);
                    }
                }

                EditorGUILayout.Space(2);
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(
                        new GUIContent(_localization.Get("uv_channel", "UV Channel"), _localization.Get("uv_channel_tooltip", "UV channel for analysis/preview (UV0..UV7).")),
                        GUILayout.Width(120));

                    int newUv = EditorGUILayout.Popup(settings.UVChannel,
                        new[] { "UV0", "UV1", "UV2", "UV3", "UV4", "UV5", "UV6", "UV7" },
                        GUILayout.Width(80));

                    if (newUv != settings.UVChannel)
                    {
                        OnUVChannelChanged?.Invoke(newUv);
                    }
                    GUILayout.FlexibleSpace();
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
