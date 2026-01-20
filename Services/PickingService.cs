// PickingService.cs - Handles 3D picking for UV island selection
using System;
using UnityEditor;
using UnityEngine;

namespace Dennoko.UVTools.Services
{
    /// <summary>
    /// Provides 3D picking functionality for selecting UV islands in the scene view.
    /// Manages temporary collider lifecycle.
    /// </summary>
    public class PickingService : IDisposable
    {
        private MeshCollider _tempCollider;
        private GameObject _tempColliderGO;
        private Transform _targetTransform;
        private Mesh _targetMesh;
        private Mesh _bakedMesh;
        private bool _useBakedMesh;
        private bool _disposed;

        /// <summary>
        /// Initializes the picking service for the specified target.
        /// </summary>
        /// <param name="targetTransform">Transform of the target object</param>
        /// <param name="targetMesh">Target mesh for collision</param>
        /// <param name="bakedMesh">Optional baked mesh for SkinnedMeshRenderer</param>
        /// <param name="useBakedMesh">Whether to use baked mesh for picking</param>
        public void Initialize(Transform targetTransform, Mesh targetMesh, Mesh bakedMesh = null, bool useBakedMesh = false)
        {
            Cleanup();

            _targetTransform = targetTransform;
            _targetMesh = targetMesh;
            _bakedMesh = bakedMesh;
            _useBakedMesh = useBakedMesh;

            if (_targetTransform == null || _targetMesh == null) return;

            CreateTempCollider();
        }

        /// <summary>
        /// Updates the mesh used for collision.
        /// </summary>
        public void UpdateMesh(Mesh bakedMesh, bool useBakedMesh)
        {
            _bakedMesh = bakedMesh;
            _useBakedMesh = useBakedMesh;

            if (_tempCollider != null)
            {
                _tempCollider.sharedMesh = (_useBakedMesh && _bakedMesh != null) ? _bakedMesh : _targetMesh;
            }
        }

        /// <summary>
        /// Attempts to pick a UV island at the given GUI position.
        /// </summary>
        /// <param name="guiPos">Mouse position in GUI coordinates</param>
        /// <param name="analysis">UV analysis result</param>
        /// <returns>Island index if hit, null otherwise</returns>
        public int? TryPick(Vector2 guiPos, UVAnalysis analysis)
        {
            if (_tempCollider == null || analysis == null) return null;

            Ray ray = HandleUtility.GUIPointToWorldRay(guiPos);
            if (Physics.Raycast(ray, out var hit, Mathf.Infinity))
            {
                if (hit.collider != _tempCollider) return null;

                int triIndex = hit.triangleIndex;
                if (analysis.TriangleToIsland.TryGetValue(triIndex, out int islandIdx))
                {
                    return islandIdx;
                }
            }

            return null;
        }

        /// <summary>
        /// Checks if the service has an active collider.
        /// </summary>
        public bool IsReady => _tempCollider != null;

        /// <summary>
        /// Cleans up temporary collider resources.
        /// </summary>
        public void Cleanup()
        {
            if (_tempColliderGO != null)
            {
                try
                {
                    UnityEngine.Object.DestroyImmediate(_tempColliderGO);
                }
                catch { /* ignore */ }
                _tempColliderGO = null;
                _tempCollider = null;
            }
        }

        /// <summary>
        /// Creates the temporary mesh collider for picking.
        /// </summary>
        private void CreateTempCollider()
        {
            if (_targetTransform == null || _targetMesh == null) return;

            _tempColliderGO = new GameObject("__UVMaskPickerCollider__");
            _tempColliderGO.hideFlags = HideFlags.HideAndDontSave;
            _tempColliderGO.transform.SetPositionAndRotation(_targetTransform.position, _targetTransform.rotation);
            _tempColliderGO.transform.localScale = _targetTransform.lossyScale;

            _tempCollider = _tempColliderGO.AddComponent<MeshCollider>();
            _tempCollider.sharedMesh = (_useBakedMesh && _bakedMesh != null) ? _bakedMesh : _targetMesh;
            _tempCollider.convex = false;
        }

        /// <summary>
        /// Disposes of the picking service.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            Cleanup();
            _disposed = true;
        }
    }
}
