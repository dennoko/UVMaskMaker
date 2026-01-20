// UVVertexColorBaker.cs - shared helpers to bake mask into vertex colors and save as a new Mesh asset
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Dennoko.UVTools
{
    public static class UVVertexColorBaker
    {
        // Build vertex colors (simple): selected islands -> black (0,0,0,1), others -> white (1,1,1,1)
        public static Color32[] BuildVertexColors(UVAnalysis analysis, HashSet<int> selectedIslands, int vertexCount)
        {
            var colors = new Color32[vertexCount];
            var white = new Color32(255, 255, 255, 255);
            for (int i = 0; i < vertexCount; i++) colors[i] = white;

            if (analysis == null || analysis.Triangles == null) return colors;

            foreach (var tri in analysis.Triangles)
            {
                int isl;
                if (!analysis.TriangleToIsland.TryGetValue(tri.triIndex, out isl)) continue;
                bool selected = selectedIslands != null && selectedIslands.Contains(isl);
                if (!selected) continue;
                // Selected triangle -> set its vertices to black
                colors[tri.v0] = new Color32(0, 0, 0, 255);
                colors[tri.v1] = new Color32(0, 0, 0, 255);
                colors[tri.v2] = new Color32(0, 0, 0, 255);
            }
            return colors;
        }

        // Channel-wise vertex color build, mirroring PNG logic:
        // - If baseColors provided (length==vertexCount), only overwrite selected vertices; others remain base.
        // - If baseColors null or wrong length, treat base as white and write full mask.
        public static Color32[] BuildVertexColorsChannelWise(
            UVAnalysis analysis,
            HashSet<int> selectedIslands,
            int vertexCount,
            Color32[] baseColors,
            bool writeR, bool writeG, bool writeB, bool writeA)
        {
            if (vertexCount <= 0) return Array.Empty<Color32>();
            var hasBase = baseColors != null && baseColors.Length == vertexCount;
            var colors = new Color32[vertexCount];
            if (hasBase) Array.Copy(baseColors, colors, vertexCount);
            else
            {
                var white = new Color32(255, 255, 255, 255);
                for (int i = 0; i < vertexCount; i++) colors[i] = white;
            }

            // Build per-vertex selection mask from selected triangles
            var selVert = new bool[vertexCount];
            if (analysis != null && analysis.Triangles != null && selectedIslands != null && selectedIslands.Count > 0)
            {
                foreach (var tri in analysis.Triangles)
                {
                    if (!analysis.TriangleToIsland.TryGetValue(tri.triIndex, out int isl)) continue;
                    if (!selectedIslands.Contains(isl)) continue;
                    if ((uint)tri.v0 < selVert.Length) selVert[tri.v0] = true;
                    if ((uint)tri.v1 < selVert.Length) selVert[tri.v1] = true;
                    if ((uint)tri.v2 < selVert.Length) selVert[tri.v2] = true;
                }
            }

            if (hasBase)
            {
                // Only overwrite selected vertices
                for (int i = 0; i < vertexCount; i++)
                {
                    if (!selVert[i]) continue;
                    var c = colors[i];
                    byte vRGB = 0; // selected -> black in RGB
                    if (writeR) c.r = vRGB;
                    if (writeG) c.g = vRGB;
                    if (writeB) c.b = vRGB;
                    if (writeA) c.a = 255; // selected -> opaque
                    colors[i] = c;
                }
            }
            else
            {
                // No base: write full channel from mask
                for (int i = 0; i < vertexCount; i++)
                {
                    bool selected = selVert[i];
                    var c = colors[i];
                    byte vRGB = (byte)(selected ? 0 : 255);
                    if (writeR) c.r = vRGB;
                    if (writeG) c.g = vRGB;
                    if (writeB) c.b = vRGB;
                    if (writeA) c.a = (byte)(selected ? 255 : 0);
                    colors[i] = c;
                }
            }
            return colors;
        }

        // Duplicate a mesh and assign vertex colors; copies most attributes conservatively
        public static Mesh CreateColoredMesh(Mesh source, Color32[] colors)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            var m = UnityEngine.Object.Instantiate(source);
            m.name = source.name + "_VC";
            if (colors != null && colors.Length == source.vertexCount) m.colors32 = colors;
            // Safety: ensure bounds are up to date
            m.RecalculateBounds();
            return m;
        }

        // Compute default bake folder next to mesh asset: <mesh-dir>/VertexColorMasks
        public static string GetDefaultBakeFolderForMesh(Mesh mesh)
        {
            var meshPath = AssetDatabase.GetAssetPath(mesh);
            if (string.IsNullOrEmpty(meshPath)) return "Assets/VertexColorMasks";
            var dir = Path.GetDirectoryName(meshPath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(dir)) dir = "Assets";
            // If already inside VertexColorMasks, use that folder; else create/use a subfolder
            var last = Path.GetFileName(dir)?.Replace('\\', '/');
            string folder;
            if (string.Equals(last, "VertexColorMasks", StringComparison.OrdinalIgnoreCase)) folder = dir;
            else folder = dir + "/VertexColorMasks";
            UVMaskExport.EnsureAssetFolderPath(folder);
            return folder;
        }

        // Save mesh asset at the given folder with file name; returns final asset path
        public static string SaveMeshAsset(Mesh mesh, string folder, string fileNameNoExt, bool overwriteExisting = false)
        {
            if (string.IsNullOrEmpty(folder)) folder = "Assets";
            UVMaskExport.EnsureAssetFolderPath(folder);
            string path = folder.TrimEnd('/') + "/" + Sanitize(fileNameNoExt) + ".asset";
            if (overwriteExisting && AssetDatabase.LoadAssetAtPath<Mesh>(path) != null)
            {
                AssetDatabase.DeleteAsset(path);
            }
            else
            {
                path = AssetDatabase.GenerateUniqueAssetPath(path);
            }
            AssetDatabase.CreateAsset(mesh, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(path);
            return path;
        }

        private static string Sanitize(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "NewMesh";
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name;
        }
    }
}
