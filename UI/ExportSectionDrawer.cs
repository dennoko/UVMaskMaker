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
    /// Advanced options (channel write, vertex color) are collapsed by default.
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

        public string FileName
        {
            get => _fileName;
            set => _fileName = value;
        }

        /// <summary>
        /// Draws the export section.
        /// </summary>
        public void Draw(MaskSettings settings, bool hasAnalysis)
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

            // Output folder
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(
                    new GUIContent(_localization["output_folder"], _localization["output_folder_tooltip"]),
                    GUILayout.Width(80));

                EditorGUILayout.TextField(settings.OutputDir);

                if (GUILayout.Button(
                    new GUIContent(_localization["browse"], _localization["browse_tooltip"]),
                    EditorUIStyles.SmallButtonStyle, GUILayout.Width(60)))
                {
                    var selected = EditorUtility.OpenFolderPanel(
                        _localization["folder_dialog_select"],
                        settings.OutputDir, "");

                    if (!string.IsNullOrEmpty(selected))
                    {
                        if (selected.Contains("Assets"))
                        {
                            var projPath = Path.GetFullPath(Application.dataPath + "/..");
                            var rel = MakeProjectRelative(selected, projPath);
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
                }
            }

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
