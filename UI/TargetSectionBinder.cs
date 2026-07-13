// TargetSectionBinder.cs - Binds the target object selection card (UI Toolkit)
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Dennoko.UVTools.Data;
using Dennoko.UVTools.Services;

namespace Dennoko.UVTools.UI
{
    /// <summary>
    /// Binds the target model selection card of the UV Mask Maker window.
    /// Handles the object field, drag-and-drop, baked mesh option, work copy
    /// buttons and the submesh/UV channel dropdowns.
    /// </summary>
    public class TargetSectionBinder
    {
        private readonly LocalizationService _localization;

        // UXML elements
        private Label _targetTitle;
        private Label _targetDropArea;
        private ObjectField _targetField;
        private Button _targetClearBtn;
        private VisualElement _skinnedOptions;
        private Toggle _bakedMeshToggle;
        private Button _setupWorkCopyBtn;
        private VisualElement _workcopyOptions;
        private HelpBox _workcopyInfo;
        private Button _cleanupWorkCopyBtn;
        private VisualElement _meshOptions;
        private DropdownField _submeshDropdown;
        private DropdownField _uvChannelDropdown;

        private static readonly List<string> UVChannelChoices = new List<string>
        {
            "UV0", "UV1", "UV2", "UV3", "UV4", "UV5", "UV6", "UV7"
        };

        public TargetSectionBinder(LocalizationService localization)
        {
            _localization = localization;
        }

        public event System.Action<GameObject> OnTargetChanged;
        public event System.Action<bool> OnBakedMeshChanged;
        public event System.Action OnSetupWorkCopyClicked;
        public event System.Action OnCleanupWorkCopyClicked;
        public event System.Action<int> OnTargetSubmeshChanged;
        public event System.Action<int> OnUVChannelChanged;

        public void Bind(VisualElement root)
        {
            _targetTitle       = root.Q<Label>("target-title");
            _targetDropArea    = root.Q<Label>("target-drop-area");
            _targetField       = root.Q<ObjectField>("target-field");
            _targetClearBtn    = root.Q<Button>("target-clear-btn");
            _skinnedOptions    = root.Q<VisualElement>("skinned-options");
            _bakedMeshToggle   = root.Q<Toggle>("baked-mesh-toggle");
            _setupWorkCopyBtn  = root.Q<Button>("setup-workcopy-btn");
            _workcopyOptions   = root.Q<VisualElement>("workcopy-options");
            _workcopyInfo      = root.Q<HelpBox>("workcopy-info");
            _cleanupWorkCopyBtn = root.Q<Button>("cleanup-workcopy-btn");
            _meshOptions       = root.Q<VisualElement>("mesh-options");
            _submeshDropdown   = root.Q<DropdownField>("submesh-dropdown");
            _uvChannelDropdown = root.Q<DropdownField>("uv-channel-dropdown");

            _uvChannelDropdown.choices = UVChannelChoices;

            _targetField.RegisterValueChangedCallback(evt =>
                OnTargetChanged?.Invoke(evt.newValue as GameObject));
            _targetClearBtn.clicked += () => OnTargetChanged?.Invoke(null);

            _bakedMeshToggle.RegisterValueChangedCallback(evt =>
                OnBakedMeshChanged?.Invoke(evt.newValue));
            _setupWorkCopyBtn.clicked += () => OnSetupWorkCopyClicked?.Invoke();
            _cleanupWorkCopyBtn.clicked += () => OnCleanupWorkCopyClicked?.Invoke();

            _submeshDropdown.RegisterValueChangedCallback(evt =>
            {
                int idx = _submeshDropdown.choices.IndexOf(evt.newValue);
                OnTargetSubmeshChanged?.Invoke(idx - 1);
            });

            _uvChannelDropdown.RegisterValueChangedCallback(evt =>
            {
                int idx = UVChannelChoices.IndexOf(evt.newValue);
                if (idx >= 0) OnUVChannelChanged?.Invoke(idx);
            });

            // Drag-and-drop of a GameObject onto the drop area.
            _targetDropArea.RegisterCallback<DragEnterEvent>(evt =>
                _targetDropArea.AddToClassList("maskmaker-drop-area--hover"));
            _targetDropArea.RegisterCallback<DragLeaveEvent>(evt =>
                _targetDropArea.RemoveFromClassList("maskmaker-drop-area--hover"));
            _targetDropArea.RegisterCallback<DragUpdatedEvent>(evt =>
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                evt.StopPropagation();
            });
            _targetDropArea.RegisterCallback<DragPerformEvent>(evt =>
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
                _targetDropArea.RemoveFromClassList("maskmaker-drop-area--hover");
                evt.StopPropagation();
            });
        }

        public void ApplyLocalization()
        {
            _targetTitle.text = _localization["target_model"];
            _targetDropArea.text = _localization["drag_drop_hint"];

            _targetField.tooltip = _localization["object_field_tooltip"];
            _targetClearBtn.text = _localization["clear_button"];
            _targetClearBtn.tooltip = _localization["clear_button_tooltip"];

            _bakedMeshToggle.label = _localization["use_baked_mesh"];
            _bakedMeshToggle.tooltip = _localization["use_baked_mesh_tooltip"];

            _setupWorkCopyBtn.text = _localization.Get("setup_work_copy", "Setup Work Copy");
            _setupWorkCopyBtn.tooltip = _localization.Get(
                "setup_work_copy_tooltip",
                "Create a temporary static mesh copy to work on, avoiding deformation issues.");

            _workcopyInfo.text = _localization.Get("work_copy_active_msg", "Work Copy Active. Original mesh is safe.");

            _cleanupWorkCopyBtn.text = _localization.Get("cleanup_work_copy", "Cleanup Work Copy");
            _cleanupWorkCopyBtn.tooltip = _localization.Get(
                "cleanup_work_copy_tooltip",
                "Delete work copy and revert to original target.");

            _submeshDropdown.label = _localization.Get("target_submesh", "対象マテリアル");
            _submeshDropdown.tooltip = _localization.Get(
                "target_submesh_tooltip",
                "Target material/submesh to extract. All submeshes by default.");

            _uvChannelDropdown.label = _localization.Get("uv_channel", "UV Channel");
            _uvChannelDropdown.tooltip = _localization.Get(
                "uv_channel_tooltip", "UV channel for analysis/preview (UV0..UV7).");
        }

        public void UpdateState(GameObject target, Renderer renderer, MaskSettings settings, bool isWorkCopy)
        {
            _targetField.SetValueWithoutNotify(target);
            _bakedMeshToggle.SetValueWithoutNotify(settings.UseBakedMesh);

            bool showSkinned = renderer is SkinnedMeshRenderer && !isWorkCopy;
            _skinnedOptions.style.display = showSkinned ? DisplayStyle.Flex : DisplayStyle.None;
            _workcopyOptions.style.display = isWorkCopy ? DisplayStyle.Flex : DisplayStyle.None;
            _meshOptions.style.display = renderer != null ? DisplayStyle.Flex : DisplayStyle.None;

            // Rebuild the submesh dropdown choices from the renderer's materials.
            var names = new List<string> { _localization.Get("submesh_all", "All Submeshes") };
            var materials = renderer != null ? renderer.sharedMaterials : null;
            if (materials != null)
            {
                for (int i = 0; i < materials.Length; i++)
                {
                    string matName = materials[i] != null ? materials[i].name : "None";
                    names.Add($"[{i}] {matName}");
                }
            }
            _submeshDropdown.choices = names;

            int currentIndex = settings.TargetSubmesh + 1;
            if (currentIndex < 0 || currentIndex >= names.Count) currentIndex = 0;
            _submeshDropdown.SetValueWithoutNotify(names[currentIndex]);

            _uvChannelDropdown.SetValueWithoutNotify($"UV{settings.UVChannel}");
        }
    }
}
