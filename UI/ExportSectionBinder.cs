// ExportSectionBinder.cs - Binds the Quick Export card and the collapsible
// Output Settings foldout (UI Toolkit).
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Dennoko.UVTools.Data;
using Dennoko.UVTools.Services;

namespace Dennoko.UVTools.UI
{
    /// <summary>
    /// Binds the export section: Quick Export (always visible) and the
    /// collapsible Output Settings foldout.
    /// </summary>
    public class ExportSectionBinder
    {
        private readonly LocalizationService _localization;
        private string _fileName = "uv_mask";

        // Quick export
        private Label _exportTitle;
        private DropdownField _resolutionDropdown;
        private Toggle _invertMaskToggle;
        private SliderInt _pixelMarginSlider;
        private Button _savePngBtn;

        // Output settings (collapsible)
        private Foldout _outputSettingsFoldout;
        private TextField _filenameField;
        private Label _outputDropArea;
        private TextField _outputDirField;
        private Button _browseBtn;
        private Toggle _useTexFolderToggle;
        private Toggle _saveInvertedToggle;

        private static readonly List<string> ResolutionChoices = new List<string> { "512", "1024", "2048", "4096" };

        public ExportSectionBinder(LocalizationService localization)
        {
            _localization = localization;
        }

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
            set
            {
                _fileName = value;
                _filenameField?.SetValueWithoutNotify(value);
            }
        }

        public void Bind(VisualElement root)
        {
            _exportTitle         = root.Q<Label>("export-title");
            _resolutionDropdown  = root.Q<DropdownField>("resolution-dropdown");
            _invertMaskToggle    = root.Q<Toggle>("invert-mask-toggle");
            _pixelMarginSlider   = root.Q<SliderInt>("pixel-margin-slider");
            _savePngBtn          = root.Q<Button>("save-png-btn");

            _outputSettingsFoldout = root.Q<Foldout>("output-settings-foldout");
            _filenameField          = root.Q<TextField>("filename-field");
            _outputDropArea         = root.Q<Label>("output-drop-area");
            _outputDirField         = root.Q<TextField>("output-dir-field");
            _browseBtn              = root.Q<Button>("browse-btn");
            _useTexFolderToggle     = root.Q<Toggle>("use-tex-folder-toggle");
            _saveInvertedToggle     = root.Q<Toggle>("save-inverted-toggle");

            _resolutionDropdown.choices = ResolutionChoices;
            _resolutionDropdown.RegisterValueChangedCallback(evt =>
            {
                if (int.TryParse(evt.newValue, out int size)) OnResolutionChanged?.Invoke(size);
            });

            _invertMaskToggle.RegisterValueChangedCallback(evt => OnInvertMaskChanged?.Invoke(evt.newValue));
            _pixelMarginSlider.RegisterValueChangedCallback(evt => OnPixelMarginChanged?.Invoke(evt.newValue));
            _savePngBtn.clicked += () => OnSaveClicked?.Invoke();

            _filenameField.RegisterValueChangedCallback(evt => _fileName = evt.newValue);

            _useTexFolderToggle.RegisterValueChangedCallback(evt => OnUseTextureFolderChanged?.Invoke(evt.newValue));
            _saveInvertedToggle.RegisterValueChangedCallback(evt => OnSaveInvertedChanged?.Invoke(evt.newValue));

            _browseBtn.clicked += () =>
            {
                var selected = EditorUtility.OpenFolderPanel(
                    _localization["folder_dialog_select"], _outputDirField.value, "");
                if (!string.IsNullOrEmpty(selected)) SetOutputFolder(selected);
            };

            // Drag-and-drop of a folder/texture onto the output drop area.
            _outputDropArea.RegisterCallback<DragEnterEvent>(evt =>
                _outputDropArea.AddToClassList("maskmaker-drop-area--hover"));
            _outputDropArea.RegisterCallback<DragLeaveEvent>(evt =>
                _outputDropArea.RemoveFromClassList("maskmaker-drop-area--hover"));
            _outputDropArea.RegisterCallback<DragUpdatedEvent>(evt =>
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                evt.StopPropagation();
            });
            _outputDropArea.RegisterCallback<DragPerformEvent>(evt =>
            {
                DragAndDrop.AcceptDrag();
                _outputDropArea.RemoveFromClassList("maskmaker-drop-area--hover");
                HandleFolderDrop();
                evt.StopPropagation();
            });
        }

        public void ApplyLocalization()
        {
            _exportTitle.text = _localization.Get("quick_export", "クイックエクスポート");

            _resolutionDropdown.label = _localization["resolution"];
            _resolutionDropdown.tooltip = _localization["resolution_tooltip"];

            _invertMaskToggle.label = _localization["invert_mask"];
            _invertMaskToggle.tooltip = _localization["invert_mask_tooltip"];

            _pixelMarginSlider.label = _localization["pixel_margin"];
            _pixelMarginSlider.tooltip = _localization["pixel_margin_tooltip"];

            _savePngBtn.text = _localization["save_png"];
            _savePngBtn.tooltip = _localization["save_png_tooltip"];

            _outputSettingsFoldout.text = _localization.Get("output_settings", "出力設定");
            _outputSettingsFoldout.tooltip = _localization.Get(
                "output_settings_tooltip", "ファイル名、出力先などの詳細設定");

            _filenameField.label = _localization["file_name"];
            _filenameField.tooltip = _localization["file_name_tooltip"];

            _outputDropArea.text = _localization.Get("output_folder_drop_hint", "フォルダまたは画像をドロップ");

            _outputDirField.label = _localization["output_folder"];
            _outputDirField.tooltip = _localization["output_folder_tooltip"];

            _browseBtn.text = _localization["browse"];
            _browseBtn.tooltip = _localization["browse_tooltip"];

            // NOTE: the legacy drawer used the same (tooltip) key for both the
            // label and the tooltip of this toggle; kept identical here.
            _useTexFolderToggle.label = _localization.Get(
                "use_texture_folder_tooltip", "メインテクスチャのフォルダを使用");
            _useTexFolderToggle.tooltip = _localization.Get(
                "use_texture_folder_tooltip", "メインテクスチャのフォルダを使用");

            _saveInvertedToggle.label = _localization["save_inverted_too"];
            _saveInvertedToggle.tooltip = _localization["save_inverted_too_tooltip"];
        }

        public void UpdateState(MaskSettings settings)
        {
            _resolutionDropdown.SetValueWithoutNotify(settings.TextureSize.ToString());
            _invertMaskToggle.SetValueWithoutNotify(settings.InvertMask);
            _pixelMarginSlider.SetValueWithoutNotify(settings.PixelMargin);

            _outputDirField.SetValueWithoutNotify(settings.OutputDir);
            _outputDirField.SetEnabled(!settings.UseTextureFolder);
            _useTexFolderToggle.SetValueWithoutNotify(settings.UseTextureFolder);
            _saveInvertedToggle.SetValueWithoutNotify(settings.SaveInvertedToo);
        }

        private void HandleFolderDrop()
        {
            foreach (var obj in DragAndDrop.objectReferences)
            {
                string assetPath = AssetDatabase.GetAssetPath(obj);
                if (string.IsNullOrEmpty(assetPath)) continue;

                if (AssetDatabase.IsValidFolder(assetPath))
                {
                    OnOutputDirChanged?.Invoke(assetPath.Replace('\\', '/'));
                    return;
                }

                string parentDir = Path.GetDirectoryName(assetPath);
                if (!string.IsNullOrEmpty(parentDir))
                {
                    OnOutputDirChanged?.Invoke(parentDir.Replace('\\', '/'));
                    return;
                }
            }

            foreach (var path in DragAndDrop.paths)
            {
                if (string.IsNullOrEmpty(path)) continue;
                SetOutputFolder(path);
                return;
            }
        }

        private void SetOutputFolder(string path)
        {
            string folderPath = path;

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
