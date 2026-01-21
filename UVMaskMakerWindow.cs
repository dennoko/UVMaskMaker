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
    /// Acts as a thin coordinator, delegating UI drawing to specialized drawer classes.
    /// </summary>
    public class UVMaskMakerWindow : EditorWindow
    {
        // Services
        private SettingsManager _settingsManager;
        private LocalizationService _localization;
        private PickingService _pickingService;
        private OverlayRenderer _overlayRenderer;
        private WorkCopyService _workCopyService;
        private UVPreviewDrawer _previewDrawer;
        private IMaskExporter _exporter;

        // UI Drawers
        private TargetSectionDrawer _targetDrawer;
        private SelectionSectionDrawer _selectionDrawer;
        private ExportSectionDrawer _exportDrawer;
        private AdvancedOptionsDrawer _advancedDrawer;

        // Settings
        private MaskSettings _settings;

        // Target selection and mesh refs
        private GameObject _targetGO;
        private Renderer _targetRenderer;
        private Mesh _targetMesh;
        private Transform _targetTransform;
        
        // Work Copy state
        private bool _isWorkCopy;
        private GameObject _sourceTargetGO;

        // UI state
        private Vector2 _scrollPos;
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
            wnd.minSize = new Vector2(400, 600);
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

            InitializeServices();
            InitializeDrawers();
            LoadAssetReferences();
            SubscribeToEvents();
        }

        private void InitializeServices()
        {
            _settingsManager = new SettingsManager();
            _settings = _settingsManager.Load();
            _localization = LocalizationService.Instance;
            _localization.LoadLanguage(_settings.Language);
            _pickingService = new PickingService();
            _overlayRenderer = new OverlayRenderer();
            _workCopyService = new WorkCopyService();
            _previewDrawer = new UVPreviewDrawer();
            _exporter = new PngExporter();
        }

        private void InitializeDrawers()
        {
            _targetDrawer = new TargetSectionDrawer(_localization);
            _selectionDrawer = new SelectionSectionDrawer(_localization);
            _exportDrawer = new ExportSectionDrawer(_localization);
            _advancedDrawer = new AdvancedOptionsDrawer(_localization);

            // Wire up target drawer events
            _targetDrawer.OnTargetChanged += SetTarget;
            _targetDrawer.OnBakedMeshChanged += OnBakedMeshOptionChanged;
            _targetDrawer.OnSetupWorkCopyClicked += SetupWorkCopy;
            _targetDrawer.OnCleanupWorkCopyClicked += CleanupWorkCopy;

            // Wire up selection drawer events
            _selectionDrawer.OnAnalyzeClicked += AnalyzeTargetMesh;
            _selectionDrawer.OnInvertClicked += InvertSelection;
            _selectionDrawer.OnSelectAllClicked += SelectAll;
            _selectionDrawer.OnClearClicked += ClearSelection;
            _selectionDrawer.OnModeChanged += mode => { _settings.AddMode = mode; _settingsManager.Save(_settings); };
            _selectionDrawer.OnHotkeyChanged += key => { _settings.ModeToggleHotkey = key; _settingsManager.Save(_settings); };

            // Wire up export drawer events
            _exportDrawer.OnSaveClicked += SaveMaskPNG;
            _exportDrawer.OnResolutionChanged += size => { _settings.TextureSize = size; _previewDirty = true; _settingsManager.Save(_settings); _previewDrawer.InvalidateLabelMap(); };
            _exportDrawer.OnOutputDirChanged += dir => { _settings.OutputDir = dir; _settingsManager.Save(_settings); };
            _exportDrawer.OnSaveInvertedChanged += val => { _settings.SaveInvertedToo = val; _settingsManager.Save(_settings); };
            _exportDrawer.OnInvertMaskChanged += val => { _settings.InvertMask = val; _previewDirty = true; _settingsManager.Save(_settings); };
            _exportDrawer.OnPixelMarginChanged += val => { _settings.PixelMargin = val; _previewDirty = true; _settingsManager.Save(_settings); };
            _exportDrawer.OnUseTextureFolderChanged += val => { _settings.UseTextureFolder = val; _settingsManager.Save(_settings); };

            // Wire up advanced drawer events
            WireAdvancedDrawerEvents();

            // Wire up preview drawer events
            _previewDrawer.OnIslandClicked += OnPreviewIslandClicked;
        }

        private void WireAdvancedDrawerEvents()
        {
            _advancedDrawer.OnUVChannelChanged += ch => { _settings.UVChannel = ch; _settingsManager.Save(_settings); AnalyzeTargetMesh(); };
            _advancedDrawer.OnOverlayOnTopChanged += v => { _settings.OverlayOnTop = v; _settingsManager.Save(_settings); SceneView.RepaintAll(); };
            _advancedDrawer.OnDisableAAChanged += v => { _settings.DisableAA = v; _settingsManager.Save(_settings); SceneView.RepaintAll(); };
            _advancedDrawer.OnBackfaceCullChanged += v => { _settings.BackfaceCull = v; _settingsManager.Save(_settings); SceneView.RepaintAll(); };
            _advancedDrawer.OnThicknessChanged += v => { _settings.OverlaySeamThickness = v; _settingsManager.Save(_settings); SceneView.RepaintAll(); };
            _advancedDrawer.OnDepthOffsetChanged += v => { _settings.OverlayDepthOffset = v; _settingsManager.Save(_settings); SceneView.RepaintAll(); };
            _advancedDrawer.OnSeamColorChanged += c => { _settings.SeamColor = c; _settingsManager.Save(_settings); SceneView.RepaintAll(); };
            _advancedDrawer.OnSelectedColorChanged += c => { _settings.SelectedSceneColor = c; _settingsManager.Save(_settings); SceneView.RepaintAll(); };
            _advancedDrawer.OnPreviewFillColorChanged += c => { _settings.PreviewFillSelectedColor = c; _previewDirty = true; _settingsManager.Save(_settings); };
            _advancedDrawer.OnOverlayAlphaChanged += v => { _settings.PreviewOverlayAlpha = v; _previewDirty = true; _settingsManager.Save(_settings); };
            _advancedDrawer.OnShowIslandPreviewChanged += v => { _settings.ShowIslandPreview = v; _settingsManager.Save(_settings); Repaint(); };
            _advancedDrawer.OnPreviewOverlayBaseChanged += v => { _settings.PreviewOverlayBaseTex = v; _settingsManager.Save(_settings); Repaint(); };
            _advancedDrawer.OnChannelWriteEnabledChanged += v => { _settings.ChannelWriteEnabled = v; _settingsManager.Save(_settings); };
            _advancedDrawer.OnBasePNGChanged += tex => { _basePNG = tex; _settingsManager.SetBasePNGPath(tex ? AssetDatabase.GetAssetPath(tex) : ""); };
            _advancedDrawer.OnChannelsChanged += (r, g, b, a) => { _settings.WriteR = r; _settings.WriteG = g; _settings.WriteB = b; _settings.WriteA = a; _settingsManager.Save(_settings); };
            _advancedDrawer.OnBakeVertexColorClicked += BakeMaskToVertexColors;
            _advancedDrawer.OnBaseVCMeshChanged += m => { _baseVCMesh = m; _settingsManager.SetBaseVCMeshPath(m ? AssetDatabase.GetAssetPath(m) : ""); };
            _advancedDrawer.OnOverwriteExistingChanged += v => { _settings.OverwriteExistingVC = v; _settingsManager.Save(_settings); };
            _advancedDrawer.OnWorkCopyOffsetChanged += v => { _settings.WorkCopyOffset = v; _settingsManager.Save(_settings); };
            _advancedDrawer.OnUseEnglishChanged += OnLanguageChanged;
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
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorSceneManager.sceneSaving -= OnSceneSaving;
            if (_settingsManager != null && _settings != null) _settingsManager.Save(_settings);

            _pickingService?.Dispose();
            if (_previewDrawer != null)
            {
                _previewDrawer.OnIslandClicked -= OnPreviewIslandClicked;
                _previewDrawer.Dispose();
            }
            if (_advancedDrawer != null) _advancedDrawer.OnUseEnglishChanged -= OnLanguageChanged;

            if (_bakedMesh != null)
            {
                try { DestroyImmediate(_bakedMesh); } catch { }
                _bakedMesh = null;
            }

            Log("[OnDisable] Window closed");
        }

        private void OnPlayModeStateChanged(PlayModeStateChange state) { }
        private void OnSceneSaving(UnityEngine.SceneManagement.Scene scene, string path) => _pickingService?.Cleanup();

        private void OnGUI()
        {
            HandleHotkey();

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);
            EditorGUILayout.Space(4);

            // Target section (always visible)
            _targetDrawer.Draw(_targetGO, _targetRenderer, _settings, _isWorkCopy);

            // Preview section
            DrawPreview();

            // Selection section (always visible)
            _selectionDrawer.Draw(_settings, _analysis != null);

            // Export section (always visible, with basic options)
            _exportDrawer.FileName = _targetGO != null ? _targetGO.name + "_mask" : "uv_mask";
            _exportDrawer.Draw(_settings, _analysis != null, GetBaseTexturePath());

            EditorGUILayout.Space(8);

            // Advanced options (collapsed by default)
            _advancedDrawer.DrawOverlaySection(_settings, GetBaseTexture());
            _advancedDrawer.DrawChannelWriteSection(_settings, _basePNG);
            _advancedDrawer.DrawVertexColorSection(_settings, _baseVCMesh, _analysis != null);
            _advancedDrawer.DrawPreferencesSection(_settings);

            EditorGUILayout.Space(8);

            // Help
            DrawHelpBox();

            EditorGUILayout.EndScrollView();
        }

        private void DrawPreview()
        {
            EditorGUILayout.Space(4);
            if (_previewDirty)
            {
                _previewDrawer.MarkDirty();
                _previewDirty = false;
            }
            _previewDrawer.Draw(_analysis, _selectedIslands, _settings, _settings.PreviewOverlayBaseTex ? GetBaseTexture() : null, _localization);
            EditorGUILayout.Space(4);
        }

        private void DrawHelpBox()
        {
            EditorGUILayout.HelpBox(
                _localization.Get("help_usage", _settings.ModeToggleHotkey),
                MessageType.Info);
        }

        private void HandleHotkey()
        {
            var e = Event.current;
            if (e != null && e.type == EventType.KeyDown && e.keyCode == _settings.ModeToggleHotkey && !EditorGUIUtility.editingTextField)
            {
                ToggleAddRemoveMode(null);
                e.Use();
            }
        }

        #region Core Logic

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
            _previewDrawer.InvalidateLabelMap();

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

            // Work Copy check
            _isWorkCopy = _workCopyService.IsWorkCopy(_targetGO);
            if (!_isWorkCopy && _sourceTargetGO != null && _sourceTargetGO != _targetGO)
            {
                // Lost track of source or switched manually. Clear source reference.
                // Or maybe we selected the source again?
                if (_targetGO != _sourceTargetGO) _sourceTargetGO = null;
            }

            if (_targetMesh == null)
            {
                EditorUtility.DisplayDialog(_localization["dialog_no_mesh"], _localization["dialog_no_mesh_msg"], _localization["ok"]);
                return;
            }

            Log($"[SetTarget] Target set to '{_targetGO.name}', Mesh='{_targetMesh.name}', VertexCount={_targetMesh.vertexCount}");
            BakeCurrentPoseAuto();
            AnalyzeTargetMesh();
            _pickingService.Initialize(_targetTransform, _targetMesh, _bakedMesh, _settings.UseBakedMesh);
        }

        private void OnBakedMeshOptionChanged(bool useBaked)
        {
            _settings.UseBakedMesh = useBaked;
            _settingsManager.Save(_settings);
            _overlayRenderer.InvalidateCache();
            _pickingService.UpdateMesh(_bakedMesh, useBaked);
            SceneView.RepaintAll();
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

        private void SetupWorkCopy()
        {
            if (_targetRenderer == null) return;
            if (_isWorkCopy) return;

            // Remember source
            _sourceTargetGO = _targetGO;

            // Create copy
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

            var copyToDelete = _targetGO;
            var originalSource = _sourceTargetGO;

            // Switch back first? Or delete first?
            // If we delete active object, inspector might freak out.
            // Let's set target back to source first.
            if (originalSource != null)
            {
                SetTarget(originalSource);
            }
            else
            {
                // Source lost? Just clear target.
                SetTarget(null);
            }

            _workCopyService.CleanupWorkCopy(copyToDelete);
            Log("[WorkCopy] Cleaned up work copy");
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

        private void OnPreviewIslandClicked(int islandIdx)
        {
            if (islandIdx < 0) return;
            if (_analysis == null) return;

            // Toggle selection regardless of mode (preview clicks are precise)
            if (_selectedIslands.Contains(islandIdx))
                _selectedIslands.Remove(islandIdx);
            else
                _selectedIslands.Add(islandIdx);

            _previewDirty = true;
            Repaint();
            SceneView.RepaintAll();
            Log($"[PreviewClick] island={islandIdx} TOGGLE → {(_selectedIslands.Contains(islandIdx) ? "SELECTED" : "DESELECTED")}");
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

        private string GetBaseTexturePath()
        {
            var tex = GetBaseTexture();
            if (tex == null) return null;
            return AssetDatabase.GetAssetPath(tex);
        }

        private void ToggleAddRemoveMode(SceneView sv)
        {
            double now = EditorApplication.timeSinceStartup;
            if (now - _lastHotkeyToggleTime < 0.05f) return;
            _lastHotkeyToggleTime = now;
            _settings.AddMode = !_settings.AddMode;
            var targetSV = sv ?? SceneView.lastActiveSceneView;
            targetSV?.ShowNotification(new GUIContent(_settings.AddMode ? _localization["notification_mode_add"] : _localization["notification_mode_remove"]));
            Repaint();
            SceneView.RepaintAll();
        }

        private void OnLanguageChanged(bool useEnglish)
        {
            _settings.UseEnglish = useEnglish;
            _settings.Language = useEnglish ? "en" : "ja";
            _localization.LoadLanguage(_settings.Language);
            RequestRepaint();
        }

        private void SaveMaskPNG()
        {
            if (_analysis == null)
            {
                EditorUtility.DisplayDialog(_localization["dialog_no_data"], _localization["dialog_no_data_msg"], _localization["ok"]);
                return;
            }

            string targetDir = _settings.OutputDir;
            if (_settings.UseTextureFolder)
            {
                string texPath = GetBaseTexturePath();
                if (!string.IsNullOrEmpty(texPath))
                {
                    targetDir = Path.GetDirectoryName(texPath);
                }
            }

            if (!AssetDatabase.IsValidFolder(targetDir))
            {
                UVMaskExport.EnsureAssetFolderPath(targetDir);
            }

            string fileName = _exportDrawer.FileName;
            if (string.IsNullOrEmpty(fileName)) fileName = "uv_mask";
            if (!fileName.EndsWith(".png")) fileName += ".png";

            string fullPath = Path.Combine(targetDir, fileName).Replace('\\', '/');
            fullPath = AssetDatabase.GenerateUniqueAssetPath(fullPath);

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

            if (_exporter.Export(_analysis, _selectedIslands, exportSettings, fullPath))
            {
                Log($"[Save] Wrote PNG {fullPath}");
                var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(fullPath);
                if (obj != null) EditorGUIUtility.PingObject(obj);

                if (_settings.SaveInvertedToo)
                {
                    var invertedSettings = new ExportSettings
                    {
                        TextureSize = _settings.TextureSize,
                        PixelMargin = _settings.PixelMargin,
                        InvertMask = !_settings.InvertMask,
                        ChannelWriteEnabled = _settings.ChannelWriteEnabled,
                        WriteR = _settings.WriteR,
                        WriteG = _settings.WriteG,
                        WriteB = _settings.WriteB,
                        WriteA = _settings.WriteA,
                        BasePNG = _basePNG
                    };

                    string dir = Path.GetDirectoryName(fullPath);
                    string nameNoExt = Path.GetFileNameWithoutExtension(fullPath);
                    string invertedPath = Path.Combine(dir, nameNoExt + "_inv.png").Replace('\\', '/');
                    
                    if (_exporter.Export(_analysis, _selectedIslands, invertedSettings, invertedPath))
                    {
                        Log($"[Save] Wrote Inverted PNG {invertedPath}");
                    }
                }
            }
        }


        private void BakeMaskToVertexColors()
        {
            if (_targetMesh == null || _analysis == null)
            {
                EditorUtility.DisplayDialog(_localization["dialog_no_target"], _localization["dialog_run_analysis_first"], _localization["ok"]);
                return;
            }
            try
            {
                Color32[] baseColors = _baseVCMesh != null && _baseVCMesh.vertexCount == _targetMesh.vertexCount
                    ? _baseVCMesh.colors32
                    : _targetMesh.colors32;

                var colors = UVVertexColorBaker.BuildVertexColorsChannelWise(
                    _analysis, _selectedIslands, _targetMesh.vertexCount, baseColors,
                    _settings.WriteR, _settings.WriteG, _settings.WriteB, _settings.WriteA);
                var colored = UVVertexColorBaker.CreateColoredMesh(_targetMesh, colors);
                var folder = UVVertexColorBaker.GetDefaultBakeFolderForMesh(_targetMesh);
                var nameNoExt = _targetMesh.name + "_WithVertexColors";
                var assetPath = UVVertexColorBaker.SaveMeshAsset(colored, folder, nameNoExt, _settings.OverwriteExistingVC);
                Log($"[BakeVC] Saved mesh with vertex colors: {assetPath}");
                RevealSaved(assetPath);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Bake vertex colors failed: {ex.Message}\n{ex}");
                EditorUtility.DisplayDialog(_localization["dialog_error"], _localization["dialog_bake_channel_failed"], _localization["ok"]);
            }
        }

        private void BakeCurrentPoseAuto()
        {
            if (!(_targetRenderer is SkinnedMeshRenderer smr)) return;
            if (_bakedMesh == null) _bakedMesh = new Mesh { name = $"{_targetMesh?.name}_Baked" };
            else _bakedMesh.Clear();
            
            try { smr.BakeMesh(_bakedMesh); _overlayRenderer.InvalidateCache(); _pickingService.UpdateMesh(_bakedMesh, _settings.UseBakedMesh); }
            catch { }
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

        #endregion

        #region Utilities

        private void RequestRepaint()
        {
            Repaint();
            SceneView.RepaintAll();
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
            try { Directory.CreateDirectory(LogDir); File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss} {msg}\n"); } catch { }
            Debug.Log($"[UVMaskMaker] {msg}");
        }

        #endregion
    }
}
