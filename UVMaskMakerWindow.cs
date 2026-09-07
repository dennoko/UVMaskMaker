// UVMaskMakerWindow.cs
// Unity 2022.3+ Editor tool to generate black/white UV mask images based on selected UV islands.
// UI Toolkit (UXML/USS) implementation using the dennokoworks floating design system.
// Layout:
//   [Header]  Window title
//   [Split ]  Preview zone (zoom/pan/paint) / Settings (scrollable cards)
//   [Fixed ]  Status bar

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
using Dennoko.UVTools.Core;
using Dennoko.UVTools.Data;
using Dennoko.UVTools.Services;
using Dennoko.UVTools.UI;

namespace Dennoko.UVTools
{
    /// <summary>
    /// Main EditorWindow for UV Mask Maker tool.
    /// UI structure lives in UVMaskMakerWindow.uxml, styling in DennokoTheme.uss +
    /// MaskMakerStyles.uss. This class wires services, section binders and the
    /// UVPreviewElement together.
    /// </summary>
    public class UVMaskMakerWindow : EditorWindow
    {
        // ── UXML / USS asset GUIDs (from the .meta files in UI/) ─────────────
        private const string UXML_GUID       = "de41846a61644c30919bab3fc03bc850";
        private const string THEME_USS_GUID  = "c5f01bd7afc0497e8242567db620345e";
        private const string STYLES_USS_GUID = "1133541777034564908d34edfe2b5a64";

        // ── Services ─────────────────────────────────────────────────────────
        private SettingsManager     _settingsManager;
        private LocalizationService _localization;
        private PickingService      _pickingService;
        private OverlayRenderer     _overlayRenderer;
        private WorkCopyService     _workCopyService;
        private MaskPainter         _maskPainter;
        private IMaskExporter       _exporter;

        // ── UI: preview ──────────────────────────────────────────────────────
        private UVPreviewElement _preview;
        private Label  _zoomLabel;
        private Button _undoBtn, _redoBtn, _clearPaintBtn;
        private Button _modeSelectBtn, _modePaintBtn;
        private Button _subBrushBtn, _subRectBtn, _subLassoBtn, _subEraserBtn;
        private VisualElement _paintTools;
        private Label  _brushSizeLabel;
        private SliderInt _brushSizeSlider;
        private Button _resetViewBtn;

        // ── UI: selection card ───────────────────────────────────────────────
        private VisualElement _selectionCard;
        private Label  _selectionTitle;
        private Button _modeAddBtn, _modeRemoveBtn;
        private Button _invertBtn, _selectAllBtn, _clearSelectionBtn;
        private Button _pausePickBtn;

        // ── UI: analysis-dependent buttons owned by binder sections ─────────
        private Button _savePngBtn, _bakeVcBtn;

        // ── UI: status bar ───────────────────────────────────────────────────
        private Label _statusLabel;
        private IVisualElementScheduledItem _statusResetSchedule;

        // ── UI: version info ────────────────────────────────────────────────
        private Label _versionLabel;
        private Button _versionReloadBtn;
        private DennokoVersionChecker.Result _versionResult =
            new DennokoVersionChecker.Result { State = DennokoVersionChecker.State.Checking, LocalVersion = "0.0.0" };

        // ── Section binders ──────────────────────────────────────────────────
        private TargetSectionBinder   _targetBinder;
        private MaskImportBinder      _maskImportBinder;
        private ExportSectionBinder   _exportBinder;
        private AdvancedOptionsBinder _advancedBinder;

        // ── Settings ──────────────────────────────────────────────────────────
        private MaskSettings _settings;

        // ── Target state ──────────────────────────────────────────────────────
        private GameObject _targetGO;
        private Renderer   _targetRenderer;
        private Mesh       _targetMesh;
        private Transform  _targetTransform;
        private bool       _isWorkCopy;
        private GameObject _sourceTargetGO;

        // ── UI state ─────────────────────────────────────────────────────────
        private double _lastHotkeyToggleTime = 0;
        private bool   _suppressAutoWorkCopy = false;

        // ── Computed / selection data ─────────────────────────────────────────
        private UVAnalysis   _analysis;
        private HashSet<int> _selectedIslands = new HashSet<int>();
        private Mesh         _bakedMesh;

        // ── Asset references ─────────────────────────────────────────────────
        private Texture2D _basePNG;
        private Mesh      _baseVCMesh;

        // ── Status bar ────────────────────────────────────────────────────────
        private enum StatusType { Info, Success, Error }

        // ── Log ───────────────────────────────────────────────────────────────
        private static string LogDir  => Path.Combine(Application.dataPath, "../Logs/MaskMaker");
        private static string LogPath => Path.Combine(LogDir, "MaskMaker.log");

        // ─────────────────────────────────────────────────────────────────────
        [MenuItem("dennokoworks/MaskMaker")]
        public static void ShowWindow()
        {
            var wnd = GetWindow<UVMaskMakerWindow>();
            wnd.titleContent = new GUIContent("Mask Maker");
            wnd.minSize = new Vector2(400, 600);
            wnd.Show();
        }

        // ─────────────────────────────────────────────────────────────────────
        // Lifecycle
        // ─────────────────────────────────────────────────────────────────────

        private void OnEnable()
        {
            try
            {
                Directory.CreateDirectory(LogDir);
                Log($"[OnEnable] Window opened at {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            }
            catch { /* ignore */ }

            InitializeServices();
            InitializeBinders();
            LoadAssetReferences();
            SubscribeToEvents();
        }

        private void InitializeServices()
        {
            _settingsManager = new SettingsManager();
            _settings        = _settingsManager.Load();
            _localization    = LocalizationService.Instance;
            _localization.LoadLanguage(_settings.Language);
            _pickingService  = new PickingService();
            _overlayRenderer = new OverlayRenderer();
            _workCopyService = new WorkCopyService();
            _maskPainter     = new MaskPainter(_settings.TextureSize);
            _exporter        = new PngExporter();
        }

        private void InitializeBinders()
        {
            _targetBinder     = new TargetSectionBinder(_localization);
            _maskImportBinder = new MaskImportBinder(_localization);
            _exportBinder     = new ExportSectionBinder(_localization);
            _advancedBinder   = new AdvancedOptionsBinder(_localization);

            // Target binder events
            _targetBinder.OnTargetChanged          += SetTarget;
            _targetBinder.OnBakedMeshChanged       += OnBakedMeshOptionChanged;
            _targetBinder.OnSetupWorkCopyClicked   += SetupWorkCopy;
            _targetBinder.OnCleanupWorkCopyClicked += CleanupWorkCopy;
            _targetBinder.OnTargetSubmeshChanged   += idx =>
            {
                _settings.TargetSubmesh = idx;
                _settingsManager.Save(_settings);
                AnalyzeTargetMesh();
            };
            _targetBinder.OnUVChannelChanged += ch =>
            {
                _settings.UVChannel = ch;
                _settingsManager.Save(_settings);
                AnalyzeTargetMesh();
            };

            // Mask import binder events
            _maskImportBinder.OnLoadClicked += LoadMaskImage;

            // Export binder events
            _exportBinder.OnSaveClicked       += SaveMaskPNG;
            _exportBinder.OnResolutionChanged += size =>
            {
                _settings.TextureSize = size;
                _settingsManager.Save(_settings);
                _preview?.InvalidateLabelMap();
                _preview?.MarkDirty();
            };
            _exportBinder.OnOutputDirChanged      += dir  => { _settings.OutputDir = dir; _settingsManager.Save(_settings); _exportBinder.UpdateState(_settings); };
            _exportBinder.OnSaveInvertedChanged   += val  => { _settings.SaveInvertedToo = val; _settingsManager.Save(_settings); };
            _exportBinder.OnInvertMaskChanged     += val  => { _settings.InvertMask = val; _settingsManager.Save(_settings); _preview?.MarkDirty(); };
            _exportBinder.OnPixelMarginChanged    += val  => { _settings.PixelMargin = val; _settingsManager.Save(_settings); _preview?.MarkDirty(); };
            _exportBinder.OnUseTextureFolderChanged += val => { _settings.UseTextureFolder = val; _settingsManager.Save(_settings); _exportBinder.UpdateState(_settings); };

            // Advanced binder events
            WireAdvancedBinderEvents();
        }

        private void WireAdvancedBinderEvents()
        {
            _advancedBinder.OnOverlayOnTopChanged       += v => { _settings.OverlayOnTop           = v; _settingsManager.Save(_settings); SceneView.RepaintAll(); };
            _advancedBinder.OnDisableAAChanged          += v => { _settings.DisableAA              = v; _settingsManager.Save(_settings); SceneView.RepaintAll(); };
            _advancedBinder.OnBackfaceCullChanged       += v => { _settings.BackfaceCull           = v; _settingsManager.Save(_settings); SceneView.RepaintAll(); };
            _advancedBinder.OnThicknessChanged          += v => { _settings.OverlaySeamThickness   = v; _settingsManager.Save(_settings); SceneView.RepaintAll(); };
            _advancedBinder.OnDepthOffsetChanged        += v => { _settings.OverlayDepthOffset     = v; _settingsManager.Save(_settings); SceneView.RepaintAll(); };
            _advancedBinder.OnSeamColorChanged          += c => { _settings.SeamColor              = c; _settingsManager.Save(_settings); SceneView.RepaintAll(); };
            _advancedBinder.OnSelectedColorChanged      += c => { _settings.SelectedSceneColor     = c; _settingsManager.Save(_settings); SceneView.RepaintAll(); };
            _advancedBinder.OnPreviewFillColorChanged   += c => { _settings.PreviewFillSelectedColor = c; _settingsManager.Save(_settings); _preview?.MarkDirty(); };
            _advancedBinder.OnOverlayAlphaChanged       += v => { _settings.PreviewOverlayAlpha    = v; _settingsManager.Save(_settings); _preview?.MarkDirty(); };
            _advancedBinder.OnShowIslandPreviewChanged  += v => { _settings.ShowIslandPreview      = v; _settingsManager.Save(_settings); _preview?.RefreshBorderOverlay(); };
            _advancedBinder.OnPreviewOverlayBaseChanged += v => { _settings.PreviewOverlayBaseTex  = v; _settingsManager.Save(_settings); RefreshPreviewBaseTexture(); };
            _advancedBinder.OnChannelWriteEnabledChanged += v => { _settings.ChannelWriteEnabled   = v; _settingsManager.Save(_settings); };
            _advancedBinder.OnBasePNGChanged            += tex => { _basePNG = tex; _settingsManager.SetBasePNGPath(tex ? AssetDatabase.GetAssetPath(tex) : ""); };
            _advancedBinder.OnChannelsChanged           += (r, g, b, a) => { _settings.WriteR = r; _settings.WriteG = g; _settings.WriteB = b; _settings.WriteA = a; _settingsManager.Save(_settings); };
            _advancedBinder.OnBakeVertexColorClicked    += BakeMaskToVertexColors;
            _advancedBinder.OnBaseVCMeshChanged         += m => { _baseVCMesh = m; _settingsManager.SetBaseVCMeshPath(m ? AssetDatabase.GetAssetPath(m) : ""); };
            _advancedBinder.OnOverwriteExistingChanged  += v => { _settings.OverwriteExistingVC   = v; _settingsManager.Save(_settings); };
            _advancedBinder.OnWorkCopyOffsetChanged     += v => { _settings.WorkCopyOffset        = v; _settingsManager.Save(_settings); };
            _advancedBinder.OnAutoWorkCopyChanged       += v => { _settings.AutoWorkCopy          = v; _settingsManager.Save(_settings); };
            _advancedBinder.OnHotkeyChanged             += key => { _settings.ModeToggleHotkey    = key; _settingsManager.Save(_settings); };
            _advancedBinder.OnUseEnglishChanged         += OnLanguageChanged;
        }

        private void LoadAssetReferences()
        {
            var basePngPath = _settingsManager.GetBasePNGPath();
            if (!string.IsNullOrEmpty(basePngPath)) _basePNG = AssetDatabase.LoadAssetAtPath<Texture2D>(basePngPath);
            var baseVCPath = _settingsManager.GetBaseVCMeshPath();
            if (!string.IsNullOrEmpty(baseVCPath)) _baseVCMesh = AssetDatabase.LoadAssetAtPath<Mesh>(baseVCPath);
        }

        private void SubscribeToEvents()
        {
            SceneView.duringSceneGui += OnSceneGUI;
            EditorSceneManager.sceneSaving += OnSceneSaving;
            EditorApplication.update += EditorUpdate;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            EditorSceneManager.sceneSaving -= OnSceneSaving;
            EditorApplication.update -= EditorUpdate;

            if (_settingsManager != null && _settings != null) _settingsManager.Save(_settings);
            _pickingService?.Dispose();

            // Restore original visibility if needed
            if (_isWorkCopy && _sourceTargetGO != null)
            {
                _sourceTargetGO.SetActive(true);
            }

            // _preview disposes its textures via DetachFromPanelEvent
            if (_bakedMesh != null) { try { DestroyImmediate(_bakedMesh); } catch { } _bakedMesh = null; }

            Log("[OnDisable] Window closed");
        }

        private void OnSceneSaving(UnityEngine.SceneManagement.Scene scene, string path) => _pickingService?.Cleanup();

        /// <summary>
        /// Called every editor frame. Repaints the scene view during active paint
        /// strokes so hand-painted regions are immediately visible on the 3D mesh
        /// via the mask overlay renderer. The preview element repaints itself.
        /// </summary>
        private void EditorUpdate()
        {
            if (_preview != null && _preview.IsPainting)
            {
                SceneView.RepaintAll();
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // CreateGUI — UI Toolkit setup
        // ─────────────────────────────────────────────────────────────────────

        public void CreateGUI()
        {
            VisualElement root = rootVisualElement;

            // テーマ非依存のためのルートクラスを適用
            root.AddToClassList("dennoko-root");
            // USS ロード失敗時も背景が明るくならないよう Surface0 を C# 側でも保証
            root.style.backgroundColor = (Color)new Color32(0x12, 0x12, 0x12, 0xFF);
            root.style.flexGrow = 1;

            // 標準フォント: OS のメイリオを全体に適用（全テキスト要素へ継承される）。
            // 生成・アトラス保護・キャッシュ消失時の再適用はすべて DennokoUIFont が行う。
            DennokoUIFont.Apply(root);

            // USS のロードと適用 (テーマ + ツール固有)
            LoadStyleSheet(root, THEME_USS_GUID);
            LoadStyleSheet(root, STYLES_USS_GUID);

            // UXML のロードとインスタンス化
            string uxmlPath = AssetDatabase.GUIDToAssetPath(UXML_GUID);
            var uxml = string.IsNullOrEmpty(uxmlPath)
                ? null
                : AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(uxmlPath);
            if (uxml == null)
            {
                root.Add(new Label("UXML Asset が見つかりません。GUID を確認してください。"));
                return;
            }
            uxml.CloneTree(root);

            InitializeUI(root);
        }

        private static void LoadStyleSheet(VisualElement root, string guid)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var uss = string.IsNullOrEmpty(path)
                ? null
                : AssetDatabase.LoadAssetAtPath<StyleSheet>(path);
            if (uss != null)
            {
                root.styleSheets.Add(uss);
            }
            else
            {
                Debug.LogWarning($"[{nameof(UVMaskMakerWindow)}] USS が見つかりません。GUID を確認してください: {guid}");
            }
        }

        private void InitializeUI(VisualElement root)
        {
            // ── Preview element ─────────────────────────────────────────────
            _preview = root.Q<UVPreviewElement>("uv-preview");
            _preview.OnIslandClicked      += OnPreviewIslandClicked;
            _preview.OnPaintStrokeFinished += OnPaintStrokeFinished;
            _preview.OnViewChanged        += UpdateZoomLabel;
            _preview.SetContext(_analysis, _selectedIslands, _settings, _maskPainter);
            RefreshPreviewBaseTexture();

            // ── Preview toolbar ─────────────────────────────────────────────
            _zoomLabel     = root.Q<Label>("zoom-label");
            _resetViewBtn  = root.Q<Button>("reset-view-btn");
            _undoBtn       = root.Q<Button>("undo-btn");
            _redoBtn       = root.Q<Button>("redo-btn");
            _clearPaintBtn = root.Q<Button>("clear-paint-btn");

            _undoBtn.clicked += () => { _maskPainter.Undo(); _preview.MarkDirty(); RefreshPaintToolbar(); };
            _redoBtn.clicked += () => { _maskPainter.Redo(); _preview.MarkDirty(); RefreshPaintToolbar(); };
            _clearPaintBtn.clicked += () =>
            {
                _maskPainter.Clear();
                _preview.MarkDirty();
                RefreshPaintToolbar();
            };
            _resetViewBtn.clicked += () => _preview.ResetView();

            // ── Paint footer ────────────────────────────────────────────────
            _modeSelectBtn   = root.Q<Button>("mode-select-btn");
            _modePaintBtn    = root.Q<Button>("mode-paint-btn");
            _paintTools      = root.Q<VisualElement>("paint-tools");
            _subBrushBtn     = root.Q<Button>("sub-brush-btn");
            _subRectBtn      = root.Q<Button>("sub-rect-btn");
            _subLassoBtn     = root.Q<Button>("sub-lasso-btn");
            _subEraserBtn    = root.Q<Button>("sub-eraser-btn");
            _brushSizeLabel  = root.Q<Label>("brush-size-label");
            _brushSizeSlider = root.Q<SliderInt>("brush-size-slider");

            _modeSelectBtn.clicked += () => SetPaintMode(false);
            _modePaintBtn.clicked  += () => SetPaintMode(true);
            _subBrushBtn.clicked   += () => SetPaintSubMode(PaintSubMode.Brush);
            _subRectBtn.clicked    += () => SetPaintSubMode(PaintSubMode.Rect);
            _subLassoBtn.clicked   += () => SetPaintSubMode(PaintSubMode.Lasso);
            _subEraserBtn.clicked  += () => SetPaintSubMode(PaintSubMode.Eraser);

            _brushSizeSlider.value = _settings.BrushSize;
            _brushSizeSlider.RegisterValueChangedCallback(evt =>
            {
                _settings.BrushSize = evt.newValue;
                _settingsManager.Save(_settings);
            });

            // ── Selection card ──────────────────────────────────────────────
            _selectionCard     = root.Q<VisualElement>("selection-card");
            _selectionTitle    = root.Q<Label>("selection-title");
            _modeAddBtn        = root.Q<Button>("mode-add-btn");
            _modeRemoveBtn     = root.Q<Button>("mode-remove-btn");
            _invertBtn         = root.Q<Button>("invert-btn");
            _selectAllBtn      = root.Q<Button>("select-all-btn");
            _clearSelectionBtn = root.Q<Button>("clear-selection-btn");
            _pausePickBtn      = root.Q<Button>("pause-pick-btn");

            _modeAddBtn.clicked    += () => SetAddMode(true);
            _modeRemoveBtn.clicked += () => SetAddMode(false);
            _invertBtn.clicked         += InvertSelection;
            _selectAllBtn.clicked      += SelectAll;
            _clearSelectionBtn.clicked += ClearSelection;
            _pausePickBtn.clicked      += ToggleScenePickPaused;

            // ── Analysis-dependent buttons (inside binder sections) ─────────
            _savePngBtn = root.Q<Button>("save-png-btn");
            _bakeVcBtn  = root.Q<Button>("bake-vc-btn");

            // ── Section binders ─────────────────────────────────────────────
            _targetBinder.Bind(root);
            _maskImportBinder.Bind(root);
            _exportBinder.Bind(root);
            _advancedBinder.Bind(root);

            _exportBinder.UpdateState(_settings);
            _advancedBinder.UpdateState(_settings, _basePNG, _baseVCMesh, _analysis != null, GetBaseTexture());

            // ── Status bar ──────────────────────────────────────────────────
            _statusLabel = root.Q<Label>("status-label");

            // ── Hotkey (window focus path; scene view path is in OnSceneGUI) ─
            root.focusable = true;
            root.RegisterCallback<KeyDownEvent>(OnRootKeyDown, TrickleDown.TrickleDown);

            // ── Initial state ───────────────────────────────────────────────
            ApplyLocalization();
            RefreshTargetDependentUI();
            RefreshModeUI();
            RefreshPaintToolbar();
            UpdateZoomLabel();

            // ── Version Info ────────────────────────────────────────────────
            _versionLabel = root.Q<Label>("version-label");
            _versionReloadBtn = root.Q<Button>("version-reload-button");
            if (_versionReloadBtn != null)
            {
                _versionReloadBtn.clicked += () =>
                {
                    MaskMakerVersion.ForceRecheck();
                    LoadVersionResultFromSessionState();
                };
            }
            StartVersionCheck();
        }

        // ─────────────────────────────────────────────────────────────────────
        // UI refresh helpers
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>Applies localized texts/tooltips to window-owned elements and all binders.</summary>
        private void ApplyLocalization()
        {
            if (_preview == null) return; // CreateGUI not yet run

            _preview.SetHintText(_localization.Get("preview_hint", "Run analysis to preview UVs"));

            _undoBtn.text       = _localization.Get("tool_undo", "Undo");
            _redoBtn.text       = _localization.Get("tool_redo", "Redo");
            _clearPaintBtn.text = _localization.Get("tool_clear", "Clear");
            _resetViewBtn.text    = "Reset";
            _resetViewBtn.tooltip = _localization.Get("preview_reset_view", "ビューをリセット");

            _modeSelectBtn.text = _localization.Get("tool_select", "Select");
            _modePaintBtn.text  = _localization.Get("tool_paint",  "Paint");
            _subBrushBtn.text   = _localization.Get("tool_brush",  "Brush");
            _subRectBtn.text    = _localization.Get("tool_rect",   "Rect");
            _subLassoBtn.text   = _localization.Get("tool_lasso",  "Lasso");
            _subEraserBtn.text  = _localization.Get("tool_eraser", "Eraser");
            _brushSizeLabel.text = _localization.Get("brush_size", "Size");

            _selectionTitle.text = _localization.Get("island_selection", "アイランド選択");
            _modeAddBtn.text        = _localization["mode_add"];
            _modeAddBtn.tooltip     = _localization["mode_add_tooltip"];
            _modeRemoveBtn.text     = _localization["mode_remove"];
            _modeRemoveBtn.tooltip  = _localization["mode_remove_tooltip"];
            _invertBtn.text         = _localization["invert"];
            _invertBtn.tooltip      = _localization["invert_tooltip"];
            _selectAllBtn.text      = _localization["select_all"];
            _selectAllBtn.tooltip   = _localization["select_all_tooltip"];
            _clearSelectionBtn.text    = _localization["clear_selection"];
            _clearSelectionBtn.tooltip = _localization["clear_selection_tooltip"];
            RefreshScenePickUI();

            _targetBinder.ApplyLocalization();
            _maskImportBinder.ApplyLocalization();
            _exportBinder.ApplyLocalization();
            _advancedBinder.ApplyLocalization();

            if (_statusLabel != null &&
                !_statusLabel.ClassListContains("dennoko-status--success") &&
                !_statusLabel.ClassListContains("dennoko-status--error"))
            {
                _statusLabel.text = _localization.Get("status_ready", "Ready");
            }

            if (_versionReloadBtn != null)
            {
                _versionReloadBtn.tooltip = _localization.Get("version_reload_tooltip", "アップデートを再確認");
            }
            ApplyVersionLabel();
        }

        /// <summary>Refreshes everything that depends on the current target / analysis.</summary>
        private void RefreshTargetDependentUI()
        {
            if (_preview == null) return; // CreateGUI not yet run

            bool hasAnalysis = _analysis != null;
            _selectionCard.SetEnabled(hasAnalysis);
            _savePngBtn?.SetEnabled(hasAnalysis);
            _bakeVcBtn?.SetEnabled(hasAnalysis);

            _targetBinder.UpdateState(_targetGO, _targetRenderer, _settings, _isWorkCopy);
            _advancedBinder.UpdateState(_settings, _basePNG, _baseVCMesh, hasAnalysis, GetBaseTexture());

            // Export file name follows the target
            string fileName = _targetGO != null ? _targetGO.name : "uv_mask";
            if (_isWorkCopy && fileName.EndsWith(" [WorkCopy]"))
                fileName = fileName.Replace(" [WorkCopy]", "");
            _exportBinder.FileName = fileName + "_mask";

            RefreshPreviewBaseTexture();
        }

        /// <summary>Select/Paint mode + sub-mode button active states, brush slider enabling.</summary>
        private void RefreshModeUI()
        {
            if (_preview == null) return;

            _modeSelectBtn.EnableInClassList("dennoko-button-active", !_settings.IsPaintMode);
            _modePaintBtn.EnableInClassList("dennoko-button-active", _settings.IsPaintMode);
            _paintTools.SetEnabled(_settings.IsPaintMode);

            _subBrushBtn.EnableInClassList("dennoko-button-active", _settings.PaintSubMode == PaintSubMode.Brush);
            _subRectBtn.EnableInClassList("dennoko-button-active", _settings.PaintSubMode == PaintSubMode.Rect);
            _subLassoBtn.EnableInClassList("dennoko-button-active", _settings.PaintSubMode == PaintSubMode.Lasso);
            _subEraserBtn.EnableInClassList("dennoko-button-active", _settings.PaintSubMode == PaintSubMode.Eraser);

            bool usesSize = _settings.PaintSubMode == PaintSubMode.Brush
                         || _settings.PaintSubMode == PaintSubMode.Eraser;
            _brushSizeLabel.SetEnabled(usesSize);
            _brushSizeSlider.SetEnabled(usesSize);

            _modeAddBtn.EnableInClassList("dennoko-button-active", _settings.AddMode);
            _modeRemoveBtn.EnableInClassList("dennoko-button-active", !_settings.AddMode);

            RefreshScenePickUI();
        }

        /// <summary>
        /// Pause button text/tooltip/active state. While paused the Add/Remove mode
        /// buttons stay usable but scene view clicks are left to Unity.
        /// </summary>
        private void RefreshScenePickUI()
        {
            if (_pausePickBtn == null) return;

            bool paused = _settings.ScenePickPaused;
            _pausePickBtn.text = paused
                ? _localization.Get("scene_pick_resume", "シーン選択を再開")
                : _localization.Get("scene_pick_pause",  "シーン選択を一時停止");
            _pausePickBtn.tooltip = _localization.Get(
                "scene_pick_pause_tooltip",
                "一時停止中はシーンビューのクリックを奪わず、Unity 標準の選択・操作が行えます。");
            _pausePickBtn.EnableInClassList("dennoko-button-active", paused);
        }

        private void ToggleScenePickPaused()
        {
            _settings.ScenePickPaused = !_settings.ScenePickPaused;
            _settingsManager.Save(_settings);
            RefreshScenePickUI();
            SetStatus(
                _settings.ScenePickPaused
                    ? _localization.Get("status_scene_pick_paused", "シーン選択を一時停止中（Unity の操作が可能）")
                    : _localization.Get("status_scene_pick_resumed", "シーン選択を再開しました"),
                StatusType.Info);
            SceneView.RepaintAll();
        }

        /// <summary>Undo/Redo button enabled states.</summary>
        private void RefreshPaintToolbar()
        {
            if (_preview == null) return;
            _undoBtn.SetEnabled(_maskPainter.HasUndo);
            _redoBtn.SetEnabled(_maskPainter.HasRedo);
        }

        private void UpdateZoomLabel()
        {
            if (_zoomLabel == null || _preview == null) return;
            _zoomLabel.text = $"{Mathf.RoundToInt(_preview.ZoomLevel * 100)}%";
        }

        private void RefreshPreviewBaseTexture()
        {
            _preview?.SetBaseTexture(_settings.PreviewOverlayBaseTex ? GetBaseTexture() : null);
        }

        private void SetPaintMode(bool paint)
        {
            if (_settings.IsPaintMode == paint) return;
            _settings.IsPaintMode = paint;
            _settingsManager.Save(_settings);
            RefreshModeUI();
        }

        private void SetPaintSubMode(PaintSubMode mode)
        {
            if (_settings.PaintSubMode == mode) return;
            _settings.PaintSubMode = mode;
            _settingsManager.Save(_settings);
            RefreshModeUI();
        }

        private void SetAddMode(bool add)
        {
            if (_settings.AddMode == add) return;
            _settings.AddMode = add;
            _settingsManager.Save(_settings);
            RefreshModeUI();
        }

        // ─────────────────────────────────────────────────────────────────────
        // Status bar
        // ─────────────────────────────────────────────────────────────────────

        private void SetStatus(string message, StatusType type, long autoResetMs = 4000)
        {
            if (_statusLabel == null) return; // CreateGUI not yet run

            _statusLabel.text = message;
            _statusLabel.EnableInClassList("dennoko-status--success", type == StatusType.Success);
            _statusLabel.EnableInClassList("dennoko-status--error",   type == StatusType.Error);

            _statusResetSchedule?.Pause();
            if (type != StatusType.Info)
            {
                _statusResetSchedule = _statusLabel.schedule
                    .Execute(() => SetStatus(_localization.Get("status_ready", "Ready"), StatusType.Info))
                    .StartingIn(autoResetMs);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Hotkey
        // ─────────────────────────────────────────────────────────────────────

        private void OnRootKeyDown(KeyDownEvent e)
        {
            // Ignore while typing in a text input
            if (e.target is TextElement || e.target is TextField) return;

            if (e.keyCode == _settings.ModeToggleHotkey && _targetMesh != null)
            {
                ToggleAddRemoveMode(null);
                e.StopPropagation();
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Core Logic
        // ─────────────────────────────────────────────────────────────────────

        private bool TryFixReadWrite(Mesh mesh)
        {
            if (mesh.isReadable) return true;
            string path = AssetDatabase.GetAssetPath(mesh);
            if (string.IsNullOrEmpty(path)) return false;
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer != null)
            {
                importer.isReadable = true;
                importer.SaveAndReimport();
                Log($"[AutoFix] Enabled Read/Write for {path}");
                return true;
            }
            return false;
        }

        private void SetTarget(GameObject go)
        {
            if (_targetGO != null && _isWorkCopy && _targetGO != go)
            {
                Log($"[SetTarget] Auto-cleaning up work copy '{_targetGO.name}'");
                if (_sourceTargetGO != null) _sourceTargetGO.SetActive(true);
                _workCopyService.CleanupWorkCopy(_targetGO);
                _isWorkCopy = false;
            }

            _targetGO        = go;
            _targetRenderer  = null;
            _targetMesh      = null;
            _targetTransform = null;
            _analysis        = null;
            _selectedIslands.Clear();
            _pickingService.Cleanup();
            _overlayRenderer.InvalidateCache();
            _preview?.InvalidateLabelMap();
            _preview?.SetContext(_analysis, _selectedIslands, _settings, _maskPainter);
            _preview?.MarkDirty();

            if (_bakedMesh != null) { try { DestroyImmediate(_bakedMesh); } catch { } _bakedMesh = null; }
            if (_targetGO == null)
            {
                SetStatus(_localization.Get("status_no_target", "ターゲットを設定してください"), StatusType.Info);
                RefreshTargetDependentUI();
                return;
            }

            var smr = _targetGO.GetComponentInChildren<SkinnedMeshRenderer>();
            var mr  = _targetGO.GetComponentInChildren<MeshRenderer>();
            if (smr != null)
            {
                _targetRenderer  = smr;
                _targetMesh      = smr.sharedMesh;
                _targetTransform = smr.transform;
            }
            else if (mr != null)
            {
                _targetRenderer  = mr;
                var mf = mr.GetComponent<MeshFilter>();
                _targetMesh      = mf ? mf.sharedMesh : null;
                _targetTransform = mr.transform;
            }

            _isWorkCopy = _workCopyService.IsWorkCopy(_targetGO);
            if (!_isWorkCopy && _sourceTargetGO != null && _sourceTargetGO != _targetGO)
                _sourceTargetGO = null;

            if (_targetMesh == null)
            {
                SetStatus(_localization["dialog_no_mesh_msg"], StatusType.Error);
                RefreshTargetDependentUI();
                return;
            }

            if (!TryFixReadWrite(_targetMesh))
            {
                _targetGO = null; _targetRenderer = null; _targetMesh = null;
                RefreshTargetDependentUI();
                return;
            }

            Log($"[SetTarget] Target='{_targetGO.name}', Mesh='{_targetMesh.name}'");

            if (!_isWorkCopy && _settings.AutoWorkCopy && !_suppressAutoWorkCopy)
            {
                SetupWorkCopy();
                return;
            }

            BakeCurrentPoseAuto();
            AnalyzeTargetMesh();
            bool forceBakedPicking = (_targetRenderer is SkinnedMeshRenderer) && _bakedMesh != null;
            _pickingService.Initialize(_targetTransform, _targetMesh, _bakedMesh, forceBakedPicking || _settings.UseBakedMesh);
            RefreshTargetDependentUI();
        }

        private void OnBakedMeshOptionChanged(bool useBaked)
        {
            _settings.UseBakedMesh = useBaked;
            _settingsManager.Save(_settings);
            _overlayRenderer.InvalidateCache();
            bool forceBakedPicking = (_targetRenderer is SkinnedMeshRenderer) && _bakedMesh != null;
            _pickingService.UpdateMesh(_bakedMesh, forceBakedPicking || useBaked);
            SceneView.RepaintAll();
        }

        private void AnalyzeTargetMesh()
        {
            if (_targetMesh == null)
            {
                return;
            }
            try
            {
                _analysis = UVAnalyzer.Analyze(_targetMesh, _settings.UVChannel, _settings.TargetSubmesh);
                OnAnalysisSuccess();
            }
            catch (Exception ex)
            {
                if (_settings.UVChannel != 0)
                {
                    Log($"[Analyze][Warning] Failed with UV{_settings.UVChannel}, falling back to UV0. ({ex.Message})");
                    _settings.UVChannel = 0;
                    _settingsManager.Save(_settings);
                    try
                    {
                        _analysis = UVAnalyzer.Analyze(_targetMesh, 0, _settings.TargetSubmesh);
                        OnAnalysisSuccess();
                    }
                    catch (Exception ex2) { HandleAnalysisError(ex2); }
                }
                else
                {
                    HandleAnalysisError(ex);
                }
            }
        }

        private void OnAnalysisSuccess()
        {
            _selectedIslands.Clear();
            _overlayRenderer.InvalidateCache();
            _preview?.InvalidateLabelMap();
            _preview?.SetContext(_analysis, _selectedIslands, _settings, _maskPainter);
            _preview?.MarkDirty();
            BakeCurrentPoseAuto();
            RefreshTargetDependentUI();
            string msg = string.Format(
                _localization.Get("status_analyzed", "解析完了: {0} アイランド"),
                _analysis.Islands.Count);
            SetStatus(msg, StatusType.Success);
            Log($"[Analyze] Found {_analysis.Islands.Count} UV islands, {_analysis.BorderEdges.Count} UV border edges");
        }

        private void HandleAnalysisError(Exception ex)
        {
            Debug.LogError($"UV analysis failed: {ex.Message}\n{ex}");
            Log($"[Analyze][Error] {ex}");
            SetStatus(_localization.Get("status_analyze_error", "解析に失敗しました"), StatusType.Error);
        }

        private void SetupWorkCopy()
        {
            if (_targetRenderer == null || _isWorkCopy || _targetGO == null) return;
            _sourceTargetGO = _targetGO;

            var copy = _workCopyService.CreateWorkCopy(_targetRenderer, _settings.WorkCopyOffset);
            if (copy != null)
            {
                SetTarget(copy);
                Log($"[WorkCopy] Created work copy '{copy.name}'");
            }
        }

        private void CleanupWorkCopy()
        {
            if (!_isWorkCopy || _targetGO == null) return;
            var original = _sourceTargetGO;

            _suppressAutoWorkCopy = true;
            try
            {
                SetTarget(original != null ? original : null);
            }
            finally
            {
                _suppressAutoWorkCopy = false;
            }
            Log("[WorkCopy] Cleaned up work copy");
        }

        private void InvertSelection()
        {
            if (_analysis == null) return;
            var newSel = new HashSet<int>();
            for (int i = 0; i < _analysis.Islands.Count; i++)
                if (!_selectedIslands.Contains(i)) newSel.Add(i);
            _selectedIslands = newSel;
            _preview?.SetSelection(_selectedIslands);
            _preview?.MarkDirty();
            SceneView.RepaintAll();
        }

        private void SelectAll()
        {
            if (_analysis == null) return;
            _selectedIslands = new HashSet<int>(Enumerable.Range(0, _analysis.Islands.Count));
            _preview?.SetSelection(_selectedIslands);
            _preview?.MarkDirty();
            SceneView.RepaintAll();
        }

        private void ClearSelection()
        {
            _selectedIslands.Clear();
            _preview?.MarkDirty();
            SceneView.RepaintAll();
        }

        private void OnPreviewIslandClicked(int islandIdx)
        {
            if (islandIdx < 0 || _analysis == null) return;
            if (_selectedIslands.Contains(islandIdx)) _selectedIslands.Remove(islandIdx);
            else _selectedIslands.Add(islandIdx);
            _preview?.MarkDirty();
            SceneView.RepaintAll();
            Log($"[PreviewClick] island={islandIdx} TOGGLE → {(_selectedIslands.Contains(islandIdx) ? "SELECTED" : "DESELECTED")}");
        }

        private void OnPaintStrokeFinished()
        {
            _settingsManager.Save(_settings);
            RefreshPaintToolbar();
            SceneView.RepaintAll();
        }

        /// <summary>
        /// Loads an existing mask image and applies its black regions to the paint mask.
        /// Pixels whose luminance is below the threshold (0-255) are treated as "painted" (255).
        /// </summary>
        private void LoadMaskImage(Texture2D sourceTex, int blackThreshold)
        {
            if (sourceTex == null) return;

            int size = _settings.TextureSize;
            _maskPainter.EnsureSize(size);

            // Make a temporary readable copy of the texture at the target resolution
            var rt = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            Graphics.Blit(sourceTex, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;

            var readable = new Texture2D(size, size, TextureFormat.RGBA32, false, true /* linear */);
            readable.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            readable.Apply();

            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);

            _maskPainter.SaveUndoState();

            var pixels = readable.GetPixels32();
            var mask   = _maskPainter.Mask;
            float thresholdNorm = blackThreshold / 255f;

            for (int i = 0; i < pixels.Length; i++)
            {
                // GetPixels32() returns sRGB byte values; use perceptual (sRGB) luminance coefficients.
                float lum = pixels[i].r / 255f * 0.299f
                          + pixels[i].g / 255f * 0.587f
                          + pixels[i].b / 255f * 0.114f;
                if (lum < thresholdNorm)
                    mask[i] = 255;
            }

            // readable was created with new Texture2D (not an asset), DestroyImmediate is safe here.
            DestroyImmediate(readable);

            _maskPainter.MarkAllTilesDirty();
            _preview?.MarkDirty();
            RefreshPaintToolbar();
            SceneView.RepaintAll();

            SetStatus(
                _localization.Get("mask_import_done", "マスク画像を読み込みました"),
                StatusType.Success);
            Log($"[LoadMask] Loaded mask from '{sourceTex.name}' (threshold={blackThreshold})");
        }

        private Texture GetBaseTexture()
        {
            if (_targetRenderer == null) return null;
            var mats = _targetRenderer.sharedMaterials;
            if (mats == null || mats.Length == 0) return null;

            if (_settings.TargetSubmesh >= 0 && _settings.TargetSubmesh < mats.Length)
            {
                var m = mats[_settings.TargetSubmesh];
                if (m != null)
                {
                    if (m.HasProperty("_BaseMap")) { var t = m.GetTexture("_BaseMap"); if (t != null) return t; }
                    if (m.HasProperty("_MainTex")) { var t = m.GetTexture("_MainTex"); if (t != null) return t; }
                }
                return Texture2D.whiteTexture;
            }

            foreach (var m in mats)
            {
                if (m == null) continue;
                if (m.HasProperty("_BaseMap")) { var t = m.GetTexture("_BaseMap"); if (t != null) return t; }
                if (m.HasProperty("_MainTex")) { var t = m.GetTexture("_MainTex"); if (t != null) return t; }
            }
            return Texture2D.whiteTexture;
        }

        private string GetBaseTexturePath()
        {
            var tex = GetBaseTexture();
            return tex == null ? null : AssetDatabase.GetAssetPath(tex);
        }

        private void ToggleAddRemoveMode(SceneView sv)
        {
            double now = EditorApplication.timeSinceStartup;
            if (now - _lastHotkeyToggleTime < 0.05f) return;
            _lastHotkeyToggleTime = now;
            _settings.AddMode = !_settings.AddMode;
            var targetSV = sv ?? SceneView.lastActiveSceneView;
            targetSV?.ShowNotification(new GUIContent(
                _settings.AddMode
                    ? _localization["notification_mode_add"]
                    : _localization["notification_mode_remove"]));
            RefreshModeUI();
            SceneView.RepaintAll();
        }

        private void OnLanguageChanged(bool useEnglish)
        {
            _settings.UseEnglish = useEnglish;
            _settings.Language   = useEnglish ? "en" : "ja";
            _localization.LoadLanguage(_settings.Language);
            ApplyLocalization();
            SceneView.RepaintAll();
        }

        private void SaveMaskPNG()
        {
            if (_analysis == null)
            {
                EditorUtility.DisplayDialog(
                    _localization["dialog_no_data"],
                    _localization["dialog_no_data_msg"],
                    _localization["ok"]);
                return;
            }

            string targetDir = _settings.OutputDir;
            if (_settings.UseTextureFolder)
            {
                string texPath = GetBaseTexturePath();
                if (!string.IsNullOrEmpty(texPath))
                    targetDir = Path.GetDirectoryName(texPath);
            }

            if (!AssetDatabase.IsValidFolder(targetDir))
                UVMaskExport.EnsureAssetFolderPath(targetDir);

            string fileName = _exportBinder.FileName;
            if (string.IsNullOrEmpty(fileName)) fileName = "uv_mask";
            if (!fileName.EndsWith(".png")) fileName += ".png";

            string fullPath = AssetDatabase.GenerateUniqueAssetPath(
                Path.Combine(targetDir, fileName).Replace('\\', '/'));

            var exportSettings = new ExportSettings
            {
                TextureSize          = _settings.TextureSize,
                PixelMargin          = _settings.PixelMargin,
                InvertMask           = _settings.InvertMask,
                ChannelWriteEnabled  = _settings.ChannelWriteEnabled,
                WriteR = _settings.WriteR, WriteG = _settings.WriteG,
                WriteB = _settings.WriteB, WriteA = _settings.WriteA,
                BasePNG = _basePNG,
                PaintMask = _maskPainter?.Mask
            };

            if (_exporter.Export(_analysis, _selectedIslands, exportSettings, fullPath))
            {
                Log($"[Save] Wrote PNG {fullPath}");
                var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(fullPath);
                if (obj != null) EditorGUIUtility.PingObject(obj);
                SetStatus($"保存完了: {Path.GetFileName(fullPath)}", StatusType.Success);

                if (_settings.SaveInvertedToo)
                {
                    var invSettings = new ExportSettings
                    {
                        TextureSize         = exportSettings.TextureSize,
                        PixelMargin         = exportSettings.PixelMargin,
                        InvertMask          = !exportSettings.InvertMask,
                        ChannelWriteEnabled = exportSettings.ChannelWriteEnabled,
                        WriteR = exportSettings.WriteR, WriteG = exportSettings.WriteG,
                        WriteB = exportSettings.WriteB, WriteA = exportSettings.WriteA,
                        BasePNG = exportSettings.BasePNG,
                        PaintMask = exportSettings.PaintMask
                    };
                    string dir      = Path.GetDirectoryName(fullPath);
                    string nameBase = Path.GetFileNameWithoutExtension(fullPath);
                    string invPath  = Path.Combine(dir, nameBase + "_inv.png").Replace('\\', '/');
                    if (_exporter.Export(_analysis, _selectedIslands, invSettings, invPath))
                        Log($"[Save] Wrote Inverted PNG {invPath}");
                }
            }
            else
            {
                SetStatus(_localization.Get("status_save_error", "保存に失敗しました"), StatusType.Error);
            }
        }

        private void BakeMaskToVertexColors()
        {
            if (_targetMesh == null || _analysis == null)
            {
                EditorUtility.DisplayDialog(
                    _localization["dialog_no_target"],
                    _localization["dialog_run_analysis_first"],
                    _localization["ok"]);
                return;
            }
            try
            {
                Color32[] baseColors = _baseVCMesh != null && _baseVCMesh.vertexCount == _targetMesh.vertexCount
                    ? _baseVCMesh.colors32
                    : _targetMesh.colors32;

                var colors  = UVVertexColorBaker.BuildVertexColorsChannelWise(
                    _analysis, _selectedIslands, _targetMesh.vertexCount, baseColors,
                    _settings.WriteR, _settings.WriteG, _settings.WriteB, _settings.WriteA);
                var colored = UVVertexColorBaker.CreateColoredMesh(_targetMesh, colors);
                var folder  = UVVertexColorBaker.GetDefaultBakeFolderForMesh(_targetMesh);
                var assetPath = UVVertexColorBaker.SaveMeshAsset(colored, folder,
                    _targetMesh.name + "_WithVertexColors", _settings.OverwriteExistingVC);
                Log($"[BakeVC] Saved mesh with vertex colors: {assetPath}");
                RevealSaved(assetPath);
                SetStatus($"VC焼き込み完了: {Path.GetFileName(assetPath)}", StatusType.Success);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Bake vertex colors failed: {ex.Message}\n{ex}");
                EditorUtility.DisplayDialog(
                    _localization["dialog_error"],
                    _localization["dialog_bake_channel_failed"],
                    _localization["ok"]);
                SetStatus(_localization.Get("status_bake_error", "VC焼き込みに失敗しました"), StatusType.Error);
            }
        }

        private void BakeCurrentPoseAuto()
        {
            if (!(_targetRenderer is SkinnedMeshRenderer smr)) return;
            if (_bakedMesh == null) _bakedMesh = new Mesh { name = $"{_targetMesh?.name}_Baked" };
            else _bakedMesh.Clear();
            try
            {
                smr.BakeMesh(_bakedMesh);
                _overlayRenderer.InvalidateCache();
                _pickingService.UpdateMesh(_bakedMesh, _settings.UseBakedMesh);
            }
            catch { }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Scene View (IMGUI — unchanged; not part of the window UI)
        // ─────────────────────────────────────────────────────────────────────

        private void OnSceneGUI(SceneView sv)
        {
            var e = Event.current;

            // 一時停止中はシーンビューの入力を一切奪わず、Unity 標準の操作に委ねる
            bool paused = _settings.ScenePickPaused;

            if (!paused && e.type == EventType.KeyDown && e.keyCode == _settings.ModeToggleHotkey
                && !EditorGUIUtility.editingTextField)
            {
                if (_targetMesh != null) { ToggleAddRemoveMode(sv); e.Use(); }
            }

            if (_analysis != null && _targetTransform != null)
            {
                _overlayRenderer.DrawSeams(_analysis, _targetTransform, _settings, _bakedMesh, _settings.UseBakedMesh);

                // Texture-based mask overlay: encodes both island selection and
                // hand-painted regions, so the scene view always reflects the
                // current state of the preview.
                var overlayTex = _preview?.OverlayTexture;
                if (overlayTex != null)
                {
                    _overlayRenderer.DrawMaskOverlay(
                        _analysis, _targetTransform, _settings, overlayTex,
                        _bakedMesh, _settings.UseBakedMesh);
                }
            }

            if (_analysis != null && !paused
                && e.type == EventType.MouseDown && e.button == 0)
            {
                var pickedIsland = _pickingService.TryPick(e.mousePosition, _analysis);
                if (pickedIsland.HasValue)
                {
                    int islandIdx = pickedIsland.Value;
                    if (_settings.AddMode) _selectedIslands.Add(islandIdx);
                    else _selectedIslands.Remove(islandIdx);
                    _preview?.MarkDirty();
                    sv.Repaint();
                    Log($"[Pick] island={islandIdx} {(_settings.AddMode ? "ADD" : "REMOVE")}");
                }
                e.Use();
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // ─── バージョン管理・アップデートチェック ───────────────────────
        private void StartVersionCheck()
        {
            LoadVersionResultFromSessionState();
            MaskMakerVersion.StartCheckBackgroundTask();
        }

        internal void LoadVersionResultFromSessionState()
        {
            string local  = MaskMakerVersion.Current;
            string latest = SessionState.GetString(MaskMakerVersion.VerCheckLatestKey, string.Empty);
            bool   done   = SessionState.GetBool(MaskMakerVersion.VerCheckDoneKey, false);
            bool   error  = SessionState.GetBool(MaskMakerVersion.VerCheckErrorKey, false);

            DennokoVersionChecker.State state;
            if (!done)
                state = DennokoVersionChecker.State.Checking;
            else if (error || string.IsNullOrEmpty(latest))
                state = DennokoVersionChecker.State.Error;
            else if (DennokoVersionChecker.IsUpdateAvailable(latest, local))
                state = DennokoVersionChecker.State.UpdateAvailable;
            else
                state = DennokoVersionChecker.State.UpToDate;

            _versionResult = new DennokoVersionChecker.Result
            {
                State = state,
                LocalVersion = local,
                LatestVersion = latest,
                Url = SessionState.GetString(MaskMakerVersion.VerCheckUrlKey, string.Empty),
                Message = SessionState.GetString(MaskMakerVersion.VerCheckMessageKey, string.Empty)
            };
            ApplyVersionLabel();
        }

        private void ApplyVersionLabel()
        {
            if (_versionLabel == null) return;

            var r = _versionResult;
            string baseText = "v" + r.LocalVersion;
            string text;
            bool update = false, error = false;
            switch (r.State)
            {
                case DennokoVersionChecker.State.UpdateAvailable:
                    text = baseText + "  " + string.Format(_localization.Get("version_update_available", "更新あり {0}"), r.LatestVersion);
                    update = true;
                    break;
                case DennokoVersionChecker.State.Error:
                    text = baseText + "  " + _localization.Get("version_error", "最新版を取得できません");
                    error = true;
                    break;
                case DennokoVersionChecker.State.Checking:
                    text = baseText + "  " + _localization.Get("version_checking", "確認中...");
                    break;
                default: // UpToDate
                    text = baseText;
                    break;
            }
            _versionLabel.text = text;
            _versionLabel.EnableInClassList("dennoko-version-label--update", update);
            _versionLabel.EnableInClassList("dennoko-version-label--error", error);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Utilities
        // ─────────────────────────────────────────────────────────────────────

        private static void RevealSaved(string path)
        {
            var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
            if (obj != null)
            {
                ProjectWindowUtil.ShowCreatedAsset(obj);
                EditorGUIUtility.PingObject(obj);
                Selection.activeObject = obj;
            }
        }

        private static void Log(string msg)
        {
            try { Directory.CreateDirectory(LogDir); File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss} {msg}\n"); }
            catch { }
            Debug.Log($"[MaskMaker] {msg}");
        }
    }
}
