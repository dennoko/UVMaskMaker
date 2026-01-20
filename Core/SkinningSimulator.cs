// SkinningSimulator.cs - Simulates Linear Blend Skinning on CPU with scale overrides
using System;
using System.Collections.Generic;
using Dennoko.UVTools.Services;
using UnityEngine;

namespace Dennoko.UVTools.Core
{
    /// <summary>
    /// Performs CPU-side skinning to bake a mesh snapshot, allowing for custom bone scale overrides.
    /// This is used to accurately represent meshes deformed by Modular Avatar Scale Adjuster.
    /// </summary>
    public class SkinningSimulator
    {
        private readonly Dictionary<Transform, Matrix4x4> _boneMatrixCache = new Dictionary<Transform, Matrix4x4>();

        /// <summary>
        /// Bakes the skinned mesh into the destination mesh, applying scale overrides.
        /// </summary>
        /// <param name="smr">Target SkinnedMeshRenderer</param>
        /// <param name="destination">Mesh to write results to</param>
        /// <param name="scaleOverrides">Map of bone transforms to their override scales</param>
        public void Bake(SkinnedMeshRenderer smr, Mesh destination, Dictionary<Transform, Vector3> scaleOverrides)
        {
            if (smr == null || smr.sharedMesh == null || destination == null) return;

            Mesh sourceMesh = smr.sharedMesh;
            Transform[] bones = smr.bones;
            Matrix4x4[] bindposes = sourceMesh.bindposes;
            BoneWeight[] weights = sourceMesh.boneWeights;
            Vector3[] vertices = sourceMesh.vertices;
            
            // 1. Calculate corrected bone matrices in World Space
            Matrix4x4[] skinningMatrices = CalculateSkinningMatrices(smr, bones, bindposes, scaleOverrides);
            
            // 2. Transform vertices and normals
            Vector3[] newVertices = new Vector3[vertices.Length];
            // Note: For full correctness we should also transform normals, but for guides/picking positional accuracy is paramount.
            // Normals are less critical for guides (used for offset mostly). Let's transform them if possible.
            Vector3[] normals = sourceMesh.normals;
            Vector3[] newNormals = new Vector3[normals.Length];

            // Optimization: If no overrides and we trust BakeMesh, we wouldn't be here.
            // But since we are here, we must calculate per vertex.
            
            // World to Local matrix of the SMR transform (to bring baked mesh back to SMR local space, matching BakeMesh behavior)
            // Wait, BakeMesh returns mesh in SMR local space.
            // So our target coordinate system is SMR's Local Space.
            // But our skinning matrices are World Space.
            // So we need to multiply by smr.transform.worldToLocalMatrix at the end.
            
            Matrix4x4 worldToSmrLocal = smr.transform.worldToLocalMatrix;

            // Combine skinning matrix with worldToLocal to go straight to SMR Local Space
            for (int i = 0; i < skinningMatrices.Length; i++)
            {
                skinningMatrices[i] = worldToSmrLocal * skinningMatrices[i];
            }

            for (int i = 0; i < vertices.Length; i++)
            {
                BoneWeight bw = weights[i];
                // Handle up to 4 bones
                Vector3 v = vertices[i];
                Vector3 n = normals[i];
                
                Vector3 finalPos = Vector3.zero;
                Vector3 finalNorm = Vector3.zero;

                if (bw.weight0 > 0)
                {
                    finalPos += skinningMatrices[bw.boneIndex0].MultiplyPoint3x4(v) * bw.weight0;
                    finalNorm += skinningMatrices[bw.boneIndex0].MultiplyVector(n) * bw.weight0;
                }
                if (bw.weight1 > 0)
                {
                    finalPos += skinningMatrices[bw.boneIndex1].MultiplyPoint3x4(v) * bw.weight1;
                    finalNorm += skinningMatrices[bw.boneIndex1].MultiplyVector(n) * bw.weight1;
                }
                if (bw.weight2 > 0)
                {
                    finalPos += skinningMatrices[bw.boneIndex2].MultiplyPoint3x4(v) * bw.weight2;
                    finalNorm += skinningMatrices[bw.boneIndex2].MultiplyVector(n) * bw.weight2;
                }
                if (bw.weight3 > 0)
                {
                    finalPos += skinningMatrices[bw.boneIndex3].MultiplyPoint3x4(v) * bw.weight3;
                    finalNorm += skinningMatrices[bw.boneIndex3].MultiplyVector(n) * bw.weight3;
                }

                newVertices[i] = finalPos;
                newNormals[i] = finalNorm.normalized;
            }

            // 3. Apply to destination mesh
            destination.Clear();
            destination.vertices = newVertices;
            destination.normals = newNormals;
            destination.tangents = sourceMesh.tangents;
            destination.uv = sourceMesh.uv;
            destination.uv2 = sourceMesh.uv2;
            destination.triangles = sourceMesh.triangles;
            destination.subMeshCount = sourceMesh.subMeshCount;
            for (int i = 0; i < sourceMesh.subMeshCount; i++)
            {
                destination.SetTriangles(sourceMesh.GetTriangles(i), i);
            }
            
            // Recalculate bounds
            destination.RecalculateBounds();
        }

        private Matrix4x4[] CalculateSkinningMatrices(
            SkinnedMeshRenderer smr, 
            Transform[] bones, 
            Matrix4x4[] bindposes, 
            Dictionary<Transform, Vector3> scaleOverrides)
        {
            int boneCount = bones.Length;
            Matrix4x4[] matrices = new Matrix4x4[boneCount];
            _boneMatrixCache.Clear();

            // Cache matrices for hierarchy optimization
            // We need world space matrices for each bone, but with overridden scales in local steps.
            
            for (int i = 0; i < boneCount; i++)
            {
                Transform bone = bones[i];
                if (bone == null) 
                {
                    matrices[i] = Matrix4x4.identity;
                    continue;
                }

                Matrix4x4 correctedWorldMatrix = GetCorrectedWorldMatrix(bone, scaleOverrides);
                matrices[i] = correctedWorldMatrix * bindposes[i];
            }

            return matrices;
        }

        private Matrix4x4 GetCorrectedWorldMatrix(Transform t, Dictionary<Transform, Vector3> scaleOverrides)
        {
            if (t == null) return Matrix4x4.identity;

            if (_boneMatrixCache.TryGetValue(t, out var mat)) return mat;

            // Compute local matrix with override
            Vector3 scale = t.localScale;
            if (scaleOverrides != null && scaleOverrides.TryGetValue(t, out var overrideScale))
            {
                scale = overrideScale;
            }

            Matrix4x4 localMatrix = Matrix4x4.TRS(t.localPosition, t.localRotation, scale);

            // Recursively get parent matrix
            Matrix4x4 parentMatrix = Matrix4x4.identity;
            if (t.parent != null)
            {
                parentMatrix = GetCorrectedWorldMatrix(t.parent, scaleOverrides);
            }

            Matrix4x4 worldMatrix = parentMatrix * localMatrix;
            _boneMatrixCache[t] = worldMatrix;
            
            return worldMatrix;
        }
    }
}
