// UVMaskMakerWindow.cs
// Unity 2022.3+ Editor tool to generate black/white UV mask images based on selected UV islands.
// Refactored with modular UI components following SOLID principles.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Dennoko.UVTools.Core;
using Dennoko.UVTools.Data;
using Dennoko.UVTools.Services;
using Dennoko.UVTools.UI;

namespace Dennoko.UVTools
{
    /// <summary>
    /// Main EditorWindow for UV Mask Maker tool.
    /// Layout:
    ///   [Fixed]   Preview area (zoom/pan, outside scroll)
    ///   [Scroll]  Settings  — Step 1 Target → Step 2 Selection → Step 3 Export → Advanced
    ///   [Fixed]   Status bar
    /// </summary>
    public class UVMaskMakerWindow : EditorWindow
    {
        // ── Layout constants ──────────────────────────────────────────────────
        private const float StatusBarHeight     = 22f;
        private const float SplitterHeight      = 5f;
        private const float PreviewMinHeight    = 80f;
        private const float PreviewMaxHeight    = 700f;
        private float       _previewSplitY      = 280f;
        private bool        _splitterDragging   = false;

        // ── Services ─────────────────────────────────────────────────────────
        private SettingsManager   _settingsManager;
        private LocalizationService _localization;
        private PickingService    _pickingService;
        private OverlayRenderer   _overlayRenderer;
        private WorkCopyService   _workCopyService;
        private UVPreviewDrawer   _previewDrawer;
        private MaskPainter       _maskPainter;
        private IMaskExporter     _exporter;

        // ── UI Drawers (non-selection sections) ───────────────────────────────
        private TargetSectionDrawer  _targetDrawer;
        private MaskImportDrawer     _maskImportDrawer;
        private ExportSectionDrawer  _exportDrawer;
        private AdvancedOptionsDrawer _advancedDrawer;

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
        private Vector2 _scrollPos;
        private double  _lastHotkeyToggleTime = 0;
        private bool    _suppressAutoWorkCopy = false;

        // ── Computed / selection data ─────────────────────────────────────────
        private UVAnalysis      _analysis;
        private HashSet<int>    _selectedIslands = new HashSet<int>();
        private bool            _previewDirty    = true;
        private Mesh            _bakedMesh;

        // ── Asset references ─────────────────────────────────────────────────
        private Texture2D _basePNG;
        private Mesh      _baseVCMesh;

        // ── Status bar ────────────────────────────────────────────────────────
        private enum StatusType { Info, Success, Error }
        private string     _statusMessage   = "";
        private StatusType _statusType      = StatusType.Info;
        private double     _statusResetTime = -1.0;

        // ── Log ───────────────────────────────────────────────────────────────
        private static string LogDir  => Path.Combine(Application.dataPath, "../Logs/MaskMaker");
        private static string LogPath => Path.Combine(LogDir, "MaskMaker.log");

        // ─────────────────────────────────────────────────────────────────────
        [MenuItem("Tools/MaskMaker")]
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
            InitializeDrawers();
            LoadAssetReferences();
            SubscribeToEvents();
            wantsMouseMove = true;
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
            _previewDrawer   = new UVPreviewDrawer();
            _maskPainter     = new MaskPainter(_settings.TextureSize);
            _exporter        = new PngExporter();
        }

        private void InitializeDrawers()
        {
            _targetDrawer   = new TargetSectionDrawer(_localization);
            _maskImportDrawer = new MaskImportDrawer(_localization);
            _exportDrawer   = new ExportSectionDrawer(_localization);
            _advancedDrawer = new AdvancedOptionsDrawer(_localization);

            // Target drawer events
            _targetDrawer.OnTargetChanged      += SetTarget;
            _targetDrawer.OnBakedMeshChanged   += OnBakedMeshOptionChanged;
            _targetDrawer.OnSetupWorkCopyClicked   += SetupWorkCopy;
            _targetDrawer.OnCleanupWorkCopyClicked += CleanupWorkCopy;
            _targetDrawer.OnTargetSubmeshChanged   += idx =>
            {
                _settings.TargetSubmesh = idx;
                _settingsManager.Save(_settings);
                AnalyzeTargetMesh();
            };
            _targetDrawer.OnUVChannelChanged += ch =>
            {
                _settings.UVChannel = ch;
                _settingsManager.Save(_settings);
                AnalyzeTargetMesh();
            };

            // Mask import drawer events
            _maskImportDrawer.OnLoadClicked += LoadMaskImage;

            // Export drawer events
            _exportDrawer.OnSaveClicked           += SaveMaskPNG;
            _exportDrawer.OnResolutionChanged     += size =>
            {
                _settings.TextureSize = size;
                _previewDirty = true;
                _settingsManager.Save(_settings);
                _previewDrawer.InvalidateLabelMap();
            };
            _exportDrawer.OnOutputDirChanged      += dir  => { _settings.OutputDir = dir; _settingsManager.Save(_settings); };
            _exportDrawer.OnSaveInvertedChanged   += val  => { _settings.SaveInvertedToo = val; _settingsManager.Save(_settings); };
            _exportDrawer.OnInvertMaskChanged     += val  => { _settings.InvertMask = val; _previewDirty = true; _settingsManager.Save(_settings); };
            _exportDrawer.OnPixelMarginChanged    += val  => { _settings.PixelMargin = val; _previewDirty = true; _settingsManager.Save(_settings); };
            _exportDrawer.OnUseTextureFolderChanged += val => { _settings.UseTextureFolder = val; _settingsManager.Save(_settings); };

            // Advanced drawer events
            WireAdvancedDrawerEvents();

            // Preview drawer: repaint on view change
            _previewDrawer.OnIslandClicked += OnPreviewIslandClicked;
            _previewDrawer.OnPaintStrokeFinished += OnPaintStrokeFinished;
            _previewDrawer.OnViewChanged   += Repaint;
        }

        private void WireAdvancedDrawerEvents()
        {
            _advancedDrawer.OnOverlayOnTopChanged       += v => { _settings.OverlayOnTop           = v; _settingsManager.Save(_settings); SceneView.RepaintAll(); };
            _advancedDrawer.OnDisableAAChanged          += v => { _settings.DisableAA              = v; _settingsManager.Save(_settings); SceneView.RepaintAll(); };
            _advancedDrawer.OnBackfaceCullChanged       += v => { _settings.BackfaceCull           = v; _settingsManager.Save(_settings); SceneView.RepaintAll(); };
            _advancedDrawer.OnThicknessChanged          += v => { _settings.OverlaySeamThickness   = v; _settingsManager.Save(_settings); SceneView.RepaintAll(); };
            _advancedDrawer.OnDepthOffsetChanged        += v => { _settings.OverlayDepthOffset     = v; _settingsManager.Save(_settings); SceneView.RepaintAll(); };
            _advancedDrawer.OnSeamColorChanged          += c => { _settings.SeamColor              = c; _settingsManager.Save(_settings); SceneView.RepaintAll(); };
            _advancedDrawer.OnSelectedColorChanged      += c => { _settings.SelectedSceneColor     = c; _settingsManager.Save(_settings); SceneView.RepaintAll(); };
            _advancedDrawer.OnPreviewFillColorChanged   += c => { _settings.PreviewFillSelectedColor = c; _previewDirty = true; _settingsManager.Save(_settings); };
            _advancedDrawer.OnOverlayAlphaChanged       += v => { _settings.PreviewOverlayAlpha    = v; _previewDirty = true; _settingsManager.Save(_settings); };
            _advancedDrawer.OnShowIslandPreviewChanged  += v => { _settings.ShowIslandPreview      = v; _settingsManager.Save(_settings); Repaint(); };
            _advancedDrawer.OnPreviewOverlayBaseChanged += v => { _settings.PreviewOverlayBaseTex  = v; _settingsManager.Save(_settings); Repaint(); };
            _advancedDrawer.OnChannelWriteEnabledChanged += v => { _settings.ChannelWriteEnabled   = v; _settingsManager.Save(_settings); };
            _advancedDrawer.OnBasePNGChanged            += tex => { _basePNG = tex; _settingsManager.SetBasePNGPath(tex ? AssetDatabase.GetAssetPath(tex) : ""); };
            _advancedDrawer.OnChannelsChanged           += (r, g, b, a) => { _settings.WriteR = r; _settings.WriteG = g; _settings.WriteB = b; _settings.WriteA = a; _settingsManager.Save(_settings); };
            _advancedDrawer.OnBakeVertexColorClicked    += BakeMaskToVertexColors;
            _advancedDrawer.OnBaseVCMeshChanged         += m => { _baseVCMesh = m; _settingsManager.SetBaseVCMeshPath(m ? AssetDatabase.GetAssetPath(m) : ""); };
            _advancedDrawer.OnOverwriteExistingChanged  += v => { _settings.OverwriteExistingVC   = v; _settingsManager.Save(_settings); };
            _advancedDrawer.OnWorkCopyOffsetChanged     += v => { _settings.WorkCopyOffset        = v; _settingsManager.Save(_settings); };
            _advancedDrawer.OnAutoWorkCopyChanged       += v => { _settings.AutoWorkCopy          = v; _settingsManager.Save(_settings); };
            _advancedDrawer.OnHotkeyChanged             += key => { _settings.ModeToggleHotkey    = key; _settingsManager.Save(_settings); };
            _advancedDrawer.OnUseEnglishChanged         += OnLanguageChanged;
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
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorSceneManager.sceneSaving += OnSceneSaving;
            EditorApplication.update += EditorUpdate;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorSceneManager.sceneSaving -= OnSceneSaving;
            EditorApplication.update -= EditorUpdate;

            if (_settingsManager != null && _settings != null) _settingsManager.Save(_settings);
            _pickingService?.Dispose();

            // Restore original visibility if needed
            if (_isWorkCopy && _sourceTargetGO != null)
            {
                _sourceTargetGO.SetActive(true);
            }

            if (_previewDrawer != null)
            {
                _previewDrawer.OnIslandClicked -= OnPreviewIslandClicked;
                _previewDrawer.OnViewChanged   -= Repaint;
                _previewDrawer.Dispose();
            }
            if (_advancedDrawer != null) _advancedDrawer.OnUseEnglishChanged -= OnLanguageChanged;

            if (_bakedMesh != null) { try { DestroyImmediate(_bakedMesh); } catch { } _bakedMesh = null; }

            Log("[OnDisable] Window closed");
        }

        private void OnPlayModeStateChanged(PlayModeStateChange state) { }
        private void OnSceneSaving(UnityEngine.SceneManagement.Scene scene, string path) => _pickingService?.Cleanup();

        /// <summary>
        /// Called every editor frame. Forces continuous repainting during active paint strokes
        /// so the preview texture updates in real-time rather than on mouse release.
        /// </summary>
        private void EditorUpdate()
        {
            if (_previewDrawer != null && _previewDrawer.IsPainting)
            {
                Repaint();
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // OnGUI — 3-zone layout
        // ─────────────────────────────────────────────────────────────────────

        private void OnGUI()
        {
            // Status auto-reset
            if (_statusResetTime > 0 && EditorApplication.timeSinceStartup > _statusResetTime)
            {
                _statusMessage   = _localization.Get("status_ready", "Ready");
                _statusType      = StatusType.Info;
                _statusResetTime = -1.0;
                Repaint();
            }

            // Initialize design system (textures / styles)
            EditorUIStyles.Initialize();

            HandleHotkey();

            float w = position.width;
            float h = position.height;
            float settingsTop = _previewSplitY + SplitterHeight;
            float settingsH   = Mathf.Max(h - settingsTop - StatusBarHeight, 50f);

            // ── Window background ────────────────────────────────────────────
            EditorGUI.DrawRect(new Rect(0, 0, w, h), EditorUIStyles.Surface0);

            // ── Zone 1: Preview (fixed) ───────────────────────────────────────
            GUILayout.BeginArea(new Rect(0, 0, w, _previewSplitY));
            DrawPreviewZone(w, _previewSplitY);
            GUILayout.EndArea();

            // ── Splitter ──────────────────────────────────────────────────────
            HandleSplitter(new Rect(0, _previewSplitY, w, SplitterHeight), h);

            // ── Zone 2: Settings (scrollable) ────────────────────────────────
            GUILayout.BeginArea(new Rect(0, settingsTop, w, settingsH));
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos,
                GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            DrawSettingsContent();
            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();

            // ── Zone 3: Status bar (fixed) ───────────────────────────────────
            GUILayout.BeginArea(new Rect(0, h - StatusBarHeight, w, StatusBarHeight));
            DrawStatusBarZone();
            GUILayout.EndArea();
        }

        // ─────────────────────────────────────────────────────────────────────
        // Zone 1: Preview
        // ─────────────────────────────────────────────────────────────────────

        private void DrawPreviewZone(float w, float h)
        {
            const float headerH = 26f;
            const float footerH = 26f;
            const float pad     = 4f;

            // ── Header: PREVIEW title + Undo/Redo/Clear + zoom + reset ──
            using (new EditorGUILayout.HorizontalScope(EditorUIStyles.ToolbarStyle, GUILayout.Height(headerH)))
            {
                GUILayout.Space(4);
                GUILayout.Label("PREVIEW", EditorUIStyles.SectionHeaderSmallStyle);

                GUILayout.Space(8);

                // Paint action buttons (always visible, disabled when no paint data)
                EditorGUI.BeginDisabledGroup(!_maskPainter.HasUndo);
                if (GUILayout.Button(_localization.Get("tool_undo", "Undo"), EditorStyles.toolbarButton, GUILayout.Width(48)))
                {
                    _maskPainter.Undo();
                    _previewDirty = true;
                }
                EditorGUI.EndDisabledGroup();

                EditorGUI.BeginDisabledGroup(!_maskPainter.HasRedo);
                if (GUILayout.Button(_localization.Get("tool_redo", "Redo"), EditorStyles.toolbarButton, GUILayout.Width(48)))
                {
                    _maskPainter.Redo();
                    _previewDirty = true;
                }
                EditorGUI.EndDisabledGroup();

                if (GUILayout.Button(_localization.Get("tool_clear", "Clear"), EditorStyles.toolbarButton, GUILayout.Width(48)))
                {
                    if (EditorUtility.DisplayDialog(
                        _localization.Get("tool_clear", "Clear"),
                        _localization.Get("tool_clear_confirm", "Clear all paint?"),
                        "OK", "Cancel"))
                    {
                        _maskPainter.Clear();
                        _previewDirty = true;
                    }
                }

                GUILayout.FlexibleSpace();

                // Zoom indicator
                if (_analysis != null)
                {
                    GUILayout.Label(
                        $"{Mathf.RoundToInt(_previewDrawer.ZoomLevel * 100)}%",
                        EditorStyles.miniLabel,
                        GUILayout.Width(42));
                }

                // Reset view button
                if (GUILayout.Button(
                    new GUIContent("reset", _localization.Get("preview_reset_view", "ビューをリセット")),
                    EditorStyles.toolbarButton))
                {
                    _previewDrawer.ResetView();
                }
                GUILayout.Space(4);
            }

            // ── Preview content ──
            var contentRect = new Rect(pad, headerH + pad, w - pad * 2f, h - headerH - footerH - pad * 2f);

            if (_previewDirty)
            {
                _previewDrawer.MarkDirty();
                _previewDirty = false;
            }

            _previewDrawer.Draw(
                contentRect,
                _analysis,
                _selectedIslands,
                _settings,
                _settings.PreviewOverlayBaseTex ? GetBaseTexture() : null,
                _maskPainter,
                _localization);

            // ── Footer: Mode toggle + Brush Size + Eraser ──
            DrawPaintFooter(w, h, footerH);
        }

        private void DrawPaintFooter(float w, float h, float footerH)
        {
            var r = new Rect(0, h - footerH, w, footerH);
            GUILayout.BeginArea(r);
            using (new EditorGUILayout.HorizontalScope(EditorUIStyles.ToolbarStyle, GUILayout.Height(footerH)))
            {
                GUILayout.Space(4);

                // Tool Mode Toggle
                int toolMode = _settings.IsPaintMode ? 1 : 0;
                GUIContent[] modes = new GUIContent[] {
                    new GUIContent(_localization.Get("tool_select", "Select")),
                    new GUIContent(_localization.Get("tool_paint", "Paint"))
                };
                int newMode = GUILayout.Toolbar(toolMode, modes, GUILayout.Width(120));
                if (newMode != toolMode)
                {
                    _settings.IsPaintMode = (newMode == 1);
                    _settingsManager.Save(_settings);
                }

                GUILayout.Space(10);
                
                EditorGUI.BeginDisabledGroup(!_settings.IsPaintMode);

                // Brush Size
                GUILayout.Label(_localization.Get("brush_size", "Size"), GUILayout.Width(35));
                int newSize = (int)GUILayout.HorizontalSlider(_settings.BrushSize, 1, 100, GUILayout.Width(80));
                if (newSize != _settings.BrushSize)
                {
                    _settings.BrushSize = newSize;
                    _settingsManager.Save(_settings);
                }

                GUILayout.Space(10);

                // Eraser Toggle
                var oldBg = GUI.backgroundColor;
                if (_settings.EraseMode) GUI.backgroundColor = EditorUIStyles.AccentBlue;

                bool newEraser = GUILayout.Toggle(_settings.EraseMode, _localization.Get("tool_eraser", "Eraser"), EditorStyles.toolbarButton, GUILayout.Width(60));
                
                GUI.backgroundColor = oldBg;

                if (newEraser != _settings.EraseMode)
                {
                    _settings.EraseMode = newEraser;
                    _settingsManager.Save(_settings);
                }
                
                EditorGUI.EndDisabledGroup();
                
                GUILayout.FlexibleSpace();
                GUILayout.Space(4);
            }
            GUILayout.EndArea();
        }

        // ─────────────────────────────────────────────────────────────────────
        // Zone 2: Settings content
        // ─────────────────────────────────────────────────────────────────────

        private void DrawSettingsContent()
        {
            EditorGUILayout.Space(EditorUIStyles.CardSpacing);

            // ── STEP 1: Target ────────────────────────────────────────────────
            _targetDrawer.Draw(_targetGO, _targetRenderer, _settings, _isWorkCopy);

            EditorGUILayout.Space(EditorUIStyles.CardSpacing);

            // ── Mask Image Import ─────────────────────────────────────────────
            _maskImportDrawer.Draw();

            EditorGUILayout.Space(EditorUIStyles.CardSpacing);

            // ── STEP 2: Island Selection (mode + actions + count, combined) ───
            DrawSelectionSection();

            EditorGUILayout.Space(EditorUIStyles.CardSpacing);

            // ── STEP 3: Export ────────────────────────────────────────────────
            {
                string fileName = _targetGO != null ? _targetGO.name : "uv_mask";
                if (_isWorkCopy && fileName.EndsWith(" [WorkCopy]"))
                    fileName = fileName.Replace(" [WorkCopy]", "");
                _exportDrawer.FileName = fileName + "_mask";
                _exportDrawer.Draw(_settings, _analysis != null, GetBaseTexturePath());
            }

            // ── Advanced (collapsible) ────────────────────────────────────────
            _advancedDrawer.DrawOverlaySection(_settings, GetBaseTexture());
            _advancedDrawer.DrawChannelWriteSection(_settings, _basePNG);
            _advancedDrawer.DrawVertexColorSection(_settings, _baseVCMesh, _analysis != null);
            _advancedDrawer.DrawPreferencesSection(_settings);

            EditorGUILayout.Space(EditorUIStyles.CardSpacing);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Selection section (Mode toggle + action buttons in a single card)
        // ─────────────────────────────────────────────────────────────────────

        private void DrawSelectionSection()
        {
            EditorUIStyles.BeginCard(_localization.Get("island_selection", "アイランド選択"));

            GUI.enabled = _analysis != null;

            // Add / Remove mode toolbar (centred)
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                int toolbar = GUILayout.Toolbar(
                    _settings.AddMode ? 0 : 1,
                    new[]
                    {
                        new GUIContent(_localization["mode_add"],    _localization["mode_add_tooltip"]),
                        new GUIContent(_localization["mode_remove"], _localization["mode_remove_tooltip"])
                    },
                    GUILayout.Width(220));
                bool newMode = toolbar == 0;
                if (newMode != _settings.AddMode)
                {
                    _settings.AddMode = newMode;
                    _settingsManager.Save(_settings);
                }
                GUILayout.FlexibleSpace();
            }

            EditorGUILayout.Space(EditorUIStyles.InnerSpacing);

            // Action buttons
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(
                    new GUIContent(_localization["invert"],          _localization["invert_tooltip"]),
                    EditorUIStyles.SmallButtonStyle))
                    InvertSelection();

                if (GUILayout.Button(
                    new GUIContent(_localization["select_all"],      _localization["select_all_tooltip"]),
                    EditorUIStyles.SmallButtonStyle))
                    SelectAll();

                if (GUILayout.Button(
                    new GUIContent(_localization["clear_selection"], _localization["clear_selection_tooltip"]),
                    EditorUIStyles.SmallButtonStyle))
                    ClearSelection();
            }

            GUI.enabled = true;
            EditorUIStyles.EndCard();
        }

        // ─────────────────────────────────────────────────────────────────────
        // Splitter (resize handle between preview and settings zones)
        // ─────────────────────────────────────────────────────────────────────

        private void HandleSplitter(Rect splitterRect, float windowHeight)
        {
            // Background fill
            EditorGUI.DrawRect(splitterRect, EditorUIStyles.Surface2);
            // Center indicator line
            var lineRect = new Rect(
                splitterRect.x,
                splitterRect.y + Mathf.Floor(SplitterHeight * 0.5f) - 1f,
                splitterRect.width, 2f);
            EditorGUI.DrawRect(lineRect, EditorUIStyles.Outline);

            // Resize cursor
            EditorGUIUtility.AddCursorRect(splitterRect, MouseCursor.ResizeVertical);

            var e = Event.current;
            switch (e.type)
            {
                case EventType.MouseDown:
                    if (e.button == 0 && splitterRect.Contains(e.mousePosition))
                    {
                        _splitterDragging = true;
                        e.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (_splitterDragging)
                    {
                        float maxY = windowHeight - StatusBarHeight - SplitterHeight - 50f;
                        _previewSplitY = Mathf.Clamp(e.mousePosition.y, PreviewMinHeight, Mathf.Min(maxY, PreviewMaxHeight));
                        e.Use();
                        Repaint();
                    }
                    break;
                case EventType.MouseUp:
                    if (_splitterDragging && e.button == 0)
                    {
                        _splitterDragging = false;
                        e.Use();
                    }
                    break;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Zone 3: Status bar
        // ─────────────────────────────────────────────────────────────────────

        private void DrawStatusBarZone()
        {
            string msg = string.IsNullOrEmpty(_statusMessage)
                ? _localization.Get("status_ready", "Ready")
                : _statusMessage;
            GUILayout.Box(msg, GetStatusStyle(_statusType),
                GUILayout.ExpandWidth(true), GUILayout.Height(StatusBarHeight));
        }

        private GUIStyle GetStatusStyle(StatusType type) => type switch
        {
            StatusType.Success => EditorUIStyles.StatusSuccessStyle,
            StatusType.Error   => EditorUIStyles.StatusErrorStyle,
            _                  => EditorUIStyles.StatusInfoStyle,
        };

        private void SetStatus(string message, StatusType type, double autoResetSecs = 4.0)
        {
            _statusMessage   = message;
            _statusType      = type;
            _statusResetTime = type == StatusType.Info
                ? -1.0
                : EditorApplication.timeSinceStartup + autoResetSecs;
            Repaint();
        }

        // ─────────────────────────────────────────────────────────────────────
        // Hotkey
        // ─────────────────────────────────────────────────────────────────────

        private void HandleHotkey()
        {
            var e = Event.current;
            if (e != null && e.type == EventType.KeyDown
                && e.keyCode == _settings.ModeToggleHotkey
                && !EditorGUIUtility.editingTextField)
            {
                if (_targetMesh != null) { ToggleAddRemoveMode(null); e.Use(); }
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
            _previewDirty = true;
            _pickingService.Cleanup();
            _overlayRenderer.InvalidateCache();
            _previewDrawer.InvalidateLabelMap();

            if (_bakedMesh != null) { try { DestroyImmediate(_bakedMesh); } catch { } _bakedMesh = null; }
            if (_targetGO == null)
            {
                SetStatus(_localization.Get("status_no_target", "ターゲットを設定してください"), StatusType.Info);
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
                EditorUtility.DisplayDialog(
                    _localization["dialog_no_mesh"],
                    _localization["dialog_no_mesh_msg"],
                    _localization["ok"]);
                return;
            }

            if (!TryFixReadWrite(_targetMesh))
            {
                _targetGO = null; _targetRenderer = null; _targetMesh = null;
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
                EditorUtility.DisplayDialog(
                    _localization["dialog_no_target"],
                    _localization["dialog_no_target_msg"],
                    _localization["ok"]);
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
            _previewDirty = true;
            _overlayRenderer.InvalidateCache();
            _previewDrawer.InvalidateLabelMap();
            BakeCurrentPoseAuto();
            Repaint();
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
            _previewDirty = true;
        }

        private void SelectAll()
        {
            if (_analysis == null) return;
            _selectedIslands = new HashSet<int>(Enumerable.Range(0, _analysis.Islands.Count));
            _previewDirty = true;
        }

        private void ClearSelection()
        {
            _selectedIslands.Clear();
            _previewDirty = true;
        }

        private void OnPreviewIslandClicked(int islandIdx)
        {
            if (islandIdx < 0 || _analysis == null) return;
            if (_selectedIslands.Contains(islandIdx)) _selectedIslands.Remove(islandIdx);
            else _selectedIslands.Add(islandIdx);
            _previewDirty = true;
            Repaint();
            SceneView.RepaintAll();
            Log($"[PreviewClick] island={islandIdx} TOGGLE → {(_selectedIslands.Contains(islandIdx) ? "SELECTED" : "DESELECTED")}");
        }

        private void OnPaintStrokeFinished()
        {
            _settingsManager.Save(_settings); // save brush size or state if needed
            Repaint();
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

            var readable = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
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

            DestroyImmediate(readable);

            _maskPainter.MarkAllTilesDirty();
            _previewDirty = true;
            Repaint();

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
            Repaint();
            SceneView.RepaintAll();
        }

        private void OnLanguageChanged(bool useEnglish)
        {
            _settings.UseEnglish = useEnglish;
            _settings.Language   = useEnglish ? "en" : "ja";
            _localization.LoadLanguage(_settings.Language);
            Repaint(); SceneView.RepaintAll();
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

            string fileName = _exportDrawer.FileName;
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
        // Scene View
        // ─────────────────────────────────────────────────────────────────────

        private void OnSceneGUI(SceneView sv)
        {
            var e = Event.current;
            if (e.type == EventType.KeyDown && e.keyCode == _settings.ModeToggleHotkey
                && !EditorGUIUtility.editingTextField)
            {
                if (_targetMesh != null) { ToggleAddRemoveMode(sv); e.Use(); }
            }

            if (_analysis != null && _targetTransform != null)
            {
                _overlayRenderer.DrawSeams(_analysis, _targetTransform, _settings, _bakedMesh, _settings.UseBakedMesh);
                _overlayRenderer.DrawSelectedIslands(_analysis, _selectedIslands, _targetTransform, _settings, _targetRenderer, sv, _bakedMesh, _settings.UseBakedMesh);
            }

            if (_analysis != null && e.type == EventType.MouseDown && e.button == 0)
            {
                var pickedIsland = _pickingService.TryPick(e.mousePosition, _analysis);
                if (pickedIsland.HasValue)
                {
                    int islandIdx = pickedIsland.Value;
                    if (_settings.AddMode) _selectedIslands.Add(islandIdx);
                    else _selectedIslands.Remove(islandIdx);
                    _previewDirty = true;
                    Repaint();
                    sv.Repaint();
                    Log($"[Pick] island={islandIdx} {(_settings.AddMode ? "ADD" : "REMOVE")}");
                }
                e.Use();
            }
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
