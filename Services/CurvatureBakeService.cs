using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Dennoko.UVTools.Data;

namespace Dennoko.UVTools.Services
{
    public class CurvatureBakeService
    {
        // Must use relative path from Assets if using LoadAssetAtPath
        private const string WorldNormalShaderPath = "Assets/Editor/UVMaskMaker/res/Shaders/WorldNormalBake.shader";
        private const string CurvatureOpsShaderPath = "Assets/Editor/UVMaskMaker/res/Shaders/CurvatureOps.shader";

        private Material _worldNormalMat;
        private Material _curvatureOpsMat;

        public CurvatureBakeService()
        {
            var s1 = AssetDatabase.LoadAssetAtPath<Shader>(WorldNormalShaderPath);
            if (!s1) s1 = Shader.Find("Hidden/Dennoko/UVTools/WorldNormalBake"); // Fallback
            if (s1) _worldNormalMat = new Material(s1);
            
            var s2 = AssetDatabase.LoadAssetAtPath<Shader>(CurvatureOpsShaderPath);
            if (!s2) s2 = Shader.Find("Hidden/Dennoko/UVTools/CurvatureOps"); // Fallback
            if (s2) _curvatureOpsMat = new Material(s2);
        }

        public void Dispose()
        {
            if (_worldNormalMat) Object.DestroyImmediate(_worldNormalMat);
            if (_curvatureOpsMat) Object.DestroyImmediate(_curvatureOpsMat);
        }

        public bool BakeCurvatureMap(Renderer targetRenderer, Mesh targetMesh, MaskSettings settings, string fullPath)
        {
            if (targetRenderer == null || targetMesh == null) return false;
            if (_worldNormalMat == null || _curvatureOpsMat == null)
            {
                Debug.LogError("[CurvatureBake] Shaders missing.");
                return false;
            }

            // 1. Calculate Geometry Curvature (Vertex Colors)
            // We use a temporary mesh copy to store vertex colors
            Mesh tempMesh = Object.Instantiate(targetMesh);
            CalculateVertexCurvature(tempMesh);

            // 2. Prepare RenderTextures
            int size = settings.TextureSize;
            RenderTexture rtWorldNormal = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32);
            RenderTexture rtCurvature = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.R8);
            RenderTexture rtTemp = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.R8);

            try
            {
                // Setup World Normal Material
                Material targetMat = targetRenderer.sharedMaterial;
                Texture bumpMap = null;
                if (targetMat != null && targetMat.HasProperty("_BumpMap"))
                {
                    bumpMap = targetMat.GetTexture("_BumpMap");
                }
                _worldNormalMat.SetTexture("_BumpMap", bumpMap);
                _worldNormalMat.SetTexture("_MainTex", targetMat != null ? targetMat.mainTexture : null); // For alpha test if needed

                // Pass 1: Draw World Normal + Vertex Curvature
                RenderTexture.active = rtWorldNormal;
                GL.Clear(true, true, new Color(0.5f, 0.5f, 1.0f, 0.0f)); // Normal neutral Z+, Curvature 0
                
                // We draw the mesh using Graphics.DrawMeshNow or similar
                // But DrawMeshNow renders to active RT immediately.
                if (_worldNormalMat.SetPass(0))
                {
                    Graphics.DrawMeshNow(tempMesh, Matrix4x4.identity);
                }

                // Pass 2: Calculate Curvature (Image Effect)
                _curvatureOpsMat.SetFloat("_Strength", settings.CurvatureStrength);
                _curvatureOpsMat.SetInt("_Mode", settings.CurvatureMode);
                Graphics.Blit(rtWorldNormal, rtCurvature, _curvatureOpsMat, 0); // Pass 0: Calc

                // Pass 3: Dilation (Padding)
                // Iterate a few times
                int iterations = settings.PixelMargin > 0 ? settings.PixelMargin : 2;
                for (int i = 0; i < iterations; i++)
                {
                    Graphics.Blit(rtCurvature, rtTemp, _curvatureOpsMat, 1); // Pass 1: Dilation
                    Graphics.Blit(rtTemp, rtCurvature);
                }

                // 3. Save to PNG
                WriteRenderTextureToPng(rtCurvature, fullPath);

                Debug.Log($"[CurvatureBake] Saved to {fullPath}");
                return true;
            }
            finally
            {
                RenderTexture.ReleaseTemporary(rtWorldNormal);
                RenderTexture.ReleaseTemporary(rtCurvature);
                RenderTexture.ReleaseTemporary(rtTemp);
                if (tempMesh != null) Object.DestroyImmediate(tempMesh);
            }
        }

        private void WriteRenderTextureToPng(RenderTexture rt, string path)
        {
            RenderTexture.active = rt;
            Texture2D tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;

            byte[] bytes = tex.EncodeToPNG();
            Object.DestroyImmediate(tex);

            File.WriteAllBytes(path, bytes);
            AssetDatabase.ImportAsset(path);

            // Import settings
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                importer.isReadable = true;
                importer.streamingMipmaps = true;
                importer.SaveAndReimport();
            }
        }

        private void CalculateVertexCurvature(Mesh mesh)
        {
            // Analyze hard edges by grouping vertices at same position
            Vector3[] verts = mesh.vertices;
            Vector3[] normals = mesh.normals;
            int count = verts.Length;
            Color[] colors = new Color[count]; // Store curvature in R

            // Group by position
            // Simple approach: Dictionary<Vector3, List<int>>
            // Be careful with precision.
            var posMap = new Dictionary<Vector3, List<int>>();
            for (int i = 0; i < count; i++)
            {
                if (!posMap.TryGetValue(verts[i], out var list))
                {
                    list = new List<int>();
                    posMap[verts[i]] = list;
                }
                list.Add(i);
            }

            foreach (var kvp in posMap)
            {
                var indices = kvp.Value;
                if (indices.Count == 1)
                {
                    // Smooth vertex (or unconnected). Curvature 0 (or dependent on neighbor?)
                    // For now 0.
                    colors[indices[0]] = Color.black;
                    continue;
                }

                // Calculate smooth normal (average)
                Vector3 smoothNormal = Vector3.zero;
                foreach (int idx in indices) smoothNormal += normals[idx];
                smoothNormal.Normalize();

                foreach (int idx in indices)
                {
                    // Calculate deviation from smooth normal
                    // Dot product: 1 = same, 0 = 90 deg, -1 = 180 deg.
                    // Curvature = 1 - dot
                    float dot = Vector3.Dot(normals[idx], smoothNormal);
                    float curve = Mathf.Clamp01(1.0f - dot); 
                    
                    // Curve is high (1) if simple normal deviates from average.
                    // This happens at hard edges.
                    
                    // Note: This detects "Sharpness". Convert to color.
                    colors[idx] = new Color(curve, 0, 0, 1);
                }
            }

            mesh.colors = colors;
        }
    }
}
