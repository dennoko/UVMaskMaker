// MaskBuilder.cs - Core logic for building UV mask textures
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Dennoko.UVTools.Core
{
    /// <summary>
    /// Provides static methods for building UV mask textures.
    /// Handles rasterization, dilation, and inversion of mask data.
    /// </summary>
    public static class MaskBuilder
    {
        /// <summary>
        /// Builds a union mask from selected UV islands.
        /// </summary>
        /// <param name="analysis">UV analysis result</param>
        /// <param name="selectedIslands">Set of selected island indices</param>
        /// <param name="width">Output mask width</param>
        /// <param name="height">Output mask height</param>
        /// <returns>Byte array where 255 = selected, 0 = unselected</returns>
        public static byte[] BuildUnionMask(UVAnalysis analysis, HashSet<int> selectedIslands, int width, int height)
        {
            var mask = new byte[width * height];
            Array.Clear(mask, 0, mask.Length);

            if (analysis == null || selectedIslands == null || selectedIslands.Count == 0)
                return mask;

            foreach (var islandIdx in selectedIslands)
            {
                if (islandIdx < 0 || islandIdx >= analysis.Islands.Count) continue;

                var island = analysis.Islands[islandIdx];
                foreach (var tri in island.Triangles)
                {
                    RasterizeTriangleToMask(width, height, mask, tri.uv0, tri.uv1, tri.uv2);
                }
            }

            return mask;
        }

        /// <summary>
        /// Inverts maskmask in place (0 becomes 255, 255 becomes 0).
        /// </summary>
        public static void InvertMask(byte[] mask)
        {
            if (mask == null) return;
            for (int i = 0; i < mask.Length; i++)
            {
                mask[i] = (byte)(mask[i] == 0 ? 255 : 0);
            }
        }

        /// <summary>
        /// Dilates black (255) regions by the specified number of pixels.
        /// </summary>
        public static void DilateMask(byte[] mask, int width, int height, int iterations)
        {
            Dennoko.UVTools.UVMaskExport.DilateMaskBytes(mask, width, height, iterations);
        }

        /// <summary>
        /// Dilates white (0) regions by the specified number of pixels.
        /// Used when mask is inverted.
        /// </summary>
        public static void DilateWhite(byte[] mask, int width, int height, int iterations)
        {
            Dennoko.UVTools.UVMaskExport.DilateWhiteBytes(mask, width, height, iterations);
        }

        /// <summary>
        /// Builds complete mask with all processing steps applied.
        /// </summary>
        /// <param name="analysis">UV analysis result</param>
        /// <param name="selectedIslands">Set of selected island indices</param>
        /// <param name="width">Output mask width</param>
        /// <param name="height">Output mask height</param>
        /// <param name="pixelMargin">Number of pixels to dilate</param>
        /// <param name="invertMask">Whether to invert the mask</param>
        /// <returns>Processed byte mask</returns>
        public static byte[] BuildProcessedMask(
            UVAnalysis analysis,
            HashSet<int> selectedIslands,
            int width,
            int height,
            int pixelMargin,
            bool invertMask)
        {
            var mask = BuildUnionMask(analysis, selectedIslands, width, height);

            if (invertMask)
            {
                InvertMask(mask);
            }

            if (pixelMargin > 0)
            {
                if (invertMask)
                {
                    DilateWhite(mask, width, height, pixelMargin);
                }
                else
                {
                    DilateMask(mask, width, height, pixelMargin);
                }
            }

            return mask;
        }

        /// <summary>
        /// Converts byte mask to Color32 array for texture rendering.
        /// </summary>
        /// <param name="mask">Source byte mask</param>
        /// <param name="selectedColor">Color for selected (255) pixels</param>
        /// <param name="unselectedColor">Color for unselected (0) pixels</param>
        /// <returns>Color32 array for texture</returns>
        public static Color32[] MaskToColors(byte[] mask, Color32 selectedColor, Color32 unselectedColor)
        {
            var pixels = new Color32[mask.Length];
            for (int i = 0; i < mask.Length; i++)
            {
                pixels[i] = mask[i] != 0 ? selectedColor : unselectedColor;
            }
            return pixels;
        }

        /// <summary>
        /// Converts byte mask to semi-transparent overlay for preview.
        /// </summary>
        /// <param name="mask">Source byte mask</param>
        /// <param name="selectedColor">Color for selected pixels (alpha will be used from this)</param>
        /// <param name="alpha">Overlay alpha (0-1)</param>
        /// <returns>Color32 array with transparent unselected regions</returns>
        public static Color32[] MaskToOverlay(byte[] mask, Color selectedColor, float alpha)
        {
            var overlay = new Color32[mask.Length];
            byte a = (byte)Mathf.Clamp(Mathf.RoundToInt(alpha * 255f), 0, 255);
            var col = (Color32)selectedColor;
            col.a = a;
            var transparent = new Color32(0, 0, 0, 0);

            for (int i = 0; i < mask.Length; i++)
            {
                overlay[i] = mask[i] != 0 ? col : transparent;
            }

            return overlay;
        }

        /// <summary>
        /// Rasterizes a UV triangle to the mask buffer.
        /// </summary>
        private static void RasterizeTriangleToMask(int W, int H, byte[] dst, Vector2 uv0, Vector2 uv1, Vector2 uv2)
        {
            Vector2 p0 = new Vector2(Mathf.Clamp01(uv0.x) * (W - 1), Mathf.Clamp01(uv0.y) * (H - 1));
            Vector2 p1 = new Vector2(Mathf.Clamp01(uv1.x) * (W - 1), Mathf.Clamp01(uv1.y) * (H - 1));
            Vector2 p2 = new Vector2(Mathf.Clamp01(uv2.x) * (W - 1), Mathf.Clamp01(uv2.y) * (H - 1));

            int minX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(p0.x, Mathf.Min(p1.x, p2.x))));
            int maxX = Mathf.Min(W - 1, Mathf.CeilToInt(Mathf.Max(p0.x, Mathf.Max(p1.x, p2.x))));
            int minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(p0.y, Mathf.Min(p1.y, p2.y))));
            int maxY = Mathf.Min(H - 1, Mathf.CeilToInt(Mathf.Max(p0.y, Mathf.Max(p1.y, p2.y))));

            float Edge(Vector2 a, Vector2 b, Vector2 c) => (c.x - a.x) * (b.y - a.y) - (c.y - a.y) * (b.x - a.x);
            float area = Edge(p0, p1, p2);
            if (Mathf.Approximately(area, 0)) return;
            bool topLeftRule = area > 0;

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                    float w0 = Edge(p1, p2, p);
                    float w1 = Edge(p2, p0, p);
                    float w2 = Edge(p0, p1, p);
                    bool inside = (w0 >= 0 && w1 >= 0 && w2 >= 0) || (w0 <= 0 && w1 <= 0 && w2 <= 0);

                    if (!inside && topLeftRule)
                    {
                        inside = (w0 == 0 && ((p2.y > p1.y) || (p2.y == p1.y && p2.x < p1.x))) ||
                                 (w1 == 0 && ((p0.y > p2.y) || (p0.y == p2.y && p0.x < p2.x))) ||
                                 (w2 == 0 && ((p1.y > p0.y) || (p1.y == p0.y && p1.x < p0.x)));
                    }

                    if (!inside) continue;
                    dst[y * W + x] = 255;
                }
            }
        }
    }
}
