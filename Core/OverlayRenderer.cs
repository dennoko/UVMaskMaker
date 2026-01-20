// OverlayRenderer.cs - Handles SceneView overlay drawing for UV seams and islands
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Pool;
using Dennoko.UVTools.Data;

namespace Dennoko.UVTools.Core
{
    /// <summary>
    /// Renders UV seams and selected island overlays in the SceneView.
    /// Caches world-space geometry for performance.
    /// </summary>
    public class OverlayRenderer
    {
        // Cached world-space data
        private Vector3[] _worldPosBase;
        private Vector3[] _worldNormal;
        private Matrix4x4 _lastLocalToWorld;
        private bool _cacheValid = false;

        /// <summary>
        /// Invalidates the cached overlay geometry.
        /// Call when mesh or transform changes.
        /// </summary>
        public void InvalidateCache()
        {
            _cacheValid = false;
        }

        /// <summary>
        /// Draws seam edges in the scene view.
        /// </summary>
        /// <param name="analysis">UV analysis containing border edges</param>
        /// <param name="transform">Target transform</param>
        /// <param name="settings">Current mask settings</param>
        /// <param name="bakedMesh">Optional baked mesh for SkinnedMeshRenderer</param>
        /// <param name="useBakedMesh">Whether to use baked mesh</param>
        public void DrawSeams(
            UVAnalysis analysis,
            Transform transform,
            MaskSettings settings,
            Mesh bakedMesh = null,
            bool useBakedMesh = false)
        {
            if (analysis == null || transform == null) return;
            if (analysis.BorderEdges == null || analysis.BorderEdges.Count == 0) return;

            EnsureWorldCache(analysis, transform, bakedMesh, useBakedMesh);
            if (_worldPosBase == null || _worldNormal == null) return;

            Handles.zTest = settings.OverlayOnTop
                ? UnityEngine.Rendering.CompareFunction.Always
                : UnityEngine.Rendering.CompareFunction.LessEqual;
            Handles.color = settings.SeamColor;

            if (settings.DisableAA)
            {
                var seamVerts = ListPool<Vector3>.Get();
                foreach (var be in analysis.BorderEdges)
                {
                    if ((uint)be.v0 >= _worldPosBase.Length || (uint)be.v1 >= _worldPosBase.Length) continue;
                    var a = _worldPosBase[be.v0] + _worldNormal[be.v0] * settings.OverlayDepthOffset;
                    var b = _worldPosBase[be.v1] + _worldNormal[be.v1] * settings.OverlayDepthOffset;
                    seamVerts.Add(a);
                    seamVerts.Add(b);
                }
                if (seamVerts.Count >= 2) Handles.DrawLines(seamVerts.ToArray());
                ListPool<Vector3>.Release(seamVerts);
            }
            else
            {
                foreach (var be in analysis.BorderEdges)
                {
                    if ((uint)be.v0 >= _worldPosBase.Length || (uint)be.v1 >= _worldPosBase.Length) continue;
                    var a = _worldPosBase[be.v0] + _worldNormal[be.v0] * settings.OverlayDepthOffset;
                    var b = _worldPosBase[be.v1] + _worldNormal[be.v1] * settings.OverlayDepthOffset;
                    Handles.DrawAAPolyLine(settings.OverlaySeamThickness, a, b);
                }
            }
        }

        /// <summary>
        /// Draws selected island wireframes in the scene view.
        /// </summary>
        /// <param name="analysis">UV analysis containing islands</param>
        /// <param name="selectedIslands">Set of selected island indices</param>
        /// <param name="transform">Target transform</param>
        /// <param name="settings">Current mask settings</param>
        /// <param name="renderer">Target renderer for bounds calculation</param>
        /// <param name="sceneView">Active scene view</param>
        /// <param name="bakedMesh">Optional baked mesh</param>
        /// <param name="useBakedMesh">Whether to use baked mesh</param>
        public void DrawSelectedIslands(
            UVAnalysis analysis,
            HashSet<int> selectedIslands,
            Transform transform,
            MaskSettings settings,
            Renderer renderer,
            SceneView sceneView,
            Mesh bakedMesh = null,
            bool useBakedMesh = false)
        {
            if (analysis == null || transform == null || selectedIslands == null || selectedIslands.Count == 0)
                return;

            EnsureWorldCache(analysis, transform, bakedMesh, useBakedMesh);
            if (_worldPosBase == null || _worldNormal == null) return;

            Handles.zTest = settings.OverlayOnTop
                ? UnityEngine.Rendering.CompareFunction.Always
                : UnityEngine.Rendering.CompareFunction.LessEqual;
            Handles.color = settings.SelectedSceneColor;

            // Calculate thickness based on distance
            float thicknessBase = Mathf.Max(0.5f, settings.OverlaySeamThickness);
            float distForScale = 1f;
            Camera cam = sceneView?.camera ?? SceneView.lastActiveSceneView?.camera;
            if (cam != null && renderer != null)
            {
                var bounds = renderer.bounds;
                distForScale = Mathf.Max(0.1f, Vector3.Distance(cam.transform.position, bounds.center));
            }
            float thickness = Mathf.Clamp(thicknessBase / distForScale, 0.5f, thicknessBase);
            Vector3 camPos = cam != null ? cam.transform.position : Vector3.zero;

            if (settings.DisableAA)
            {
                var lineVerts = ListPool<Vector3>.Get();
                foreach (var idx in selectedIslands)
                {
                    if (idx < 0 || idx >= analysis.Islands.Count) continue;
                    var island = analysis.Islands[idx];
                    foreach (var tri in island.Triangles)
                    {
                        if ((uint)tri.v0 >= _worldPosBase.Length || (uint)tri.v1 >= _worldPosBase.Length || (uint)tri.v2 >= _worldPosBase.Length)
                            continue;

                        var a = _worldPosBase[tri.v0] + _worldNormal[tri.v0] * settings.OverlayDepthOffset;
                        var b = _worldPosBase[tri.v1] + _worldNormal[tri.v1] * settings.OverlayDepthOffset;
                        var c = _worldPosBase[tri.v2] + _worldNormal[tri.v2] * settings.OverlayDepthOffset;

                        if (settings.BackfaceCull && cam != null)
                        {
                            var n = Vector3.Cross(b - a, c - a);
                            if (n.sqrMagnitude > 1e-12f)
                            {
                                var center = (a + b + c) / 3f;
                                if (Vector3.Dot(n.normalized, camPos - center) <= 0f) continue;
                            }
                        }

                        lineVerts.Add(a); lineVerts.Add(b);
                        lineVerts.Add(b); lineVerts.Add(c);
                        lineVerts.Add(c); lineVerts.Add(a);
                    }
                }
                if (lineVerts.Count >= 2) Handles.DrawLines(lineVerts.ToArray());
                ListPool<Vector3>.Release(lineVerts);
            }
            else
            {
                foreach (var idx in selectedIslands)
                {
                    if (idx < 0 || idx >= analysis.Islands.Count) continue;
                    var island = analysis.Islands[idx];
                    foreach (var tri in island.Triangles)
                    {
                        if ((uint)tri.v0 >= _worldPosBase.Length || (uint)tri.v1 >= _worldPosBase.Length || (uint)tri.v2 >= _worldPosBase.Length)
                            continue;

                        float baseOffset = settings.OverlayDepthOffset;
                        var a = _worldPosBase[tri.v0] + _worldNormal[tri.v0] * baseOffset;
                        var b = _worldPosBase[tri.v1] + _worldNormal[tri.v1] * baseOffset;
                        var c = _worldPosBase[tri.v2] + _worldNormal[tri.v2] * baseOffset;

                        if (settings.BackfaceCull && cam != null)
                        {
                            var n = Vector3.Cross(b - a, c - a);
                            if (n.sqrMagnitude > 1e-12f)
                            {
                                var center = (a + b + c) / 3f;
                                if (Vector3.Dot(n.normalized, camPos - center) <= 0f) continue;
                            }
                        }

                        Handles.DrawAAPolyLine(thickness, a, b);
                        Handles.DrawAAPolyLine(thickness, b, c);
                        Handles.DrawAAPolyLine(thickness, c, a);
                    }
                }
            }
        }

        /// <summary>
        /// Ensures world-space vertex cache is up to date.
        /// </summary>
        private void EnsureWorldCache(UVAnalysis analysis, Transform transform, Mesh bakedMesh, bool useBakedMesh)
        {
            var l2w = transform.localToWorldMatrix;
            var mesh = (useBakedMesh && bakedMesh != null) ? bakedMesh : null;

            bool needRebuild = !_cacheValid || _worldPosBase == null || _worldNormal == null || _lastLocalToWorld != l2w;

            int expectedCount = mesh != null ? mesh.vertexCount : analysis.Vertices.Count;
            if (_worldPosBase == null || _worldPosBase.Length != expectedCount)
                needRebuild = true;

            if (!needRebuild) return;

            int vCount = expectedCount;
            if (_worldPosBase == null || _worldPosBase.Length != vCount)
                _worldPosBase = new Vector3[vCount];
            if (_worldNormal == null || _worldNormal.Length != vCount)
                _worldNormal = new Vector3[vCount];

            if (mesh != null)
            {
                var verts = mesh.vertices;
                var norms = mesh.normals;
                for (int i = 0; i < vCount; i++)
                {
                    var lp = (i < verts.Length) ? verts[i] : Vector3.zero;
                    var ln = (norms != null && i < norms.Length) ? norms[i] : Vector3.up;
                    if (ln.sqrMagnitude < 1e-8f) ln = Vector3.up;
                    _worldPosBase[i] = transform.TransformPoint(lp);
                    _worldNormal[i] = transform.TransformDirection(ln).normalized;
                }
            }
            else
            {
                for (int i = 0; i < vCount; i++)
                {
                    var lp = analysis.Vertices[i];
                    var ln = (i < analysis.Normals.Count) ? analysis.Normals[i] : Vector3.up;
                    if (ln.sqrMagnitude < 1e-8f) ln = Vector3.up;
                    _worldPosBase[i] = transform.TransformPoint(lp);
                    _worldNormal[i] = transform.TransformDirection(ln).normalized;
                }
            }

            _lastLocalToWorld = l2w;
            _cacheValid = true;
        }
    }
}
