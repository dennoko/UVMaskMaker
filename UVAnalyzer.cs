// UVAnalyzer.cs - shared analysis types and analyzer
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Dennoko.UVTools
{
    /// <summary>
    /// Holds the results of a UV analysis for a mesh.
    /// </summary>
    public class UVAnalysis
    {
        public List<Vector3> Vertices = new List<Vector3>(); // mesh vertices in local space
        public List<Vector3> Normals = new List<Vector3>();
        public List<Vector2> UVs = new List<Vector2>();
    public int UVChannel = 0; // which UV channel was analyzed
        public List<UVTriangle> Triangles = new List<UVTriangle>();
        public List<UVIsland> Islands = new List<UVIsland>();
        public List<UVBorderEdge> BorderEdges = new List<UVBorderEdge>(); // seam edges in vertex space
        public Dictionary<int, int> TriangleToIsland = new Dictionary<int, int>(); // triIndex -> islandIndex
    }

    public struct UVTriangle
    {
        public int triIndex; // index in mesh.triangles/3
        public int v0, v1, v2; // vertex indices
        public Vector2 uv0, uv1, uv2; // uv coords
    }

    public class UVIsland
    {
        public List<UVTriangle> Triangles = new List<UVTriangle>();
    }

    public struct UVBorderEdge
    {
        public int v0, v1; // vertex indices, used for scene overlay drawing
        public Vector2 uv0, uv1; // uv endpoints, used for UV-space edge identity
    }

    public static class UVAnalyzer
    {
        private const float UVKeyScale = 100000f; // for rounding to 1e-5

        /// <summary>
        /// Analyze the mesh UVs (specified channel) to compute islands and border edges (seams).
        /// </summary>
        public static UVAnalysis Analyze(Mesh mesh, int uvChannel)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));
            if (!mesh.isReadable) throw new InvalidOperationException("Mesh is not readable. Please enable Read/Write on the mesh import settings.");

            var analysis = new UVAnalysis();
            analysis.UVChannel = Mathf.Clamp(uvChannel, 0, 7);
            analysis.Vertices.AddRange(mesh.vertices);
            var normals = mesh.normals;
            if (normals != null && normals.Length == mesh.vertexCount)
            {
                analysis.Normals.AddRange(normals);
            }
            else
            {
                analysis.Normals.AddRange(Enumerable.Repeat(Vector3.zero, mesh.vertexCount));
            }

            var uvs = new List<Vector2>();
            mesh.GetUVs(analysis.UVChannel, uvs);
            if (uvs == null || uvs.Count == 0) throw new InvalidOperationException($"Mesh has no UV{analysis.UVChannel}.");
            analysis.UVs.AddRange(uvs);

            int[] tris = mesh.triangles;
            int triCount = tris.Length / 3;
            analysis.Triangles.Capacity = triCount;

            for (int t = 0; t < triCount; t++)
            {
                int i0 = tris[t * 3 + 0];
                int i1 = tris[t * 3 + 1];
                int i2 = tris[t * 3 + 2];
                var tri = new UVTriangle
                {
                    triIndex = t,
                    v0 = i0, v1 = i1, v2 = i2,
                    uv0 = analysis.UVs[i0],
                    uv1 = analysis.UVs[i1],
                    uv2 = analysis.UVs[i2]
                };
                analysis.Triangles.Add(tri);
            }

            // 3D edge mapping to UV edges
            var edgeToTris3D = new Dictionary<(int, int), List<(int triIdx, (int a, int b) uvEdge)>>();
            (int, int) EdgeKey3D(int a, int b) => a < b ? (a, b) : (b, a);

            (int a, int b) GetTriUe(UVTriangle tr, int va, int vb)
            {
                if ((tr.v0 == va && tr.v1 == vb) || (tr.v1 == va && tr.v0 == vb)) return (tr.v0, tr.v1);
                if ((tr.v1 == va && tr.v2 == vb) || (tr.v2 == va && tr.v1 == vb)) return (tr.v1, tr.v2);
                if ((tr.v2 == va && tr.v0 == vb) || (tr.v0 == va && tr.v2 == vb)) return (tr.v2, tr.v0);
                return (va, vb);
            }

            for (int tIdx = 0; tIdx < triCount; tIdx++)
            {
                var tr = analysis.Triangles[tIdx];
                void Add3D(int a, int b)
                {
                    var key = EdgeKey3D(a, b);
                    var uvE = GetTriUe(tr, a, b);
                    if (!edgeToTris3D.TryGetValue(key, out var list)) { list = new List<(int, (int, int))>(2); edgeToTris3D[key] = list; }
                    list.Add((tIdx, uvE));
                }
                Add3D(tr.v0, tr.v1);
                Add3D(tr.v1, tr.v2);
                Add3D(tr.v2, tr.v0);
            }

            bool SameUVEdge((int a, int b) e0, (int a, int b) e1)
            {
                var u0a = analysis.UVs[e0.a]; var u0b = analysis.UVs[e0.b];
                var u1a = analysis.UVs[e1.a]; var u1b = analysis.UVs[e1.b];
                long k0a = HashUV(u0a), k0b = HashUV(u0b);
                long k1a = HashUV(u1a), k1b = HashUV(u1b);
                bool dir0 = (k0a == k1a && k0b == k1b);
                bool dir1 = (k0a == k1b && k0b == k1a);
                return dir0 || dir1;
            }

            var borderEdges3D = new HashSet<(int, int)>();
            foreach (var kv in edgeToTris3D)
            {
                var trisList = kv.Value;
                if (trisList.Count == 1)
                {
                    var uvE = trisList[0].uvEdge;
                    borderEdges3D.Add(kv.Key);
                    analysis.BorderEdges.Add(new UVBorderEdge { v0 = kv.Key.Item1, v1 = kv.Key.Item2, uv0 = analysis.UVs[uvE.a], uv1 = analysis.UVs[uvE.b] });
                }
                else if (trisList.Count >= 2)
                {
                    bool seam = false;
                    var baseUV = trisList[0].uvEdge;
                    for (int i = 1; i < trisList.Count; i++)
                    {
                        if (!SameUVEdge(baseUV, trisList[i].uvEdge)) { seam = true; break; }
                    }
                    if (seam)
                    {
                        var uvE = baseUV;
                        borderEdges3D.Add(kv.Key);
                        analysis.BorderEdges.Add(new UVBorderEdge { v0 = kv.Key.Item1, v1 = kv.Key.Item2, uv0 = analysis.UVs[uvE.a], uv1 = analysis.UVs[uvE.b] });
                    }
                }
            }

            // Build connectivity ignoring border edges
            var edgeToTris = new Dictionary<(long, long), List<int>>();
            (long, long) EdgeKeyUV(Vector2 u0, Vector2 u1)
            {
                long k0 = HashUV(u0);
                long k1 = HashUV(u1);
                return k0 < k1 ? (k0, k1) : (k1, k0);
            }
            var borderSet = new HashSet<(long, long)>();
            foreach (var be in analysis.BorderEdges)
            {
                borderSet.Add(EdgeKeyUV(be.uv0, be.uv1));
            }
            void MapEdgeTri(int triIdx, int a, int b)
            {
                var key = EdgeKeyUV(analysis.UVs[a], analysis.UVs[b]);
                if (borderSet.Contains(key)) return;
                if (!edgeToTris.TryGetValue(key, out var list)) { list = new List<int>(2); edgeToTris[key] = list; }
                if (!list.Contains(triIdx)) list.Add(triIdx);
            }

            for (int t = 0; t < triCount; t++)
            {
                var tr = analysis.Triangles[t];
                MapEdgeTri(t, tr.v0, tr.v1);
                MapEdgeTri(t, tr.v1, tr.v2);
                MapEdgeTri(t, tr.v2, tr.v0);
            }

            var visited = new bool[triCount];
            for (int t = 0; t < triCount; t++)
            {
                if (visited[t]) continue;
                var island = new UVIsland();
                var stack = new Stack<int>();
                stack.Push(t);
                visited[t] = true;
                while (stack.Count > 0)
                {
                    int cur = stack.Pop();
                    island.Triangles.Add(analysis.Triangles[cur]);
                    var ctr = analysis.Triangles[cur];
                    foreach (var key in new[]
                    {
                        EdgeKeyUV(analysis.UVs[ctr.v0], analysis.UVs[ctr.v1]),
                        EdgeKeyUV(analysis.UVs[ctr.v1], analysis.UVs[ctr.v2]),
                        EdgeKeyUV(analysis.UVs[ctr.v2], analysis.UVs[ctr.v0])
                    })
                    {
                        if (edgeToTris.TryGetValue(key, out var list))
                        {
                            foreach (var nbr in list)
                            {
                                if (!visited[nbr] && nbr != cur)
                                {
                                    visited[nbr] = true;
                                    stack.Push(nbr);
                                }
                            }
                        }
                    }
                }
                int islandIdx = analysis.Islands.Count;
                analysis.Islands.Add(island);
                foreach (var tri in island.Triangles) analysis.TriangleToIsland[tri.triIndex] = islandIdx;
            }

            return analysis;
        }

    /// <summary>
    /// Backward-compatible overload analyzing UV channel 0.
    /// </summary>
    public static UVAnalysis Analyze(Mesh mesh) => Analyze(mesh, 0);

        private static long HashUV(Vector2 uv)
        {
            long x = (long)Mathf.Round(uv.x * UVKeyScale);
            long y = (long)Mathf.Round(uv.y * UVKeyScale);
            return (x << 32) ^ (y & 0xffffffffL);
        }
    }
}
