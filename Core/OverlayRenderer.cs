// OverlayRenderer.cs - Handles SceneView overlay drawing for UV seams and islands
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Pool;
using Dennoko.UVTools.Data;

namespace Dennoko.UVTools.Core
{
    /// <summary>
    /// Renders UV seams and mask overlay in the SceneView.
    ///
    /// Seam rendering uses Unity's Handles API (line drawing).
    /// Mask overlay rendering uses GL immediate mode with a transparent
    /// UV-sampled shader (Hidden/UVMaskMaker/MaskOverlay) that blends the
    /// mask preview texture — including hand-painted regions — onto the 3D mesh
    /// surface via screen-space compositing (Blend SrcAlpha OneMinusSrcAlpha).
    ///
    /// Caches world-space geometry for performance.
    /// </summary>
    public class OverlayRenderer
    {
        // Cached world-space data
        private Vector3[] _worldPosBase;
        private Vector3[] _worldNormal;
        private Matrix4x4 _lastLocalToWorld;
        private bool _cacheValid = false;

        // Material used for the mask overlay (created once, reused across repaints)
        private static Material _maskOverlayMat;

        /// <summary>
        /// Invalidates the cached overlay geometry.
        /// Call when mesh or transform changes.
        /// </summary>
        public void InvalidateCache()
        {
            _cacheValid = false;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Mask overlay (texture-based, replaces island wireframes)
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Projects the overlay texture onto the mesh surface in the scene view
        /// using GL immediate mode and the MaskOverlay shader.
        ///
        /// The overlay texture encodes both island selection and hand-painted
        /// regions:  selected / painted pixels carry the selection colour with
        /// an alpha value; unselected pixels are fully transparent, leaving the
        /// mesh surface unchanged.
        ///
        /// This replaces the previous wireframe DrawSelectedIslands approach so
        /// that filled shape and paint strokes are visible directly on the object.
        /// </summary>
        /// <param name="analysis">UV analysis containing all mesh triangles</param>
        /// <param name="transform">Target object transform</param>
        /// <param name="settings">Current mask settings (ZTest, depth offset)</param>
        /// <param name="overlayTexture">
        ///   Semi-transparent overlay texture from UVPreviewDrawer.OverlayTexture.
        ///   Must not be null.
        /// </param>
        /// <param name="bakedMesh">Optional baked mesh for SkinnedMeshRenderer</param>
        /// <param name="useBakedMesh">Whether to use baked mesh positions</param>
        public void DrawMaskOverlay(
            UVAnalysis analysis,
            Transform transform,
            MaskSettings settings,
            Texture2D overlayTexture,
            Mesh bakedMesh = null,
            bool useBakedMesh = false)
        {
            if (analysis == null || transform == null || overlayTexture == null) return;
            if (Event.current.type != EventType.Repaint) return;

            EnsureWorldCache(analysis, transform, bakedMesh, useBakedMesh);
            if (_worldPosBase == null || _worldNormal == null) return;

            var mat = GetOrCreateOverlayMaterial();
            if (mat == null) return;

            mat.mainTexture = overlayTexture;
            mat.SetInt("_ZTest", (int)(settings.OverlayOnTop
                ? UnityEngine.Rendering.CompareFunction.Always
                : UnityEngine.Rendering.CompareFunction.LessEqual));

            if (!mat.SetPass(0)) return;

            float depthOffset = settings.OverlayDepthOffset;
            var cam = Camera.current;

            GL.PushMatrix();
            if (cam != null)
            {
                // Explicitly set up the view/projection matrices from the scene camera
                // so world-space vertices passed to GL.Vertex() are correctly projected
                // to clip space regardless of any prior GL matrix state.
                GL.LoadProjectionMatrix(cam.projectionMatrix);
                GL.modelview = cam.worldToCameraMatrix;
            }
            else
            {
                // Fallback: ensure the model part is identity so that world-space
                // vertices are not double-transformed.
                GL.MultMatrix(Matrix4x4.identity);
            }
            GL.Begin(GL.TRIANGLES);

            int posLen = _worldPosBase.Length;
            foreach (var tri in analysis.Triangles)
            {
                // Guard against vertex-count mismatch that can occur when the baked
                // mesh (SkinnedMeshRenderer) is temporarily out of sync with the analysis.
                // The check is intentionally lightweight (unsigned comparison) to keep the
                // hot loop fast on typical meshes.
                if ((uint)tri.v0 >= (uint)posLen ||
                    (uint)tri.v1 >= (uint)posLen ||
                    (uint)tri.v2 >= (uint)posLen) continue;

                var a = _worldPosBase[tri.v0] + _worldNormal[tri.v0] * depthOffset;
                var b = _worldPosBase[tri.v1] + _worldNormal[tri.v1] * depthOffset;
                var c = _worldPosBase[tri.v2] + _worldNormal[tri.v2] * depthOffset;

                GL.TexCoord2(tri.uv0.x, tri.uv0.y); GL.Vertex(a);
                GL.TexCoord2(tri.uv1.x, tri.uv1.y); GL.Vertex(b);
                GL.TexCoord2(tri.uv2.x, tri.uv2.y); GL.Vertex(c);
            }

            GL.End();
            GL.PopMatrix();
        }

        /// <summary>
        /// Returns (and lazily creates) the shared material for the mask overlay.
        /// Tries to load the custom Hidden/UVMaskMaker/MaskOverlay shader first;
        /// falls back to Unlit/Transparent so the tool degrades gracefully if the
        /// shader file has not been compiled yet (e.g., fresh project import).
        /// </summary>
        private static Material GetOrCreateOverlayMaterial()
        {
            if (_maskOverlayMat != null) return _maskOverlayMat;

            var shader = Shader.Find("Hidden/UVMaskMaker/MaskOverlay");
            if (shader == null)
                shader = Shader.Find("Unlit/Transparent");
            if (shader == null)
            {
                Debug.LogWarning("[OverlayRenderer] Could not find the 'Hidden/UVMaskMaker/MaskOverlay' shader " +
                                 "or its built-in fallback 'Unlit/Transparent'. " +
                                 "Please ensure MaskOverlay.shader is present in the project and has compiled " +
                                 "without errors (check the Console for shader compilation messages). " +
                                 "The mask overlay will not be rendered in the scene view until the shader is available.");
                return null;
            }

            _maskOverlayMat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            return _maskOverlayMat;
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
            // Use SeamEdges3D: deduplicated 3D vertex pairs — one entry per unique 3D edge
            // regardless of how many UV sides it has. This avoids drawing the same 3D line
            // multiple times when seam edges have two UV sides stored in BorderEdges.
            if (analysis.SeamEdges3D == null || analysis.SeamEdges3D.Count == 0) return;

            EnsureWorldCache(analysis, transform, bakedMesh, useBakedMesh);
            if (_worldPosBase == null || _worldNormal == null) return;

            Handles.zTest = settings.OverlayOnTop
                ? UnityEngine.Rendering.CompareFunction.Always
                : UnityEngine.Rendering.CompareFunction.LessEqual;
            Handles.color = settings.SeamColor;

            if (settings.DisableAA)
            {
                var seamVerts = ListPool<Vector3>.Get();
                foreach (var (v0, v1) in analysis.SeamEdges3D)
                {
                    if ((uint)v0 >= _worldPosBase.Length || (uint)v1 >= _worldPosBase.Length) continue;
                    var a = _worldPosBase[v0] + _worldNormal[v0] * settings.OverlayDepthOffset;
                    var b = _worldPosBase[v1] + _worldNormal[v1] * settings.OverlayDepthOffset;
                    seamVerts.Add(a);
                    seamVerts.Add(b);
                }
                if (seamVerts.Count >= 2) Handles.DrawLines(seamVerts.ToArray());
                ListPool<Vector3>.Release(seamVerts);
            }
            else
            {
                foreach (var (v0, v1) in analysis.SeamEdges3D)
                {
                    if ((uint)v0 >= _worldPosBase.Length || (uint)v1 >= _worldPosBase.Length) continue;
                    var a = _worldPosBase[v0] + _worldNormal[v0] * settings.OverlayDepthOffset;
                    var b = _worldPosBase[v1] + _worldNormal[v1] * settings.OverlayDepthOffset;
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
