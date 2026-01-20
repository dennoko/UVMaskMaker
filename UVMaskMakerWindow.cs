// UVMaskMakerWindow.cs
// Unity 2022.3+ Editor tool to generate black/white UV mask images based on selected UV islands.
// Refactored to use separated service classes following SOLID principles.

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
    /// EditorWindow that lets you select a MeshRenderer/SkinnedMeshRenderer from the scene,
    /// detect UV seams (outer UV borders), select UV islands with a hotkey+click,
    /// preview the mask, and export a black/white PNG.
    /// </summary>
    public class UVMaskMakerWindow : EditorWindow
    {
        // Services
        private SettingsManager _settingsManager;
        private LocalizationService _localization;
        private PickingService _pickingService;
        private OverlayRenderer _overlayRenderer;
        private UVPreviewDrawer _previewDrawer;
        private IMaskExporter _exporter;

        // Settings
        private MaskSettings _settings;

        // Target selection and mesh refs
        private GameObject _targetGO;
        private Renderer _targetRenderer;
        private Mesh _targetMesh;
        private Transform _targetTransform;

        // UI state
        private Vector2 _scrollPos;
        private string _fileName = "uv_mask";

        // Depth offset throttling
        private float _pendingOverlayDepthOffset = 0f;
        private double _nextDepthCommitTime = 0;
        private const double DepthCommitIntervalSec = 0.08;
        private double _lastHotkeyToggleTime = 0;

        // Computed data
        private UVAnalysis _analysis;
        private HashSet<int> _selectedIslands = new HashSet<int>();
        private bool _previewDirty = true;

        // Baked mesh (for SkinnedMeshRenderer)
        private Mesh _bakedMesh;

        // Asset references
        private Texture2D _basePNG;
        private Mesh _baseVCMesh;

        // Log file paths
        private static string LogDir => Path.Combine(Application.dataPath, "../Logs/UVMaskMaker");
        private static string LogPath => Path.Combine(LogDir, "UVMaskMaker.log");

        [MenuItem("Tools/UV Mask Maker")]
        public static void ShowWindow()
        {
            var wnd = GetWindow<UVMaskMakerWindow>();
            wnd.titleContent = new GUIContent(LocalizationService.Instance["window_title"]);
            wnd.minSize = new Vector2(480, 520);
            wnd.Show();
        }

        private void OnEnable()
        {
            try
            {
                Directory.CreateDirectory(LogDir);
                Log($"[OnEnable] Window opened at {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            }
            catch { /* ignore */ }

            // Initialize services
            _settingsManager = new SettingsManager();
            _settings = _settingsManager.Load();
            _localization = LocalizationService.Instance;
            _localization.LoadLanguage(_settings.Language);
            _pickingService = new PickingService();
            _overlayRenderer = new OverlayRenderer();
            _previewDrawer = new UVPreviewDrawer();
            _previewDrawer.OnIslandClicked += OnPreviewIslandClicked;
            _exporter = new PngExporter();

            _pendingOverlayDepthOffset = _settings.OverlayDepthOffset;

            // Load asset references
            var basePngPath = _settingsManager.GetBasePNGPath();
            if (!string.IsNullOrEmpty(basePngPath)) _basePNG = AssetDatabase.LoadAssetAtPath<Texture2D>(basePngPath);
            var baseVCPath = _settingsManager.GetBaseVCMeshPath();
            if (!string.IsNullOrEmpty(baseVCPath)) _baseVCMesh = AssetDatabase.LoadAssetAtPath<Mesh>(baseVCPath);

            SceneView.duringSceneGui += OnSceneGUI;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorSceneManager.sceneSaving += OnSceneSaving;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorSceneManager.sceneSaving -= OnSceneSaving;

            _pickingService?.Dispose();
            if (_previewDrawer != null)
            {
                _previewDrawer.OnIslandClicked -= OnPreviewIslandClicked;
                _previewDrawer.Dispose();
            }

            if (_bakedMesh != null)
            {
                try { DestroyImmediate(_bakedMesh); } catch { }
                _bakedMesh = null;
            }

            Log("[OnDisable] Window closed");
        }

        private void OnPlayModeStateChanged(PlayModeStateChange state) { }

        private void OnSceneSaving(UnityEngine.SceneManagement.Scene scene, string path)
        {
            _pickingService?.Cleanup();
        }

        private void OnGUI()
        {
            // Handle hotkey for mode toggle
            var e = Event.current;
            if (e != null && e.type == EventType.KeyDown && e.keyCode == _settings.ModeToggleHotkey && !EditorGUIUtility.editingTextField)
            {
                ToggleAddRemoveMode(null);
                e.Use();
            }

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);
            EditorGUILayout.Space();

            DrawTargetSection();
            DrawModeSection();
            DrawSceneOverlaySection();
            DrawSelectionButtons();
            DrawPreviewSection();
            DrawExportSection();
            DrawHelpBox();

            EditorGUILayout.EndScrollView();
        }

        #region UI Sections

        private void DrawTargetSection()
        {
            EditorGUILayout.LabelField(_localization["target_model"], EditorStyles.boldLabel);

            // Drag-and-drop area
            var dropRect = GUILayoutUtility.GetRect(0, 40, GUILayout.ExpandWidth(true));
            GUI.Box(dropRect, _localization["drag_drop_hint"], EditorStyles.helpBox);
            HandleDragAndDrop(dropRect);

            // Object field + Clear button
            using (new EditorGUILayout.HorizontalScope())
            {
                var newObj = EditorGUILayout.ObjectField(
                    new GUIContent("Object", _localization["object_field_tooltip"]),
                    _targetGO, typeof(GameObject), true) as GameObject;
                if (newObj != _targetGO) SetTarget(newObj);

                GUI.enabled = _targetGO != null;
                if (GUILayout.Button(new GUIContent(_localization["clear_button"], _localization["clear_button_tooltip"]), GUILayout.Width(60)))
                {
                    SetTarget(null);
                }
                GUI.enabled = true;
            }

            // Bake controls for SkinnedMeshRenderer
            if (_targetRenderer is SkinnedMeshRenderer)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    bool useBaked = EditorGUILayout.ToggleLeft(
                        new GUIContent(_localization["use_baked_mesh"], _localization["use_baked_mesh_tooltip"]),
                        _settings.UseBakedMesh, GUILayout.Width(140));
                    if (useBaked != _settings.UseBakedMesh)
                    {
                        _settings.UseBakedMesh = useBaked;
                        _settingsManager.Save(_settings);
                        _overlayRenderer.InvalidateCache();
                        _pickingService.UpdateMesh(_bakedMesh, _settings.UseBakedMesh);
                        SceneView.RepaintAll();
                    }
                }
            }
        }

        private void DrawModeSection()
        {
            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(new GUIContent(_localization["mode_label"], _localization["mode_tooltip"]), GUILayout.Width(50));
                int toolbar = GUILayout.Toolbar(_settings.AddMode ? 0 : 1, new[] {
                    new GUIContent(_localization["mode_add"], _localization["mode_add_tooltip"]),
                    new GUIContent(_localization["mode_remove"], _localization["mode_remove_tooltip"])
                });
                _settings.AddMode = toolbar == 0;
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField(new GUIContent(_localization["toggle_hotkey_label"], _localization["toggle_hotkey_tooltip"]), GUILayout.Width(95));
                var toggleStr = EditorGUILayout.TextField(_settings.ModeToggleHotkey.ToString(), GUILayout.Width(60));
                if (Enum.TryParse<KeyCode>(toggleStr, out var toggleParsed) && toggleParsed != _settings.ModeToggleHotkey)
                {
                    _settings.ModeToggleHotkey = toggleParsed;
                    _settingsManager.Save(_settings);
                }
            }
        }

        private void DrawSceneOverlaySection()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(_localization["scene_overlay"], EditorStyles.boldLabel);

            // UV Channel
            using (new EditorGUILayout.HorizontalScope())
            {
                int newUv = EditorGUILayout.Popup(
                    new GUIContent(_localization["uv_channel"], _localization["uv_channel_tooltip"]),
                    _settings.UVChannel, Enumerable.Range(0, 8).Select(i => $"UV{i}").ToArray());
                if (newUv != _settings.UVChannel)
                {
                    _settings.UVChannel = newUv;
                    _settingsManager.Save(_settings);
                    AnalyzeTargetMesh();
                }
                GUILayout.FlexibleSpace();
            }

            // X-Ray mode
            using (new EditorGUILayout.HorizontalScope())
            {
                bool onTop = EditorGUILayout.ToggleLeft(new GUIContent(_localization["overlay_on_top"], _localization["overlay_on_top_tooltip"]), _settings.OverlayOnTop);
                if (onTop != _settings.OverlayOnTop)
                {
                    _settings.OverlayOnTop = onTop;
                    _settingsManager.Save(_settings);
                    SceneView.RepaintAll();
                }
                GUILayout.FlexibleSpace();
            }

            // AA and backface cull
            using (new EditorGUILayout.HorizontalScope())
            {
                bool na = EditorGUILayout.ToggleLeft(new GUIContent(_localization["disable_aa"], _localization["disable_aa_tooltip"]), _settings.DisableAA);
                if (na != _settings.DisableAA)
                {
                    _settings.DisableAA = na;
                    _settingsManager.Save(_settings);
                    SceneView.RepaintAll();
                }
                bool bfc = EditorGUILayout.ToggleLeft(new GUIContent(_localization["backface_cull"], _localization["backface_cull_tooltip"]), _settings.BackfaceCull);
                if (bfc != _settings.BackfaceCull)
                {
                    _settings.BackfaceCull = bfc;
                    _settingsManager.Save(_settings);
                    SceneView.RepaintAll();
                }
                GUILayout.FlexibleSpace();
            }

            // Advanced options foldout
            _settings.AdvancedOptionsFoldout = EditorGUILayout.Foldout(_settings.AdvancedOptionsFoldout, new GUIContent(_localization["advanced_options"], _localization["advanced_options_tooltip"]), true);
            if (_settings.AdvancedOptionsFoldout)
            {
                DrawAdvancedOptions();
            }

            // Color options foldout
            _settings.ColorOptionsFoldout = EditorGUILayout.Foldout(_settings.ColorOptionsFoldout, new GUIContent(_localization["color_options"], _localization["color_options_tooltip"]), true);
            if (_settings.ColorOptionsFoldout)
            {
                DrawColorOptions();
            }
        }

        private void DrawAdvancedOptions()
        {
            using (new EditorGUI.IndentLevelScope())
            {
                float th = EditorGUILayout.Slider(new GUIContent(_localization["thickness"], _localization["thickness_tooltip"]), _settings.OverlaySeamThickness, 1f, 8f);
                if (!Mathf.Approximately(th, _settings.OverlaySeamThickness))
                {
                    _settings.OverlaySeamThickness = th;
                    _settingsManager.Save(_settings);
                    SceneView.RepaintAll();
                }

                float pendingMm = _pendingOverlayDepthOffset * 1000f;
                float newPendingMm = EditorGUILayout.Slider(new GUIContent(_localization["depth_offset"], _localization["depth_offset_tooltip"]), pendingMm, 0f, 20f);
                if (!Mathf.Approximately(newPendingMm, pendingMm))
                {
                    _pendingOverlayDepthOffset = Mathf.Clamp(newPendingMm, 0f, 20f) / 1000f;
                }

                double now = EditorApplication.timeSinceStartup;
                bool mouseUp = Event.current.type == EventType.MouseUp;
                bool timeToCommit = now >= _nextDepthCommitTime;
                if ((timeToCommit || mouseUp) && !Mathf.Approximately(_pendingOverlayDepthOffset, _settings.OverlayDepthOffset))
                {
                    _settings.OverlayDepthOffset = _pendingOverlayDepthOffset;
                    _settingsManager.Save(_settings);
                    SceneView.RepaintAll();
                    _nextDepthCommitTime = now + DepthCommitIntervalSec;
                }
            }
        }

        private void DrawColorOptions()
        {
            using (new EditorGUI.IndentLevelScope())
            {
                var selCol = EditorGUILayout.ColorField(new GUIContent(_localization["selected_islands_color"], _localization["selected_islands_color_tooltip"]), _settings.SelectedSceneColor);
                if (selCol != _settings.SelectedSceneColor)
                {
                    _settings.SelectedSceneColor = selCol;
                    _settingsManager.Save(_settings);
                    SceneView.RepaintAll();
                }

                var seamCol = EditorGUILayout.ColorField(new GUIContent(_localization["seam_color"], _localization["seam_color_tooltip"]), _settings.SeamColor);
                if (seamCol != _settings.SeamColor)
                {
                    _settings.SeamColor = seamCol;
                    _settingsManager.Save(_settings);
                    SceneView.RepaintAll();
                }

                var pfill = EditorGUILayout.ColorField(new GUIContent(_localization["preview_fill_color"], _localization["preview_fill_color_tooltip"]), _settings.PreviewFillSelectedColor);
                if (pfill != _settings.PreviewFillSelectedColor)
                {
                    _settings.PreviewFillSelectedColor = pfill;
                    _settingsManager.Save(_settings);
                    _previewDirty = true;
                    Repaint();
                }

                float a = EditorGUILayout.Slider(new GUIContent(_localization["overlay_alpha"], _localization["overlay_alpha_tooltip"]), _settings.PreviewOverlayAlpha, 0f, 1f);
                if (!Mathf.Approximately(a, _settings.PreviewOverlayAlpha))
                {
                    _settings.PreviewOverlayAlpha = a;
                    _settingsManager.Save(_settings);
                    _previewDirty = true;
                    Repaint();
                }
            }
        }

        private void DrawSelectionButtons()
        {
            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent(_localization["analyze_uvs"], _localization["analyze_uvs_tooltip"]), GUILayout.Height(24)))
                {
                    AnalyzeTargetMesh();
                }
                if (GUILayout.Button(new GUIContent(_localization["invert"], _localization["invert_tooltip"]), GUILayout.Height(24)))
                {
                    InvertSelection();
                }
                if (GUILayout.Button(new GUIContent(_localization["select_all"], _localization["select_all_tooltip"]), GUILayout.Height(24)))
                {
                    SelectAll();
                }
                if (GUILayout.Button(new GUIContent(_localization["clear_selection"], _localization["clear_selection_tooltip"]), GUILayout.Height(24)))
                {
                    ClearSelection();
                }
            }
        }

        private void DrawPreviewSection()
        {
            EditorGUILayout.Space();

            // Preview options
            using (new EditorGUILayout.HorizontalScope())
            {
                var baseTex = GetBaseTexture();
                var hasBase = baseTex != null;
                using (new EditorGUI.DisabledScope(!hasBase))
                {
                    bool overlay = EditorGUILayout.ToggleLeft(new GUIContent(_localization["preview_overlay_base"], _localization["preview_overlay_base_tooltip"]), _settings.PreviewOverlayBaseTex);
                    if (overlay != _settings.PreviewOverlayBaseTex)
                    {
                        _settings.PreviewOverlayBaseTex = overlay;
                        _settingsManager.Save(_settings);
                        Repaint();
                    }
                }
                if (!hasBase) EditorGUILayout.LabelField(_localization["no_base_texture"], GUILayout.Width(140));
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                bool dbg = EditorGUILayout.ToggleLeft(new GUIContent(_localization["show_island_preview"], _localization["show_island_preview_tooltip"]), _settings.ShowIslandPreview);
                if (dbg != _settings.ShowIslandPreview)
                {
                    _settings.ShowIslandPreview = dbg;
                    _settingsManager.Save(_settings);
                    Repaint();
                }
                bool inv = EditorGUILayout.ToggleLeft(new GUIContent(_localization["invert_mask"], _localization["invert_mask_tooltip"]), _settings.InvertMask);
                if (inv != _settings.InvertMask)
                {
                    _settings.InvertMask = inv;
                    _settingsManager.Save(_settings);
                    _previewDirty = true;
                }
                GUILayout.FlexibleSpace();
            }

            // Draw preview using UVPreviewDrawer
            if (_previewDirty)
            {
                _previewDrawer.MarkDirty();
                _previewDirty = false;
            }
            _previewDrawer.Draw(_analysis, _selectedIslands, _settings, _settings.PreviewOverlayBaseTex ? GetBaseTexture() : null, _localization);

            // Pixel margin
            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                int newMargin = EditorGUILayout.IntSlider(new GUIContent(_localization["pixel_margin"], _localization["pixel_margin_tooltip"]), _settings.PixelMargin, 0, 16);
                if (newMargin != _settings.PixelMargin)
                {
                    _settings.PixelMargin = newMargin;
                    _settingsManager.Save(_settings);
                    _previewDirty = true;
                }
            }
        }

        private void DrawExportSection()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(_localization["export_section"], EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(new GUIContent(_localization["resolution"], _localization["resolution_tooltip"]), GUILayout.Width(100));
                int newSize = EditorGUILayout.IntPopup(_settings.TextureSize, new[] { "512", "1024", "2048", "4096" }, new[] { 512, 1024, 2048, 4096 }, GUILayout.Width(100));
                if (newSize != _settings.TextureSize)
                {
                    _settings.TextureSize = newSize;
                    _previewDirty = true;
                    _settingsManager.Save(_settings);
                }
                _fileName = EditorGUILayout.TextField(new GUIContent(_localization["file_name"], _localization["file_name_tooltip"]), _fileName);
            }

            // Channel-wise export foldout
            _settings.ChannelWriteFoldout = EditorGUILayout.Foldout(_settings.ChannelWriteFoldout, new GUIContent(_localization["channel_write"], _localization["channel_write_tooltip"]), true);
            if (_settings.ChannelWriteFoldout)
            {
                DrawChannelWriteOptions();
            }

            // Output folder
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(new GUIContent(_localization["output_folder"], _localization["output_folder_tooltip"]), GUILayout.Width(90));
                EditorGUILayout.TextField(_settings.OutputDir);
                if (GUILayout.Button(new GUIContent(_localization["browse"], _localization["browse_tooltip"]), GUILayout.Width(90)))
                {
                    var selected = EditorUtility.OpenFolderPanel(_localization["folder_dialog_select"], _settings.OutputDir, "");
                    if (!string.IsNullOrEmpty(selected))
                    {
                        if (selected.Contains("Assets"))
                        {
                            var projPath = Path.GetFullPath(Application.dataPath + "/..");
                            var rel = MakeProjectRelative(selected, projPath);
                            if (!string.IsNullOrEmpty(rel))
                            {
                                _settings.OutputDir = rel.Replace('\\', '/');
                                _settingsManager.Save(_settings);
                            }
                        }
                        else
                        {
                            EditorUtility.DisplayDialog(_localization["dialog_invalid_folder"], _localization["dialog_invalid_folder_msg"], _localization["ok"]);
                        }
                    }
                }
            }

            // Save inverted too option
            using (new EditorGUILayout.HorizontalScope())
            {
                bool saveInv = EditorGUILayout.ToggleLeft(new GUIContent(_localization["save_inverted_too"], _localization["save_inverted_too_tooltip"]), _settings.SaveInvertedToo);
                if (saveInv != _settings.SaveInvertedToo)
                {
                    _settings.SaveInvertedToo = saveInv;
                    _settingsManager.Save(_settings);
                }
                GUILayout.FlexibleSpace();
            }

            // Save button
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                GUI.enabled = _analysis != null;
                if (GUILayout.Button(new GUIContent(_localization["save_png"], _localization["save_png_tooltip"]), GUILayout.Height(28), GUILayout.Width(140)))
                {
                    SaveMaskPNG();
                }
                GUI.enabled = true;
            }
        }

        private void DrawChannelWriteOptions()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                bool ch = EditorGUILayout.ToggleLeft(new GUIContent(_localization["channel_write_enabled"], _localization["channel_write_enabled_tooltip"]), _settings.ChannelWriteEnabled);
                if (ch != _settings.ChannelWriteEnabled)
                {
                    _settings.ChannelWriteEnabled = ch;
                    _settingsManager.Save(_settings);
                }

                using (new EditorGUI.DisabledScope(!_settings.ChannelWriteEnabled))
                {
                    var newBase = EditorGUILayout.ObjectField(new GUIContent(_localization["base_png"], _localization["base_png_tooltip"]), _basePNG, typeof(Texture2D), false) as Texture2D;
                    if (newBase != _basePNG)
                    {
                        _basePNG = newBase;
                        _settingsManager.SetBasePNGPath(_basePNG ? AssetDatabase.GetAssetPath(_basePNG) : string.Empty);
                    }

                    EditorGUILayout.LabelField(_localization["write_channels"]);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        bool r = EditorGUILayout.ToggleLeft(new GUIContent(_localization["channel_r"], _localization["channel_r_tooltip"]), _settings.WriteR, GUILayout.Width(40));
                        bool g = EditorGUILayout.ToggleLeft(new GUIContent(_localization["channel_g"], _localization["channel_g_tooltip"]), _settings.WriteG, GUILayout.Width(40));
                        bool b = EditorGUILayout.ToggleLeft(new GUIContent(_localization["channel_b"], _localization["channel_b_tooltip"]), _settings.WriteB, GUILayout.Width(40));
                        bool a = EditorGUILayout.ToggleLeft(new GUIContent(_localization["channel_a"], _localization["channel_a_tooltip"]), _settings.WriteA, GUILayout.Width(40));
                        if (r != _settings.WriteR || g != _settings.WriteG || b != _settings.WriteB || a != _settings.WriteA)
                        {
                            _settings.WriteR = r;
                            _settings.WriteG = g;
                            _settings.WriteB = b;
                            _settings.WriteA = a;
                            _settingsManager.Save(_settings);
                        }
                    }

                    var newBaseMesh = EditorGUILayout.ObjectField(new GUIContent(_localization["base_vc_mesh"], _localization["base_vc_mesh_tooltip"]), _baseVCMesh, typeof(Mesh), false) as Mesh;
                    if (newBaseMesh != _baseVCMesh)
                    {
                        _baseVCMesh = newBaseMesh;
                        _settingsManager.SetBaseVCMeshPath(_baseVCMesh ? AssetDatabase.GetAssetPath(_baseVCMesh) : string.Empty);
                    }

                    EditorGUILayout.Space(4);
                    EditorGUILayout.LabelField(_localization["vertex_color_bake"], EditorStyles.boldLabel);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button(new GUIContent(_localization["bake_to_vertex_colors"], _localization["bake_to_vertex_colors_tooltip"]), GUILayout.Height(24)))
                        {
                            BakeMaskToVertexColors_ChannelWise();
                        }
                    }
                    _settings.OverwriteExistingVC = EditorGUILayout.ToggleLeft(new GUIContent(_localization["overwrite_existing"], _localization["overwrite_existing_tooltip"]), _settings.OverwriteExistingVC);
                }
            }
        }

        private void DrawHelpBox()
        {
            EditorGUILayout.HelpBox(
                _localization.Get("help_usage", _settings.ModeToggleHotkey),
                MessageType.Info);
        }

        #endregion

        #region Core Logic

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
                            SetTarget(go);
                            break;
                        }
                    }
                }
                evt.Use();
            }
        }

        private void SetTarget(GameObject go)
        {
            _targetGO = go;
            _targetRenderer = null;
            _targetMesh = null;
            _targetTransform = null;
            _analysis = null;
            _selectedIslands.Clear();
            _previewDirty = true;
            _pickingService.Cleanup();
            _overlayRenderer.InvalidateCache();

            if (_bakedMesh != null)
            {
                try { DestroyImmediate(_bakedMesh); } catch { }
                _bakedMesh = null;
            }

            if (_targetGO == null) return;

            var smr = _targetGO.GetComponentInChildren<SkinnedMeshRenderer>();
            var mr = _targetGO.GetComponentInChildren<MeshRenderer>();
            if (smr != null)
            {
                _targetRenderer = smr;
                _targetMesh = smr.sharedMesh;
                _targetTransform = smr.transform;
            }
            else if (mr != null)
            {
                _targetRenderer = mr;
                var mf = mr.GetComponent<MeshFilter>();
                _targetMesh = mf ? mf.sharedMesh : null;
                _targetTransform = mr.transform;
            }

            if (_targetMesh == null)
            {
                EditorUtility.DisplayDialog(_localization["dialog_no_mesh"], _localization["dialog_no_mesh_msg"], _localization["ok"]);
                return;
            }

            Log($"[SetTarget] Target set to '{_targetGO.name}', Mesh='{_targetMesh.name}', VertexCount={_targetMesh.vertexCount}");
            _fileName = _targetGO.name + "_mask";
            BakeCurrentPoseAuto();
            AnalyzeTargetMesh();
            _pickingService.Initialize(_targetTransform, _targetMesh, _bakedMesh, _settings.UseBakedMesh);
        }

        private void AnalyzeTargetMesh()
        {
            if (_targetMesh == null)
            {
                EditorUtility.DisplayDialog(_localization["dialog_no_target"], _localization["dialog_no_target_msg"], _localization["ok"]);
                return;
            }
            try
            {
                _analysis = UVAnalyzer.Analyze(_targetMesh, _settings.UVChannel);
                _selectedIslands.Clear();
                _previewDirty = true;
                _overlayRenderer.InvalidateCache();
                _previewDrawer.InvalidateLabelMap();
                BakeCurrentPoseAuto();
                Repaint();
                Log($"[Analyze] Found {_analysis.Islands.Count} UV islands, {_analysis.BorderEdges.Count} UV border edges");
            }
            catch (Exception ex)
            {
                Debug.LogError($"UV analysis failed: {ex.Message}\n{ex}");
                Log($"[Analyze][Error] {ex}");
            }
        }

        private void InvertSelection()
        {
            if (_analysis == null) return;
            var newSel = new HashSet<int>();
            for (int i = 0; i < _analysis.Islands.Count; i++)
            {
                if (!_selectedIslands.Contains(i)) newSel.Add(i);
            }
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

        /// <summary>
        /// Callback from UVPreviewDrawer when an island is clicked in the preview texture.
        /// </summary>
        private void OnPreviewIslandClicked(int islandIdx)
        {
            if (islandIdx < 0) return; // Clicked on empty area
            if (_analysis == null) return;

            if (_settings.AddMode)
                _selectedIslands.Add(islandIdx);
            else
                _selectedIslands.Remove(islandIdx);

            _previewDirty = true;
            Repaint();
            SceneView.RepaintAll();
            Log($"[PreviewClick] island={islandIdx} {(_settings.AddMode ? "ADD" : "REMOVE")}");
        }

        private Texture GetBaseTexture()
        {
            if (_targetRenderer == null) return null;
            var mats = _targetRenderer.sharedMaterials;
            if (mats == null) return null;
            foreach (var m in mats)
            {
                if (m == null) continue;
                if (m.HasProperty("_BaseMap")) { var t = m.GetTexture("_BaseMap"); if (t != null) return t; }
                if (m.HasProperty("_MainTex")) { var t = m.GetTexture("_MainTex"); if (t != null) return t; }
            }
            return null;
        }

        private void ToggleAddRemoveMode(SceneView sv)
        {
            double now = EditorApplication.timeSinceStartup;
            if (now - _lastHotkeyToggleTime < 0.05f) return;
            _lastHotkeyToggleTime = now;
            _settings.AddMode = !_settings.AddMode;
            var targetSV = sv ?? SceneView.lastActiveSceneView;
            if (targetSV != null)
            {
                targetSV.ShowNotification(new GUIContent(_settings.AddMode ? _localization["notification_mode_add"] : _localization["notification_mode_remove"]));
            }
            Repaint();
            SceneView.RepaintAll();
        }

        private void SaveMaskPNG()
        {
            if (_analysis == null)
            {
                EditorUtility.DisplayDialog(_localization["dialog_no_data"], _localization["dialog_no_data_msg"], _localization["ok"]);
                return;
            }

            if (!AssetDatabase.IsValidFolder(_settings.OutputDir))
            {
                UVMaskExport.EnsureAssetFolderPath(_settings.OutputDir);
            }

            string path = EditorUtility.SaveFilePanelInProject(_localization["file_dialog_save_mask"], _fileName, "png", _localization["file_dialog_save_mask_msg"], _settings.OutputDir);
            if (string.IsNullOrEmpty(path)) return;

            var exportSettings = new ExportSettings
            {
                TextureSize = _settings.TextureSize,
                PixelMargin = _settings.PixelMargin,
                InvertMask = _settings.InvertMask,
                ChannelWriteEnabled = _settings.ChannelWriteEnabled,
                WriteR = _settings.WriteR,
                WriteG = _settings.WriteG,
                WriteB = _settings.WriteB,
                WriteA = _settings.WriteA,
                BasePNG = _basePNG
            };

            if (_exporter.Export(_analysis, _selectedIslands, exportSettings, path))
            {
                Log($"[Save] Wrote PNG {path}");

                // Save inverted mask if option is enabled
                if (_settings.SaveInvertedToo)
                {
                    var invertedSettings = new ExportSettings
                    {
                        TextureSize = _settings.TextureSize,
                        PixelMargin = _settings.PixelMargin,
                        InvertMask = !_settings.InvertMask,  // Invert the invert flag
                        ChannelWriteEnabled = _settings.ChannelWriteEnabled,
                        WriteR = _settings.WriteR,
                        WriteG = _settings.WriteG,
                        WriteB = _settings.WriteB,
                        WriteA = _settings.WriteA,
                        BasePNG = _basePNG
                    };

                    // Generate inverted path: filename.png -> filename_inv.png
                    string dir = Path.GetDirectoryName(path);
                    string nameNoExt = Path.GetFileNameWithoutExtension(path);
                    string invertedPath = Path.Combine(dir, nameNoExt + "_inv.png").Replace('\\', '/');

                    if (_exporter.Export(_analysis, _selectedIslands, invertedSettings, invertedPath))
                    {
                        Log($"[Save] Wrote inverted PNG {invertedPath}");
                    }
                }

                RevealSaved(path);
            }
        }

        private void BakeMaskToVertexColors_ChannelWise()
        {
            if (_targetMesh == null || _analysis == null)
            {
                EditorUtility.DisplayDialog(_localization["dialog_no_target"], _localization["dialog_run_analysis_first"], _localization["ok"]);
                return;
            }
            try
            {
                Color32[] baseColors = null;
                if (_baseVCMesh != null)
                {
                    if (_baseVCMesh.vertexCount != _targetMesh.vertexCount)
                    {
                        EditorUtility.DisplayDialog(_localization["dialog_vertex_count_mismatch"], _localization["dialog_vertex_count_mismatch_msg"], _localization["ok"]);
                        baseColors = _targetMesh.colors32;
                    }
                    else
                    {
                        baseColors = _baseVCMesh.colors32;
                    }
                }
                else
                {
                    baseColors = _targetMesh.colors32;
                }

                var colors = UVVertexColorBaker.BuildVertexColorsChannelWise(
                    _analysis, _selectedIslands, _targetMesh.vertexCount, baseColors,
                    _settings.WriteR, _settings.WriteG, _settings.WriteB, _settings.WriteA);
                var colored = UVVertexColorBaker.CreateColoredMesh(_targetMesh, colors);
                var folder = UVVertexColorBaker.GetDefaultBakeFolderForMesh(_targetMesh);
                var nameNoExt = _targetMesh.name + "_WithVertexColors";
                var assetPath = UVVertexColorBaker.SaveMeshAsset(colored, folder, nameNoExt, _settings.OverwriteExistingVC);
                Log($"[BakeVC-CH] Saved mesh with vertex colors: {assetPath}");
                var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
                if (obj != null)
                {
                    ProjectWindowUtil.ShowCreatedAsset(obj);
                    EditorGUIUtility.PingObject(obj);
                    Selection.activeObject = obj;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Bake vertex colors (channel-wise) failed: {ex.Message}\n{ex}");
                EditorUtility.DisplayDialog(_localization["dialog_error"], _localization["dialog_bake_channel_failed"], _localization["ok"]);
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
            catch { /* ignore in auto mode */ }
        }

        #endregion

        #region Scene View

        private void OnSceneGUI(SceneView sv)
        {
            var e = Event.current;
            if (e.type == EventType.KeyDown && e.keyCode == _settings.ModeToggleHotkey && !EditorGUIUtility.editingTextField)
            {
                ToggleAddRemoveMode(sv);
                e.Use();
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
                    if (_settings.AddMode)
                        _selectedIslands.Add(islandIdx);
                    else
                        _selectedIslands.Remove(islandIdx);
                    _previewDirty = true;
                    Repaint();
                    sv.Repaint();
                    Log($"[Pick] island={islandIdx} {(_settings.AddMode ? "ADD" : "REMOVE")}");
                }
                e.Use();
            }
        }

        #endregion

        #region Utilities

        private static string MakeProjectRelative(string absPath, string projectRoot)
        {
            try
            {
                var full = Path.GetFullPath(absPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var root = Path.GetFullPath(projectRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return null;
                return full.Substring(root.Length + 1);
            }
            catch { return null; }
        }

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
            try
            {
                Directory.CreateDirectory(LogDir);
                File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss} {msg}\n");
            }
            catch { /* ignore */ }
            Debug.Log($"[UVMaskMaker] {msg}");
        }

        #endregion
    }
}
