// MaskSettings.cs - Data class for UV Mask Maker settings
using UnityEngine;

namespace Dennoko.UVTools.Data
{
    /// <summary>
    /// Holds all configurable settings for the UV Mask Maker tool.
    /// This is a pure data class (POCO) with no behavior.
    /// </summary>
    [System.Serializable]
    public class MaskSettings
    {
        // Texture export settings
        public int TextureSize = 512;
        public int PixelMargin = 2;
        public bool InvertMask = false;
        public bool SaveInvertedToo = false;  // Save both normal and inverted mask
        public bool UseTextureFolder = false;  // Use main texture folder for output
        public string OutputDir = "Assets/GeneratedMasks";
        public string FileName = "uv_mask";

        // Channel-wise export settings
        public bool ChannelWriteEnabled = false;
        public bool WriteR = true;
        public bool WriteG = false;
        public bool WriteB = false;
        public bool WriteA = false;

        // Mode settings
        public bool AddMode = true;
        public KeyCode ModeToggleHotkey = KeyCode.R;

        // UV settings
        public int UVChannel = 0;

        // Scene overlay settings
        public bool OverlayOnTop = false;
        public float OverlayDepthOffset = 0f;
        public float OverlaySeamThickness = 2.5f;
        public bool DisableAA = true;
        public bool BackfaceCull = true;

        // Preview settings
        public bool ShowIslandPreview = false;
        public bool PreviewOverlayBaseTex = false;
        public float PreviewOverlayAlpha = 0.6f;

        // Color settings
        public Color SelectedSceneColor = new Color(0f, 1f, 1f, 1f);
        public Color SeamColor = new Color(1f, 0.15f, 0.15f, 1f);
        public Color PreviewFillSelectedColor = Color.black;

        // Skinned mesh settings
        public bool UseBakedMesh = false;

        // Vertex color bake settings
        public bool OverwriteExistingVC = false;

        // Foldout states (UI state, but persisted)
        public bool ColorOptionsFoldout = false;
        public bool AdvancedOptionsFoldout = false;
        public bool ChannelWriteFoldout = false;

        // Language setting
        public string Language = "ja";
        public bool UseEnglish = false;  // Enable English Mode

        /// <summary>
        /// Creates a copy of the current settings.
        /// </summary>
        public MaskSettings Clone()
        {
            return (MaskSettings)this.MemberwiseClone();
        }
    }
}
