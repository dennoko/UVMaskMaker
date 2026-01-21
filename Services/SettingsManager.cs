// SettingsManager.cs - Manages EditorPrefs persistence for MaskSettings
using UnityEditor;
using UnityEngine;
using Dennoko.UVTools.Data;

namespace Dennoko.UVTools.Services
{
    /// <summary>
    /// Handles loading and saving MaskSettings to/from EditorPrefs.
    /// Centralizes all preferences key management.
    /// </summary>
    public class SettingsManager
    {
        // EditorPrefs key prefix
        private const string Prefix = "Dennoko.UVTools.UVMaskMaker.";

        // Preference keys
        private static class Keys
        {
            public const string TextureSize = Prefix + "TextureSize";
            public const string PixelMargin = Prefix + "PixelMargin";
            public const string InvertMask = Prefix + "InvertMask";
            public const string LastSaveDir = Prefix + "LastSaveDir";
            public const string ModeToggleHotkey = Prefix + "ModeToggleHotkey";
            public const string UVChannel = Prefix + "UVChannel";
            public const string OverlayOnTop = Prefix + "OverlayOnTop";
            public const string OverlayDepthOffset = Prefix + "OverlayDepthOffset";
            public const string OverlaySeamThickness = Prefix + "OverlaySeamThickness";
            public const string DisableAA = Prefix + "DisableAA";
            public const string BackfaceCull = Prefix + "BackfaceCull";
            public const string ShowIslandPreview = Prefix + "ShowIslandPreview";
            public const string PreviewOverlayBaseTex = Prefix + "PreviewOverlayBaseTex";
            public const string PreviewOverlayAlpha = Prefix + "PreviewOverlayAlpha";
            public const string SelectedSceneColor = Prefix + "SelectedSceneColor";
            public const string SeamColor = Prefix + "SeamColor";
            public const string PreviewFillSelectedColor = Prefix + "PreviewFillSelectedColor";
            public const string UseBakedMesh = Prefix + "UseBakedMesh";
            public const string ChannelWrite = Prefix + "ChannelWrite";
            public const string ChannelWrite_R = Prefix + "ChannelWrite.R";
            public const string ChannelWrite_G = Prefix + "ChannelWrite.G";
            public const string ChannelWrite_B = Prefix + "ChannelWrite.B";
            public const string ChannelWrite_A = Prefix + "ChannelWrite.A";
            public const string BasePNGAssetPath = Prefix + "BasePNG";
            public const string BaseVCMeshAssetPath = Prefix + "BaseVCMesh";
            public const string ColorOptionsFoldout = Prefix + "ColorOptionsFoldout";
            public const string AdvancedOptionsFoldout = Prefix + "AdvancedOptionsFoldout";
            public const string ChannelWriteFoldout = Prefix + "ChannelWriteFoldout";
            public const string Language = Prefix + "Language";
            public const string UseEnglish = Prefix + "UseEnglish";
            public const string SaveInvertedToo = Prefix + "SaveInvertedToo";
            public const string UseTextureFolder = Prefix + "UseTextureFolder";
            public const string WorkCopyOffset = Prefix + "WorkCopyOffset";
            public const string AutoWorkCopy = Prefix + "AutoWorkCopy";
            public const string CurvatureStrength = Prefix + "CurvatureStrength";
            public const string CurvatureMode = Prefix + "CurvatureMode";
            public const string BakeMapsFoldout = Prefix + "BakeMapsFoldout";
        }

        /// <summary>
        /// Loads settings from EditorPrefs and returns a populated MaskSettings instance.
        /// </summary>
        public MaskSettings Load()
        {
            var settings = new MaskSettings();

            settings.TextureSize = EditorPrefs.GetInt(Keys.TextureSize, 512);
            settings.PixelMargin = EditorPrefs.GetInt(Keys.PixelMargin, 2);
            settings.InvertMask = EditorPrefs.GetBool(Keys.InvertMask, false);
            settings.OutputDir = EditorPrefs.GetString(Keys.LastSaveDir, "Assets/GeneratedMasks");
            settings.ModeToggleHotkey = (KeyCode)EditorPrefs.GetInt(Keys.ModeToggleHotkey, (int)KeyCode.R);
            settings.UVChannel = EditorPrefs.GetInt(Keys.UVChannel, 0);
            settings.OverlayOnTop = EditorPrefs.GetBool(Keys.OverlayOnTop, false);
            settings.OverlayDepthOffset = EditorPrefs.GetFloat(Keys.OverlayDepthOffset, 0f);
            settings.OverlaySeamThickness = EditorPrefs.GetFloat(Keys.OverlaySeamThickness, 2.5f);
            settings.DisableAA = EditorPrefs.GetBool(Keys.DisableAA, true);
            settings.BackfaceCull = EditorPrefs.GetBool(Keys.BackfaceCull, true);
            settings.ShowIslandPreview = EditorPrefs.GetBool(Keys.ShowIslandPreview, false);
            settings.PreviewOverlayBaseTex = EditorPrefs.GetBool(Keys.PreviewOverlayBaseTex, false);
            settings.PreviewOverlayAlpha = EditorPrefs.GetFloat(Keys.PreviewOverlayAlpha, 0.6f);
            settings.UseBakedMesh = EditorPrefs.GetBool(Keys.UseBakedMesh, false);
            settings.ChannelWriteEnabled = EditorPrefs.GetBool(Keys.ChannelWrite, false);
            settings.WriteR = EditorPrefs.GetBool(Keys.ChannelWrite_R, true);
            settings.WriteG = EditorPrefs.GetBool(Keys.ChannelWrite_G, false);
            settings.WriteB = EditorPrefs.GetBool(Keys.ChannelWrite_B, false);
            settings.WriteA = EditorPrefs.GetBool(Keys.ChannelWrite_A, false);
            settings.ColorOptionsFoldout = EditorPrefs.GetBool(Keys.ColorOptionsFoldout, false);
            settings.AdvancedOptionsFoldout = EditorPrefs.GetBool(Keys.AdvancedOptionsFoldout, false);
            settings.ChannelWriteFoldout = EditorPrefs.GetBool(Keys.ChannelWriteFoldout, false);
            settings.Language = EditorPrefs.GetString(Keys.Language, "ja");
            settings.UseEnglish = EditorPrefs.GetBool(Keys.UseEnglish, false);
            settings.SaveInvertedToo = EditorPrefs.GetBool(Keys.SaveInvertedToo, false);
            settings.UseTextureFolder = EditorPrefs.GetBool(Keys.UseTextureFolder, false);

            // Load colors (stored as hex strings)
            settings.SelectedSceneColor = LoadColor(Keys.SelectedSceneColor, new Color(0f, 1f, 1f, 1f));
            settings.SeamColor = LoadColor(Keys.SeamColor, new Color(1f, 0.15f, 0.15f, 1f));
            settings.PreviewFillSelectedColor = LoadColor(Keys.PreviewFillSelectedColor, Color.black);

            // Load Work Copy settings
            settings.WorkCopyOffset = LoadVector3(Keys.WorkCopyOffset, new Vector3(1.5f, 0f, 0f));
            settings.AutoWorkCopy = EditorPrefs.GetBool(Keys.AutoWorkCopy, true);

            // Load Curvature settings
            settings.CurvatureStrength = EditorPrefs.GetFloat(Keys.CurvatureStrength, 1.0f);
            settings.CurvatureMode = EditorPrefs.GetInt(Keys.CurvatureMode, 0);
            settings.BakeMapsFoldout = EditorPrefs.GetBool(Keys.BakeMapsFoldout, false);

            return settings;
        }

        /// <summary>
        /// Saves all settings to EditorPrefs.
        /// </summary>
        public void Save(MaskSettings settings)
        {
            EditorPrefs.SetInt(Keys.TextureSize, settings.TextureSize);
            EditorPrefs.SetInt(Keys.PixelMargin, settings.PixelMargin);
            EditorPrefs.SetBool(Keys.InvertMask, settings.InvertMask);
            EditorPrefs.SetString(Keys.LastSaveDir, settings.OutputDir);
            EditorPrefs.SetInt(Keys.ModeToggleHotkey, (int)settings.ModeToggleHotkey);
            EditorPrefs.SetInt(Keys.UVChannel, settings.UVChannel);
            EditorPrefs.SetBool(Keys.OverlayOnTop, settings.OverlayOnTop);
            EditorPrefs.SetFloat(Keys.OverlayDepthOffset, settings.OverlayDepthOffset);
            EditorPrefs.SetFloat(Keys.OverlaySeamThickness, settings.OverlaySeamThickness);
            EditorPrefs.SetBool(Keys.DisableAA, settings.DisableAA);
            EditorPrefs.SetBool(Keys.BackfaceCull, settings.BackfaceCull);
            EditorPrefs.SetBool(Keys.ShowIslandPreview, settings.ShowIslandPreview);
            EditorPrefs.SetBool(Keys.PreviewOverlayBaseTex, settings.PreviewOverlayBaseTex);
            EditorPrefs.SetFloat(Keys.PreviewOverlayAlpha, settings.PreviewOverlayAlpha);
            EditorPrefs.SetBool(Keys.UseBakedMesh, settings.UseBakedMesh);
            EditorPrefs.SetBool(Keys.ChannelWrite, settings.ChannelWriteEnabled);
            EditorPrefs.SetBool(Keys.ChannelWrite_R, settings.WriteR);
            EditorPrefs.SetBool(Keys.ChannelWrite_G, settings.WriteG);
            EditorPrefs.SetBool(Keys.ChannelWrite_B, settings.WriteB);
            EditorPrefs.SetBool(Keys.ChannelWrite_A, settings.WriteA);
            EditorPrefs.SetBool(Keys.ColorOptionsFoldout, settings.ColorOptionsFoldout);
            EditorPrefs.SetBool(Keys.AdvancedOptionsFoldout, settings.AdvancedOptionsFoldout);
            EditorPrefs.SetBool(Keys.ChannelWriteFoldout, settings.ChannelWriteFoldout);
            EditorPrefs.SetString(Keys.Language, settings.Language);
            EditorPrefs.SetBool(Keys.UseEnglish, settings.UseEnglish);
            EditorPrefs.SetBool(Keys.SaveInvertedToo, settings.SaveInvertedToo);
            EditorPrefs.SetBool(Keys.UseTextureFolder, settings.UseTextureFolder);

            // Save colors as hex strings
            SaveColor(Keys.SelectedSceneColor, settings.SelectedSceneColor);
            SaveColor(Keys.SeamColor, settings.SeamColor);
            SaveColor(Keys.PreviewFillSelectedColor, settings.PreviewFillSelectedColor);

            // Save Work Copy settings
            SaveVector3(Keys.WorkCopyOffset, settings.WorkCopyOffset);
            EditorPrefs.SetBool(Keys.AutoWorkCopy, settings.AutoWorkCopy);

            // Save Curvature settings
            EditorPrefs.SetFloat(Keys.CurvatureStrength, settings.CurvatureStrength);
            EditorPrefs.SetInt(Keys.CurvatureMode, settings.CurvatureMode);
            EditorPrefs.SetBool(Keys.BakeMapsFoldout, settings.BakeMapsFoldout);
        }

        /// <summary>
        /// Saves a single setting by key. Use for incremental saves during UI changes.
        /// </summary>
        public void SaveSingle<T>(string key, T value)
        {
            switch (value)
            {
                case int i: EditorPrefs.SetInt(Prefix + key, i); break;
                case float f: EditorPrefs.SetFloat(Prefix + key, f); break;
                case bool b: EditorPrefs.SetBool(Prefix + key, b); break;
                case string s: EditorPrefs.SetString(Prefix + key, s); break;
                case Color c: SaveColor(Prefix + key, c); break;
                case KeyCode k: EditorPrefs.SetInt(Prefix + key, (int)k); break;
            }
        }

        /// <summary>
        /// Gets the asset path for base PNG texture.
        /// </summary>
        public string GetBasePNGPath() => EditorPrefs.GetString(Keys.BasePNGAssetPath, string.Empty);

        /// <summary>
        /// Sets the asset path for base PNG texture.
        /// </summary>
        public void SetBasePNGPath(string path) => EditorPrefs.SetString(Keys.BasePNGAssetPath, path);

        /// <summary>
        /// Gets the asset path for base vertex color mesh.
        /// </summary>
        public string GetBaseVCMeshPath() => EditorPrefs.GetString(Keys.BaseVCMeshAssetPath, string.Empty);

        /// <summary>
        /// Sets the asset path for base vertex color mesh.
        /// </summary>
        public void SetBaseVCMeshPath(string path) => EditorPrefs.SetString(Keys.BaseVCMeshAssetPath, path);

        private Color LoadColor(string key, Color defaultValue)
        {
            var hex = EditorPrefs.GetString(key, ColorUtility.ToHtmlStringRGBA(defaultValue));
            if (ColorUtility.TryParseHtmlString("#" + hex, out var color))
                return color;
            return defaultValue;
        }

        private void SaveColor(string key, Color color)
        {
            EditorPrefs.SetString(key, ColorUtility.ToHtmlStringRGBA(color));
        }

        private Vector3 LoadVector3(string key, Vector3 defaultValue)
        {
            string s = EditorPrefs.GetString(key, "");
            if (string.IsNullOrEmpty(s)) return defaultValue;
            string[] parts = s.Split(',');
            if (parts.Length == 3 &&
                float.TryParse(parts[0], out float x) &&
                float.TryParse(parts[1], out float y) &&
                float.TryParse(parts[2], out float z))
            {
                return new Vector3(x, y, z);
            }
            return defaultValue;
        }

        private void SaveVector3(string key, Vector3 v)
        {
            EditorPrefs.SetString(key, $"{v.x},{v.y},{v.z}");
        }
    }
}
