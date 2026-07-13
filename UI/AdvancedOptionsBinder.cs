// AdvancedOptionsBinder.cs - Binds the collapsible advanced/optional settings
// (scene overlay, channel write, vertex color bake, preferences) (UI Toolkit).
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Dennoko.UVTools.Data;
using Dennoko.UVTools.Services;

namespace Dennoko.UVTools.UI
{
    /// <summary>
    /// Binds the collapsible advanced options foldouts.
    /// These are hidden by default to reduce UI noise.
    /// </summary>
    public class AdvancedOptionsBinder
    {
        private readonly LocalizationService _localization;
        private string _currentHotkeyStr = "R";

        // Scene overlay
        private Foldout _overlayFoldout;
        private Toggle _overlayOnTopToggle;
        private Toggle _disableAAToggle;
        private Toggle _backfaceCullToggle;
        private Slider _thicknessSlider;
        private Slider _depthOffsetSlider;
        private ColorField _selectedColorField;
        private ColorField _seamColorField;
        private ColorField _previewFillColorField;
        private Slider _overlayAlphaSlider;
        private Toggle _showIslandPreviewToggle;
        private Toggle _overlayBaseTexToggle;

        // Channel write
        private Foldout _channelWriteFoldout;
        private Toggle _channelWriteToggle;
        private VisualElement _channelWriteContent;
        private ObjectField _basePngField;
        private Label _writeChannelsLabel;
        private Toggle _writeRToggle;
        private Toggle _writeGToggle;
        private Toggle _writeBToggle;
        private Toggle _writeAToggle;

        // Vertex color bake
        private Foldout _vertexColorFoldout;
        private ObjectField _baseVcMeshField;
        private Toggle _overwriteVcToggle;
        private Button _bakeVcBtn;

        // Preferences
        private Foldout _preferencesFoldout;
        private Toggle _englishToggle;
        private TextField _hotkeyField;
        private Toggle _autoWorkCopyToggle;
        private Vector3Field _workcopyOffsetField;

        public AdvancedOptionsBinder(LocalizationService localization)
        {
            _localization = localization;
        }

        // Scene overlay events
        public event System.Action<bool> OnOverlayOnTopChanged;
        public event System.Action<bool> OnDisableAAChanged;
        public event System.Action<bool> OnBackfaceCullChanged;
        public event System.Action<float> OnThicknessChanged;
        public event System.Action<float> OnDepthOffsetChanged;
        public event System.Action<Color> OnSeamColorChanged;
        public event System.Action<Color> OnSelectedColorChanged;
        public event System.Action<Color> OnPreviewFillColorChanged;
        public event System.Action<float> OnOverlayAlphaChanged;
        public event System.Action<bool> OnShowIslandPreviewChanged;
        public event System.Action<bool> OnPreviewOverlayBaseChanged;

        // Channel write events
        public event System.Action<bool> OnChannelWriteEnabledChanged;
        public event System.Action<Texture2D> OnBasePNGChanged;
        public event System.Action<bool, bool, bool, bool> OnChannelsChanged;

        // Vertex color events
        public event System.Action OnBakeVertexColorClicked;
        public event System.Action<Mesh> OnBaseVCMeshChanged;
        public event System.Action<bool> OnOverwriteExistingChanged;

        // Preferences events
        public event System.Action<bool> OnUseEnglishChanged;
        public event System.Action<Vector3> OnWorkCopyOffsetChanged;
        public event System.Action<KeyCode> OnHotkeyChanged;
        public event System.Action<bool> OnAutoWorkCopyChanged;

        public void Bind(VisualElement root)
        {
            _overlayFoldout          = root.Q<Foldout>("overlay-foldout");
            _overlayOnTopToggle      = root.Q<Toggle>("overlay-on-top-toggle");
            _disableAAToggle         = root.Q<Toggle>("disable-aa-toggle");
            _backfaceCullToggle      = root.Q<Toggle>("backface-cull-toggle");
            _thicknessSlider         = root.Q<Slider>("thickness-slider");
            _depthOffsetSlider       = root.Q<Slider>("depth-offset-slider");
            _selectedColorField      = root.Q<ColorField>("selected-color-field");
            _seamColorField          = root.Q<ColorField>("seam-color-field");
            _previewFillColorField   = root.Q<ColorField>("preview-fill-color-field");
            _overlayAlphaSlider      = root.Q<Slider>("overlay-alpha-slider");
            _showIslandPreviewToggle = root.Q<Toggle>("show-island-preview-toggle");
            _overlayBaseTexToggle    = root.Q<Toggle>("overlay-base-tex-toggle");

            _channelWriteFoldout = root.Q<Foldout>("channel-write-foldout");
            _channelWriteToggle  = root.Q<Toggle>("channel-write-toggle");
            _channelWriteContent = root.Q<VisualElement>("channel-write-content");
            _basePngField        = root.Q<ObjectField>("base-png-field");
            _writeChannelsLabel  = root.Q<Label>("write-channels-label");
            _writeRToggle        = root.Q<Toggle>("write-r-toggle");
            _writeGToggle        = root.Q<Toggle>("write-g-toggle");
            _writeBToggle        = root.Q<Toggle>("write-b-toggle");
            _writeAToggle        = root.Q<Toggle>("write-a-toggle");

            _vertexColorFoldout = root.Q<Foldout>("vertex-color-foldout");
            _baseVcMeshField    = root.Q<ObjectField>("base-vc-mesh-field");
            _overwriteVcToggle  = root.Q<Toggle>("overwrite-vc-toggle");
            _bakeVcBtn          = root.Q<Button>("bake-vc-btn");

            _preferencesFoldout   = root.Q<Foldout>("preferences-foldout");
            _englishToggle        = root.Q<Toggle>("english-toggle");
            _hotkeyField          = root.Q<TextField>("hotkey-field");
            _autoWorkCopyToggle   = root.Q<Toggle>("auto-workcopy-toggle");
            _workcopyOffsetField  = root.Q<Vector3Field>("workcopy-offset-field");

            // ── Scene overlay ────────────────────────────────────────────
            _overlayOnTopToggle.RegisterValueChangedCallback(evt => OnOverlayOnTopChanged?.Invoke(evt.newValue));
            _disableAAToggle.RegisterValueChangedCallback(evt => OnDisableAAChanged?.Invoke(evt.newValue));
            _backfaceCullToggle.RegisterValueChangedCallback(evt => OnBackfaceCullChanged?.Invoke(evt.newValue));
            _thicknessSlider.RegisterValueChangedCallback(evt => OnThicknessChanged?.Invoke(evt.newValue));
            _depthOffsetSlider.RegisterValueChangedCallback(evt => OnDepthOffsetChanged?.Invoke(evt.newValue / 1000f));
            _selectedColorField.RegisterValueChangedCallback(evt => OnSelectedColorChanged?.Invoke(evt.newValue));
            _seamColorField.RegisterValueChangedCallback(evt => OnSeamColorChanged?.Invoke(evt.newValue));
            _previewFillColorField.RegisterValueChangedCallback(evt => OnPreviewFillColorChanged?.Invoke(evt.newValue));
            _overlayAlphaSlider.RegisterValueChangedCallback(evt => OnOverlayAlphaChanged?.Invoke(evt.newValue));
            _showIslandPreviewToggle.RegisterValueChangedCallback(evt => OnShowIslandPreviewChanged?.Invoke(evt.newValue));
            _overlayBaseTexToggle.RegisterValueChangedCallback(evt => OnPreviewOverlayBaseChanged?.Invoke(evt.newValue));

            // ── Channel write ────────────────────────────────────────────
            _channelWriteToggle.RegisterValueChangedCallback(evt =>
            {
                OnChannelWriteEnabledChanged?.Invoke(evt.newValue);
                _channelWriteContent.SetEnabled(evt.newValue);
            });
            _basePngField.RegisterValueChangedCallback(evt => OnBasePNGChanged?.Invoke(evt.newValue as Texture2D));

            System.Action fireChannelsChanged = () => OnChannelsChanged?.Invoke(
                _writeRToggle.value, _writeGToggle.value, _writeBToggle.value, _writeAToggle.value);
            _writeRToggle.RegisterValueChangedCallback(evt => fireChannelsChanged());
            _writeGToggle.RegisterValueChangedCallback(evt => fireChannelsChanged());
            _writeBToggle.RegisterValueChangedCallback(evt => fireChannelsChanged());
            _writeAToggle.RegisterValueChangedCallback(evt => fireChannelsChanged());

            // ── Vertex color bake ─────────────────────────────────────────
            _baseVcMeshField.RegisterValueChangedCallback(evt => OnBaseVCMeshChanged?.Invoke(evt.newValue as Mesh));
            _overwriteVcToggle.RegisterValueChangedCallback(evt => OnOverwriteExistingChanged?.Invoke(evt.newValue));
            _bakeVcBtn.clicked += () => OnBakeVertexColorClicked?.Invoke();

            // ── Preferences ────────────────────────────────────────────────
            _englishToggle.RegisterValueChangedCallback(evt => OnUseEnglishChanged?.Invoke(evt.newValue));
            _hotkeyField.RegisterValueChangedCallback(evt =>
            {
                if (System.Enum.TryParse<KeyCode>(evt.newValue, out var parsed))
                {
                    _currentHotkeyStr = evt.newValue;
                    OnHotkeyChanged?.Invoke(parsed);
                }
                else
                {
                    _hotkeyField.SetValueWithoutNotify(_currentHotkeyStr);
                }
            });
            _autoWorkCopyToggle.RegisterValueChangedCallback(evt => OnAutoWorkCopyChanged?.Invoke(evt.newValue));
            _workcopyOffsetField.RegisterValueChangedCallback(evt => OnWorkCopyOffsetChanged?.Invoke(evt.newValue));
        }

        public void ApplyLocalization()
        {
            _overlayFoldout.text = _localization["scene_overlay"];
            _overlayFoldout.tooltip = _localization["advanced_options_tooltip"];

            _overlayOnTopToggle.label = _localization["overlay_on_top"];
            _overlayOnTopToggle.tooltip = _localization["overlay_on_top_tooltip"];
            _disableAAToggle.label = _localization["disable_aa"];
            _disableAAToggle.tooltip = _localization["disable_aa_tooltip"];
            _backfaceCullToggle.label = _localization["backface_cull"];
            _backfaceCullToggle.tooltip = _localization["backface_cull_tooltip"];
            _thicknessSlider.label = _localization["thickness"];
            _thicknessSlider.tooltip = _localization["thickness_tooltip"];
            _depthOffsetSlider.label = _localization["depth_offset"];
            _depthOffsetSlider.tooltip = _localization["depth_offset_tooltip"];
            _selectedColorField.label = _localization["selected_islands_color"];
            _selectedColorField.tooltip = _localization["selected_islands_color_tooltip"];
            _seamColorField.label = _localization["seam_color"];
            _seamColorField.tooltip = _localization["seam_color_tooltip"];
            _previewFillColorField.label = _localization["preview_fill_color"];
            _previewFillColorField.tooltip = _localization["preview_fill_color_tooltip"];
            _overlayAlphaSlider.label = _localization["overlay_alpha"];
            _overlayAlphaSlider.tooltip = _localization["overlay_alpha_tooltip"];
            _showIslandPreviewToggle.label = _localization["show_island_preview"];
            _showIslandPreviewToggle.tooltip = _localization["show_island_preview_tooltip"];
            _overlayBaseTexToggle.label = _localization["preview_overlay_base"];
            _overlayBaseTexToggle.tooltip = _localization["preview_overlay_base_tooltip"];

            _channelWriteFoldout.text = _localization["channel_write"];
            _channelWriteFoldout.tooltip = _localization["channel_write_tooltip"];
            _channelWriteToggle.label = _localization["channel_write_enabled"];
            _channelWriteToggle.tooltip = _localization["channel_write_enabled_tooltip"];
            _basePngField.label = _localization["base_png"];
            _basePngField.tooltip = _localization["base_png_tooltip"];
            _writeChannelsLabel.text = _localization["write_channels"];
            // R/G/B/A toggle labels stay as the literal "R"/"G"/"B"/"A" authored in
            // the UXML, matching the legacy drawer (which never localized them).

            _vertexColorFoldout.text = _localization["vertex_color_bake"];
            _vertexColorFoldout.tooltip = _localization["bake_to_vertex_colors_tooltip"];
            _baseVcMeshField.label = _localization["base_vc_mesh"];
            _baseVcMeshField.tooltip = _localization["base_vc_mesh_tooltip"];
            _overwriteVcToggle.label = _localization["overwrite_existing"];
            _overwriteVcToggle.tooltip = _localization["overwrite_existing_tooltip"];
            _bakeVcBtn.text = _localization["bake_to_vertex_colors"];
            _bakeVcBtn.tooltip = _localization["bake_to_vertex_colors_tooltip"];

            _preferencesFoldout.text = _localization.Get("preferences", "環境設定");
            _preferencesFoldout.tooltip = _localization.Get(
                "preferences_tooltip", "言語、ホットキー、自動ワークコピーなどのエディタ設定");
            _englishToggle.label = _localization.Get("enable_english", "Enable English Mode");
            _englishToggle.tooltip = _localization.Get("enable_english_tooltip", "Switch UI language to English");
            _hotkeyField.label = _localization["toggle_hotkey_label"];
            _hotkeyField.tooltip = _localization["toggle_hotkey_tooltip"];
            _autoWorkCopyToggle.label = _localization["auto_work_copy"];
            _autoWorkCopyToggle.tooltip = _localization["auto_work_copy_tooltip"];
            _workcopyOffsetField.label = _localization.Get("work_copy_offset", "Work Copy Offset");
            _workcopyOffsetField.tooltip = _localization.Get(
                "work_copy_offset_tooltip", "Position offset for the work copy object");
        }

        public void UpdateState(
            MaskSettings settings, Texture2D basePNG, Mesh baseVCMesh, bool hasAnalysis, Texture baseTexture)
        {
            _overlayOnTopToggle.SetValueWithoutNotify(settings.OverlayOnTop);
            _disableAAToggle.SetValueWithoutNotify(settings.DisableAA);
            _backfaceCullToggle.SetValueWithoutNotify(settings.BackfaceCull);
            _thicknessSlider.SetValueWithoutNotify(settings.OverlaySeamThickness);
            _depthOffsetSlider.SetValueWithoutNotify(settings.OverlayDepthOffset * 1000f);
            _selectedColorField.SetValueWithoutNotify(settings.SelectedSceneColor);
            _seamColorField.SetValueWithoutNotify(settings.SeamColor);
            _previewFillColorField.SetValueWithoutNotify(settings.PreviewFillSelectedColor);
            _overlayAlphaSlider.SetValueWithoutNotify(settings.PreviewOverlayAlpha);
            _showIslandPreviewToggle.SetValueWithoutNotify(settings.ShowIslandPreview);
            _overlayBaseTexToggle.SetValueWithoutNotify(settings.PreviewOverlayBaseTex);
            _overlayBaseTexToggle.SetEnabled(baseTexture != null);

            _channelWriteToggle.SetValueWithoutNotify(settings.ChannelWriteEnabled);
            _channelWriteContent.SetEnabled(settings.ChannelWriteEnabled);
            _basePngField.SetValueWithoutNotify(basePNG);
            _writeRToggle.SetValueWithoutNotify(settings.WriteR);
            _writeGToggle.SetValueWithoutNotify(settings.WriteG);
            _writeBToggle.SetValueWithoutNotify(settings.WriteB);
            _writeAToggle.SetValueWithoutNotify(settings.WriteA);

            _baseVcMeshField.SetValueWithoutNotify(baseVCMesh);
            _overwriteVcToggle.SetValueWithoutNotify(settings.OverwriteExistingVC);
            // bake-vc-btn's enabled state (hasAnalysis) is managed by the window.

            _englishToggle.SetValueWithoutNotify(settings.UseEnglish);
            _currentHotkeyStr = settings.ModeToggleHotkey.ToString();
            _hotkeyField.SetValueWithoutNotify(_currentHotkeyStr);
            _autoWorkCopyToggle.SetValueWithoutNotify(settings.AutoWorkCopy);
            _workcopyOffsetField.SetValueWithoutNotify(settings.WorkCopyOffset);
        }
    }
}
