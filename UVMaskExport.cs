// UVMaskExport.cs - shared export helpers for UV mask PNG writing
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Dennoko.UVTools
{
    public static class UVMaskExport
    {
        // Build label map for the given analysis
        public static int[] BuildLabelMapTransient(UVAnalysis analysis, int width, int height)
        {
            var labels = new int[width * height]; for (int i = 0; i < labels.Length; i++) labels[i] = -1;
            foreach (var pair in analysis.Islands.Select((isl, idx) => new { isl, idx }))
            {
                foreach (var tri in pair.isl.Triangles) { RasterizeTriangleLabel(width, height, labels, pair.idx, tri.uv0, tri.uv1, tri.uv2); }
            }
            return labels;
        }

        private static void RasterizeTriangleLabel(int W, int H, int[] labels, int islandIdx, Vector2 uv0, Vector2 uv1, Vector2 uv2)
        {
            Vector2 p0 = new Vector2(Mathf.Clamp01(uv0.x) * (W - 1), Mathf.Clamp01(uv0.y) * (H - 1));
            Vector2 p1 = new Vector2(Mathf.Clamp01(uv1.x) * (W - 1), Mathf.Clamp01(uv1.y) * (H - 1));
            Vector2 p2 = new Vector2(Mathf.Clamp01(uv2.x) * (W - 1), Mathf.Clamp01(uv2.y) * (H - 1));
            int minX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(p0.x, Mathf.Min(p1.x, p2.x))));
            int maxX = Mathf.Min(W - 1, Mathf.CeilToInt(Mathf.Max(p0.x, Mathf.Max(p1.x, p2.x))));
            int minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(p0.y, Mathf.Min(p1.y, p2.y))));
            int maxY = Mathf.Min(H - 1, Mathf.CeilToInt(Mathf.Max(p0.y, Mathf.Max(p1.y, p2.y))));
            float Edge(Vector2 a, Vector2 b, Vector2 c) => (c.x - a.x) * (b.y - a.y) - (c.y - a.y) * (b.x - a.x);
            float area = Edge(p0, p1, p2); if (Mathf.Approximately(area, 0)) return;
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                    float w0 = Edge(p1, p2, p); float w1 = Edge(p2, p0, p); float w2 = Edge(p0, p1, p);
                    bool inside = (w0 >= 0 && w1 >= 0 && w2 >= 0) || (w0 <= 0 && w1 <= 0 && w2 <= 0);
                    if (!inside) continue; int idx = y * W + x; labels[idx] = islandIdx;
                }
            }
        }

        // Fast dilation on a binary byte mask (0=white, 255=black)
        public static void DilateMaskBytes(byte[] mask, int width, int height, int iterations)
        {
            if (mask == null || width <= 0 || height <= 0 || iterations <= 0) return;
            var src = mask; var dst = new byte[mask.Length];
            for (int it = 0; it < iterations; it++)
            {
                Buffer.BlockCopy(src, 0, dst, 0, src.Length);
                for (int y = 0; y < height; y++)
                {
                    int yOff = y * width;
                    for (int x = 0; x < width; x++)
                    {
                        int idx = yOff + x; if (src[idx] == 255) { dst[idx] = 255; continue; }
                        bool hit = false; int y0 = y > 0 ? y - 1 : y; int y1 = y < height - 1 ? y + 1 : y; int x0 = x > 0 ? x - 1 : x; int x1 = x < width - 1 ? x + 1 : x;
                        for (int ny = y0; ny <= y1 && !hit; ny++) { int nOff = ny * width; for (int nx = x0; nx <= x1; nx++) { if (nx == x && ny == y) continue; if (src[nOff + nx] == 255) { hit = true; break; } } }
                        if (hit) dst[idx] = 255;
                    }
                }
                var tmp = src; src = dst; dst = tmp;
            }
            if (!ReferenceEquals(src, mask)) Buffer.BlockCopy(src, 0, mask, 0, mask.Length);
        }

        // Dilation variant that expands WHITE (0) instead of black; useful when mask is inverted
        // (0=white expands into neighbors, 255=black remains unless neighbor is white)
        public static void DilateWhiteBytes(byte[] mask, int width, int height, int iterations)
        {
            if (mask == null || width <= 0 || height <= 0 || iterations <= 0) return;
            var src = mask; var dst = new byte[mask.Length];
            for (int it = 0; it < iterations; it++)
            {
                Buffer.BlockCopy(src, 0, dst, 0, src.Length);
                for (int y = 0; y < height; y++)
                {
                    int yOff = y * width;
                    for (int x = 0; x < width; x++)
                    {
                        int idx = yOff + x; if (src[idx] == 0) { dst[idx] = 0; continue; }
                        bool hit = false; int y0 = y > 0 ? y - 1 : y; int y1 = y < height - 1 ? y + 1 : y; int x0 = x > 0 ? x - 1 : x; int x1 = x < width - 1 ? x + 1 : x;
                        for (int ny = y0; ny <= y1 && !hit; ny++) { int nOff = ny * width; for (int nx = x0; nx <= x1; nx++) { if (nx == x && ny == y) continue; if (src[nOff + nx] == 0) { hit = true; break; } } }
                        if (hit) dst[idx] = 0; else dst[idx] = 255;
                    }
                }
                var tmp = src; src = dst; dst = tmp;
            }
            if (!ReferenceEquals(src, mask)) Buffer.BlockCopy(src, 0, mask, 0, mask.Length);
        }

        public static void EnsureAssetFolderPath(string assetFolderPath)
        {
            if (string.IsNullOrEmpty(assetFolderPath)) return; assetFolderPath = assetFolderPath.Replace('\\', '/'); if (!assetFolderPath.StartsWith("Assets")) return;
            var parts = assetFolderPath.Split('/'); string current = parts[0];
            for (int i = 1; i < parts.Length; i++) { string next = current + "/" + parts[i]; if (!AssetDatabase.IsValidFolder(next)) { AssetDatabase.CreateFolder(current, parts[i]); } current = next; }
        }

        public static Color32[] LoadBasePixelsOrWhite(Texture2D baseTex, int size)
        {
            if (baseTex == null) return Enumerable.Repeat(new Color32(255, 255, 255, 255), size * size).ToArray();
            Texture2D readable = new Texture2D(baseTex.width, baseTex.height, TextureFormat.RGBA32, false, true);
            var tmpPath = AssetDatabase.GetAssetPath(baseTex);
            var bytes = File.ReadAllBytes(tmpPath);
            readable.LoadImage(bytes);
            var srcW = readable.width; var srcH = readable.height; var srcPix = readable.GetPixels32();
            var basePixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                int sy = Mathf.Clamp(Mathf.RoundToInt((y / (float)size) * (srcH - 1)), 0, srcH - 1);
                for (int x = 0; x < size; x++)
                {
                    int sx = Mathf.Clamp(Mathf.RoundToInt((x / (float)size) * (srcW - 1)), 0, srcW - 1);
                    basePixels[y * size + x] = srcPix[sy * srcW + sx];
                }
            }
            UnityEngine.Object.DestroyImmediate(readable);
            return basePixels;
        }

        public static void WritePngAtPath(string path, Color32[] pixels, int size)
        {
            var outTex = new Texture2D(size, size, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            outTex.SetPixels32(pixels); outTex.Apply(false, false);
            var outPNG = outTex.EncodeToPNG(); UnityEngine.Object.DestroyImmediate(outTex);
            File.WriteAllBytes(path, outPNG);
            AssetDatabase.ImportAsset(path);

            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                importer.isReadable = true;
                importer.streamingMipmaps = true;
                importer.SaveAndReimport();
            }
        }
    }
}
