// ExportSectionDrawer.cs - Draws the export settings and save button
using System.IO;
using UnityEditor;
using UnityEngine;
using Dennoko.UVTools.Data;
using Dennoko.UVTools.Services;

namespace Dennoko.UVTools.UI
{
    /// <summary>
    /// Draws the export section with resolution, file settings, and save button.
    /// Supports drag-and-drop for output folder selection.
    /// </summary>
    public class ExportSectionDrawer
    {
        private readonly LocalizationService _localization;
        private string _fileName = "uv_mask";

        public ExportSectionDrawer(LocalizationService localization)
        {
            _localization = localization;
        }

        // Events
        public event System.Action OnSaveClicked;
        public event System.Action<int> OnResolutionChanged;
        public event System.Action<string> OnOutputDirChanged;
        public event System.Action<bool> OnSaveInvertedChanged;
        public event System.Action<bool> OnInvertMaskChanged;
        public event System.Action<int> OnPixelMarginChanged;
        public event System.Action<bool> OnUseTextureFolderChanged;

        public string FileName
        {
            get => _fileName;
            set => _fileName = value;
        }

        /// <summary>
        /// Draws the export section.
        /// </summary>
        /// <param name="settings">Current mask settings</param>
        /// <param name="hasAnalysis">Whether analysis data is available</param>
        /// <param name="baseTexturePath">Path to base texture for auto-folder detection (null if none)</param>
        public void Draw(MaskSettings settings, bool hasAnalysis, string baseTexturePath = null)
        {
            EditorUIStyles.BeginCard(_localization["export_section"]);

            // Resolution + File name row
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(
                    new GUIContent(_localization["resolution"], _localization["resolution_tooltip"]),
                    GUILayout.Width(60));

                int newSize = EditorGUILayout.IntPopup(
                    settings.TextureSize,
                    new[] { "512", "1024", "2048", "4096" },
                    new[] { 512, 1024, 2048, 4096 },
                    GUILayout.Width(70));

                if (newSize != settings.TextureSize)
                {
                    OnResolutionChanged?.Invoke(newSize);
                }

                GUILayout.Space(10);

                _fileName = EditorGUILayout.TextField(
                    new GUIContent(_localization["file_name"], _localization["file_name_tooltip"]),
                    _fileName);
            }

            // Output folder with drag-and-drop
            DrawOutputFolderSection(settings, baseTexturePath);

            EditorGUILayout.Space(2);

            // Quick options row
            using (new EditorGUILayout.HorizontalScope())
            {
                bool inv = EditorUIStyles.DrawToggle(
                    settings.InvertMask,
                    _localization["invert_mask"],
                    _localization["invert_mask_tooltip"]);
                if (inv != settings.InvertMask)
                {
                    OnInvertMaskChanged?.Invoke(inv);
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                bool saveInv = EditorUIStyles.DrawToggle(
                    settings.SaveInvertedToo,
                    _localization["save_inverted_too"],
                    _localization["save_inverted_too_tooltip"]);
                if (saveInv != settings.SaveInvertedToo)
                {
                    OnSaveInvertedChanged?.Invoke(saveInv);
                }
            }

            // Pixel margin
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(
                    new GUIContent(_localization["pixel_margin"], _localization["pixel_margin_tooltip"]),
                    GUILayout.Width(100));

                int newMargin = EditorGUILayout.IntSlider(settings.PixelMargin, 0, 16);
                if (newMargin != settings.PixelMargin)
                {
                    OnPixelMarginChanged?.Invoke(newMargin);
                }
            }

            EditorGUILayout.Space(8);

            // Save button (primary action)
            GUI.enabled = hasAnalysis;
            if (EditorUIStyles.DrawPrimaryButton(
                "💾 " + _localization["save_png"],
                _localization["save_png_tooltip"],
                160))
            {
                OnSaveClicked?.Invoke();
            }
            GUI.enabled = true;

            EditorUIStyles.EndCard();
        }

        private void DrawOutputFolderSection(MaskSettings settings, string baseTexturePath)
        {
            // Drag-and-drop area for folder/file
            var dropRect = GUILayoutUtility.GetRect(0, 24, GUILayout.ExpandWidth(true));
            var dropStyle = new GUIStyle(EditorStyles.helpBox)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 10
            };
            GUI.Box(dropRect, _localization.Get("output_folder_drop_hint", "フォルダまたは画像をドロップ"), dropStyle);
            HandleFolderDragAndDrop(dropRect);

            // Output folder row
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(
                    new GUIContent(_localization["output_folder"], _localization["output_folder_tooltip"]),
                    GUILayout.Width(80));

                EditorGUILayout.TextField(settings.OutputDir);

                if (GUILayout.Button(
                    new GUIContent(_localization["browse"], _localization["browse_tooltip"]),
                    EditorUIStyles.SmallButtonStyle, GUILayout.Width(50)))
                {
                    var selected = EditorUtility.OpenFolderPanel(
                        _localization["folder_dialog_select"],
                        settings.OutputDir, "");

                    if (!string.IsNullOrEmpty(selected))
                    {
                        SetOutputFolder(selected);
                    }
                }

                // Auto-detect from texture button
                bool hasBaseTexture = !string.IsNullOrEmpty(baseTexturePath);
                GUI.enabled = hasBaseTexture;
                if (GUILayout.Button(
                    new GUIContent("📁", _localization.Get("use_texture_folder_tooltip", "メインテクスチャのフォルダを使用")),
                    EditorUIStyles.SmallButtonStyle, GUILayout.Width(28)))
                {
                    if (hasBaseTexture)
                    {
                        string texDir = Path.GetDirectoryName(baseTexturePath);
                        if (!string.IsNullOrEmpty(texDir))
                        {
                            OnOutputDirChanged?.Invoke(texDir.Replace('\\', '/'));
                        }
                    }
                }
                GUI.enabled = true;
            }
        }

        private void HandleFolderDragAndDrop(Rect dropRect)
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
                        string assetPath = AssetDatabase.GetAssetPath(obj);
                        if (string.IsNullOrEmpty(assetPath)) continue;

                        // Check if it's a folder
                        if (AssetDatabase.IsValidFolder(assetPath))
                        {
                            OnOutputDirChanged?.Invoke(assetPath.Replace('\\', '/'));
                            evt.Use();
                            return;
                        }

                        // If it's a file (e.g., texture), use its parent folder
                        string parentDir = Path.GetDirectoryName(assetPath);
                        if (!string.IsNullOrEmpty(parentDir))
                        {
                            OnOutputDirChanged?.Invoke(parentDir.Replace('\\', '/'));
                            evt.Use();
                            return;
                        }
                    }

                    // Handle paths from file system
                    foreach (var path in DragAndDrop.paths)
                    {
                        if (string.IsNullOrEmpty(path)) continue;
                        SetOutputFolder(path);
                        evt.Use();
                        return;
                    }
                }
                evt.Use();
            }
        }

        private void SetOutputFolder(string path)
        {
            string folderPath = path;

            // If it's a file, get its directory
            if (File.Exists(path))
            {
                folderPath = Path.GetDirectoryName(path);
            }

            if (!string.IsNullOrEmpty(folderPath) && folderPath.Contains("Assets"))
            {
                var projPath = Path.GetFullPath(Application.dataPath + "/..");
                var rel = MakeProjectRelative(folderPath, projPath);
                if (!string.IsNullOrEmpty(rel))
                {
                    OnOutputDirChanged?.Invoke(rel.Replace('\\', '/'));
                }
            }
            else
            {
                EditorUtility.DisplayDialog(
                    _localization["dialog_invalid_folder"],
                    _localization["dialog_invalid_folder_msg"],
                    _localization["ok"]);
            }
        }

        private static string MakeProjectRelative(string absPath, string projectRoot)
        {
            try
            {
                var full = Path.GetFullPath(absPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var root = Path.GetFullPath(projectRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (!full.StartsWith(root, System.StringComparison.OrdinalIgnoreCase)) return null;
                return full.Substring(root.Length + 1);
            }
            catch { return null; }
        }
    }
}
