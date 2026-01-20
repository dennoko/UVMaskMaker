// PngExporter.cs - PNG implementation of IMaskExporter
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Dennoko.UVTools.Core;

namespace Dennoko.UVTools.Services
{
    /// <summary>
    /// Exports UV masks as PNG files.
    /// Supports both simple black/white export and channel-wise writing.
    /// </summary>
    public class PngExporter : IMaskExporter
    {
        public string FileExtension => "png";
        public string DisplayName => "PNG Image";

        /// <summary>
        /// Exports the mask to a PNG file.
        /// </summary>
        public bool Export(
            UVAnalysis analysis,
            HashSet<int> selectedIslands,
            ExportSettings settings,
            string path)
        {
            if (analysis == null || string.IsNullOrEmpty(path))
                return false;

            try
            {
                int size = Mathf.Clamp(settings.TextureSize, 8, 8192);
                var mask = MaskBuilder.BuildProcessedMask(
                    analysis,
                    selectedIslands,
                    size,
                    size,
                    settings.PixelMargin,
                    settings.InvertMask);

                Color32[] pixels;

                if (!settings.ChannelWriteEnabled)
                {
                    // Classic black/white export
                    pixels = new Color32[mask.Length];
                    for (int i = 0; i < mask.Length; i++)
                    {
                        pixels[i] = mask[i] != 0
                            ? new Color32(0, 0, 0, 255)
                            : new Color32(255, 255, 255, 255);
                    }
                }
                else
                {
                    // Channel-wise export
                    pixels = LoadBasePixelsOrWhite(settings.BasePNG, size);
                    bool hasBase = settings.BasePNG != null;

                    for (int i = 0; i < pixels.Length; i++)
                    {
                        bool selected = mask[i] != 0;
                        byte v = (byte)(selected ? 0 : 255);
                        var c = pixels[i];

                        if (hasBase)
                        {
                            // Only overwrite selected pixels
                            if (selected)
                            {
                                if (settings.WriteR) c.r = v;
                                if (settings.WriteG) c.g = v;
                                if (settings.WriteB) c.b = v;
                                if (settings.WriteA) c.a = 255;
                            }
                        }
                        else
                        {
                            // No base: write full channel from mask
                            if (settings.WriteR) c.r = v;
                            if (settings.WriteG) c.g = v;
                            if (settings.WriteB) c.b = v;
                            if (settings.WriteA) c.a = mask[i];
                        }

                        pixels[i] = c;
                    }
                }

                Dennoko.UVTools.UVMaskExport.WritePngAtPath(path, pixels, size);
                return true;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[PngExporter] Export failed: {ex.Message}\n{ex}");
                return false;
            }
        }

        private Color32[] LoadBasePixelsOrWhite(Texture2D baseTex, int size)
        {
            if (baseTex == null)
            {
                return Enumerable.Repeat(new Color32(255, 255, 255, 255), size * size).ToArray();
            }

            return Dennoko.UVTools.UVMaskExport.LoadBasePixelsOrWhite(baseTex, size);
        }
    }
}
