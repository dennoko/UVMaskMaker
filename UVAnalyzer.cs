// UVAnalyzer.cs - shared analysis types and analyzer
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Dennoko.UVTools
{
    /// <summary>
    /// Unit that a single click selects.
    /// </summary>
    public enum SelectionGranularity
    {
        UVIsland = 0,      // triangles connected through edges sharing position and UV
        ConnectedMesh = 1, // triangles connected through shared vertex positions
        Polygon = 2        // each triangle on its own
    }

    /// <summary>
    /// Partition of the analyzed triangles into selectable groups for one granularity.
    /// Selections are sets of indices into <see cref="Groups"/>.
    /// </summary>
    public class SelectionGroups
    {
        public List<UVIsland> Groups = new List<UVIsland>();
        public Dictionary<int, int> TriangleToGroup = new Dictionary<int, int>(); // triIndex -> group index
        public int Count => Groups.Count;
    }

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
        /// <summary>UV-space border edges — one entry per unique UV side of each seam/boundary edge. Used for UV preview drawing.</summary>
        public List<UVBorderEdge> BorderEdges = new List<UVBorderEdge>();
        /// <summary>Deduplicated 3D vertex-index pairs for all seam/boundary edges. Used for scene view drawing.</summary>
        public List<(int v0, int v1)> SeamEdges3D = new List<(int, int)>();

        private readonly SelectionGroups[] _groups = { new SelectionGroups(), new SelectionGroups(), new SelectionGroups() };

        public SelectionGroups GetGroups(SelectionGranularity granularity) => _groups[(int)granularity];

        /// <summary>
        /// Converts a selection between granularities. A target group is selected only when
        /// every one of its triangles was selected in the source granularity, so moving to a finer
        /// granularity keeps the selection intact while partially selected coarse groups are dropped.
        /// </summary>
        public HashSet<int> ConvertSelection(HashSet<int> selection, SelectionGranularity from, SelectionGranularity to)
        {
            if (from == to) return new HashSet<int>(selection);
            var result = new HashSet<int>();
            if (selection == null || selection.Count == 0) return result;

            var src = GetGroups(from);
            var selectedTris = new HashSet<int>();
            foreach (int g in selection)
            {
                if (g < 0 || g >= src.Count) continue;
                foreach (var tri in src.Groups[g].Triangles) selectedTris.Add(tri.triIndex);
            }

            var dst = GetGroups(to);
            for (int g = 0; g < dst.Count; g++)
            {
                bool all = true;
                foreach (var tri in dst.Groups[g].Triangles)
                {
                    if (!selectedTris.Contains(tri.triIndex)) { all = false; break; }
                }
                if (all) result.Add(g);
            }
            return result;
        }
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
        private const float UVKeyScale = 100000f;  // for rounding to 1e-5
        private const float PosKeyScale = 100000f; // for rounding to 1e-5

        /// <summary>
        /// Analyze the mesh UVs (specified channel) to compute islands and border edges (seams).
        /// </summary>
        /// <remarks>
        /// Vertices are identified by their quantized position and UV rather than by vertex index.
        /// Unity duplicates vertices wherever any attribute differs (e.g. split normals on sharp edges),
        /// so index-based connectivity would wrongly cut islands along hard edges.
        /// A UV island is a set of triangles connected through edges whose endpoints share both
        /// position and UV. Islands never cross submesh boundaries.
        /// </remarks>
        public static UVAnalysis Analyze(Mesh mesh, int uvChannel, int targetSubmesh = -1)
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
            var subTriCounts = new int[mesh.subMeshCount];
            for (int s = 0; s < subTriCounts.Length; s++) subTriCounts[s] = (int)(mesh.GetIndexCount(s) / 3);

            AnalyzeTopology(analysis, tris, subTriCounts, targetSubmesh);

            Debug.Log($"[UVAnalyzer] Submesh {targetSubmesh} extracted {analysis.Triangles.Count} triangles out of {tris.Length / 3} total.");
            return analysis;
        }

        /// <summary>
        /// Backward-compatible overload analyzing UV channel 0.
        /// </summary>
        public static UVAnalysis Analyze(Mesh mesh) => Analyze(mesh, 0, -1);

        /// <summary>
        /// Mesh-independent part of <see cref="Analyze(Mesh, int, int)"/>. Expects Vertices and UVs
        /// to be filled already; builds triangles, border edges and groups.
        /// </summary>
        /// <param name="tris">Concatenated triangle indices of all submeshes (mesh.triangles)</param>
        /// <param name="subTriCounts">Triangle count per submesh, in order</param>
        internal static void AnalyzeTopology(UVAnalysis analysis, int[] tris, int[] subTriCounts, int targetSubmesh)
        {
            int triCount = tris.Length / 3;
            analysis.Triangles.Capacity = triCount;

            var triSubmesh = new List<int>(triCount); // parallel to analysis.Triangles
            int triOffset = 0;
            for (int s = 0; s < subTriCounts.Length; s++)
            {
                int subTriCount = subTriCounts[s];
                if (targetSubmesh < 0 || s == targetSubmesh)
                {
                    for (int i = 0; i < subTriCount; i++)
                    {
                        int t = triOffset + i;
                        int i0 = tris[t * 3 + 0];
                        int i1 = tris[t * 3 + 1];
                        int i2 = tris[t * 3 + 2];
                        analysis.Triangles.Add(new UVTriangle
                        {
                            triIndex = t,
                            v0 = i0, v1 = i1, v2 = i2,
                            uv0 = analysis.UVs[i0],
                            uv1 = analysis.UVs[i1],
                            uv2 = analysis.UVs[i2]
                        });
                        triSubmesh.Add(s);
                    }
                }
                triOffset += subTriCount;
            }

            var ids = new VertexIds(analysis.Vertices, analysis.UVs);
            BuildBorderEdges(analysis, triSubmesh, ids);

            var uvIslandOf = GroupUVIslands(analysis.Triangles, triSubmesh, ids, out int islandCount);
            BuildGroups(analysis.Triangles, uvIslandOf, islandCount, analysis.GetGroups(SelectionGranularity.UVIsland));

            var connectedOf = GroupConnectedMeshes(analysis.Triangles, triSubmesh, ids, out int connectedCount);
            BuildGroups(analysis.Triangles, connectedOf, connectedCount, analysis.GetGroups(SelectionGranularity.ConnectedMesh));

            var polygonOf = new int[analysis.Triangles.Count];
            for (int i = 0; i < polygonOf.Length; i++) polygonOf[i] = i;
            BuildGroups(analysis.Triangles, polygonOf, polygonOf.Length, analysis.GetGroups(SelectionGranularity.Polygon));
        }

        /// <summary>
        /// Canonical vertex ids: vertices with the same quantized position share a position id,
        /// and vertices with the same quantized position and UV share a position+UV id.
        /// </summary>
        private sealed class VertexIds
        {
            public readonly int[] Pos;   // vertex index -> position id
            public readonly int[] PosUV; // vertex index -> position+UV id

            public VertexIds(List<Vector3> vertices, List<Vector2> uvs)
            {
                int n = vertices.Count;
                Pos = new int[n];
                PosUV = new int[n];
                var posIds = new Dictionary<Vector3Int, int>(n);
                var posUvIds = new Dictionary<(Vector3Int, Vector2Int), int>(n);
                for (int i = 0; i < n; i++)
                {
                    var pk = PosKey(vertices[i]);
                    var uk = i < uvs.Count ? UVKey(uvs[i]) : default;
                    if (!posIds.TryGetValue(pk, out int pid)) { pid = posIds.Count; posIds[pk] = pid; }
                    if (!posUvIds.TryGetValue((pk, uk), out int puid)) { puid = posUvIds.Count; posUvIds[(pk, uk)] = puid; }
                    Pos[i] = pid;
                    PosUV[i] = puid;
                }
            }
        }

        /// <summary>
        /// Order-independent key for an edge between two canonical vertex ids.
        /// </summary>
        private static long EdgeKey(int a, int b)
        {
            return a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
        }

        /// <summary>
        /// Finds seam/boundary edges. Triangle edges are grouped by (3D position edge, submesh);
        /// the group is a border when it has a single triangle (open boundary) or its triangles
        /// disagree on the UV of the edge (UV seam). Sharp edges that only split normals are not borders.
        /// </summary>
        private static void BuildBorderEdges(UVAnalysis analysis, List<int> triSubmesh, VertexIds ids)
        {
            // (position edge, submesh) -> list of (position+UV edge key, vertex a, vertex b)
            var groups = new Dictionary<(long, int), List<(long uvEdge, int a, int b)>>();
            void Add(int sub, int a, int b)
            {
                var key = (EdgeKey(ids.Pos[a], ids.Pos[b]), sub);
                if (!groups.TryGetValue(key, out var list)) { list = new List<(long, int, int)>(2); groups[key] = list; }
                list.Add((EdgeKey(ids.PosUV[a], ids.PosUV[b]), a, b));
            }
            for (int i = 0; i < analysis.Triangles.Count; i++)
            {
                var tr = analysis.Triangles[i];
                int sub = triSubmesh[i];
                Add(sub, tr.v0, tr.v1);
                Add(sub, tr.v1, tr.v2);
                Add(sub, tr.v2, tr.v0);
            }

            var seams3D = new HashSet<long>();
            var sides = new List<(long uvEdge, int a, int b)>(4);
            foreach (var kv in groups)
            {
                var list = kv.Value;
                sides.Clear();
                foreach (var e in list)
                {
                    bool dup = false;
                    foreach (var s in sides) { if (s.uvEdge == e.uvEdge) { dup = true; break; } }
                    if (!dup) sides.Add(e);
                }
                bool border = list.Count == 1 || sides.Count > 1;
                if (!border) continue;

                // Emit one UVBorderEdge per unique UV side so both sides of a seam are drawn in the UV preview.
                foreach (var s in sides)
                {
                    analysis.BorderEdges.Add(new UVBorderEdge { v0 = s.a, v1 = s.b, uv0 = analysis.UVs[s.a], uv1 = analysis.UVs[s.b] });
                }
                // Scene view draws each 3D edge once, even when shared across submeshes.
                if (seams3D.Add(kv.Key.Item1))
                {
                    analysis.SeamEdges3D.Add((sides[0].a, sides[0].b));
                }
            }
        }

        /// <summary>
        /// Groups triangles into UV islands: triangles sharing an edge with identical endpoint
        /// positions and UVs (within the same submesh) belong to the same island.
        /// Returns the island id per entry of <paramref name="triangles"/>.
        /// </summary>
        private static int[] GroupUVIslands(List<UVTriangle> triangles, List<int> triSubmesh, VertexIds ids, out int groupCount)
        {
            var uf = new UnionFind(triangles.Count);
            var firstTriOfEdge = new Dictionary<(long, int), int>(triangles.Count * 2);
            void Link(int i, int sub, int a, int b)
            {
                var key = (EdgeKey(ids.PosUV[a], ids.PosUV[b]), sub);
                if (firstTriOfEdge.TryGetValue(key, out int other)) uf.Union(i, other);
                else firstTriOfEdge[key] = i;
            }
            for (int i = 0; i < triangles.Count; i++)
            {
                var tr = triangles[i];
                int sub = triSubmesh[i];
                Link(i, sub, tr.v0, tr.v1);
                Link(i, sub, tr.v1, tr.v2);
                Link(i, sub, tr.v2, tr.v0);
            }
            return uf.Compact(out groupCount);
        }

        /// <summary>
        /// Groups triangles into connected meshes: triangles sharing any vertex position
        /// (within the same submesh) belong to the same group.
        /// Returns the group id per entry of <paramref name="triangles"/>.
        /// </summary>
        private static int[] GroupConnectedMeshes(List<UVTriangle> triangles, List<int> triSubmesh, VertexIds ids, out int groupCount)
        {
            var uf = new UnionFind(triangles.Count);
            var firstTriOfVertex = new Dictionary<(int, int), int>(triangles.Count * 2);
            void Link(int i, int sub, int v)
            {
                var key = (ids.Pos[v], sub);
                if (firstTriOfVertex.TryGetValue(key, out int other)) uf.Union(i, other);
                else firstTriOfVertex[key] = i;
            }
            for (int i = 0; i < triangles.Count; i++)
            {
                var tr = triangles[i];
                int sub = triSubmesh[i];
                Link(i, sub, tr.v0);
                Link(i, sub, tr.v1);
                Link(i, sub, tr.v2);
            }
            return uf.Compact(out groupCount);
        }

        /// <summary>
        /// Materializes group lists and the triangle -> group lookup from per-triangle group ids.
        /// </summary>
        private static void BuildGroups(List<UVTriangle> triangles, int[] groupOf, int groupCount, SelectionGroups target)
        {
            target.Groups.Capacity = groupCount;
            for (int g = 0; g < groupCount; g++) target.Groups.Add(new UVIsland());
            for (int i = 0; i < triangles.Count; i++)
            {
                int g = groupOf[i];
                target.Groups[g].Triangles.Add(triangles[i]);
                target.TriangleToGroup[triangles[i].triIndex] = g;
            }
        }

        private static Vector3Int PosKey(Vector3 p)
        {
            return new Vector3Int(
                Mathf.RoundToInt(p.x * PosKeyScale),
                Mathf.RoundToInt(p.y * PosKeyScale),
                Mathf.RoundToInt(p.z * PosKeyScale));
        }

        private static Vector2Int UVKey(Vector2 uv)
        {
            return new Vector2Int(
                Mathf.RoundToInt(uv.x * UVKeyScale),
                Mathf.RoundToInt(uv.y * UVKeyScale));
        }

        private sealed class UnionFind
        {
            private readonly int[] _parent;
            private readonly int[] _rank;

            public UnionFind(int size)
            {
                _parent = new int[size];
                _rank = new int[size];
                for (int i = 0; i < size; i++) _parent[i] = i;
            }

            public int Find(int i)
            {
                while (_parent[i] != i)
                {
                    _parent[i] = _parent[_parent[i]];
                    i = _parent[i];
                }
                return i;
            }

            public void Union(int a, int b)
            {
                int ra = Find(a), rb = Find(b);
                if (ra == rb) return;
                if (_rank[ra] < _rank[rb]) (ra, rb) = (rb, ra);
                _parent[rb] = ra;
                if (_rank[ra] == _rank[rb]) _rank[ra]++;
            }

            /// <summary>
            /// Returns sequential group ids (0..count-1) per element, numbered in order of first appearance.
            /// </summary>
            public int[] Compact(out int count)
            {
                var idOfRoot = new Dictionary<int, int>();
                var result = new int[_parent.Length];
                for (int i = 0; i < _parent.Length; i++)
                {
                    int root = Find(i);
                    if (!idOfRoot.TryGetValue(root, out int id))
                    {
                        id = idOfRoot.Count;
                        idOfRoot[root] = id;
                    }
                    result[i] = id;
                }
                count = idOfRoot.Count;
                return result;
            }
        }
    }
}
