using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace Dennoko.UVTools.Services
{
    /// <summary>
    /// Manages the creation and cleanup of temporary work copies of meshes.
    /// This allows working on a clean MeshRenderer version of a SkinnedMeshRenderer target,
    /// avoiding issues with bone scaling (Modular Avatar) and other deformers.
    /// </summary>
    public class WorkCopyService
    {
        private const string COPY_SUFFIX = " [WorkCopy]";

        /// <summary>
        /// Creates a work copy of the target renderer.
        /// </summary>
        /// <param name="originalRenderer">The source renderer (usually SkinnedMeshRenderer).</param>
        /// <param name="offset">Position offset for the copy.</param>
        /// <returns>The created work copy GameObject.</returns>
        public GameObject CreateWorkCopy(Renderer originalRenderer, Vector3 offset)
        {
            if (originalRenderer == null) return null;

            Mesh meshToCopy = null;
            if (originalRenderer is SkinnedMeshRenderer smr)
            {
                meshToCopy = smr.sharedMesh;
            }
            else
            {
                var mf = originalRenderer.GetComponent<MeshFilter>();
                if (mf != null) 
                {
                   meshToCopy = mf.sharedMesh;
                }
            }

            if (meshToCopy == null)
            {
                Debug.LogError("[UVMaskMaker] Source renderer has no mesh.");
                return null;
            }

            // Create new GameObject
            GameObject copyGO = new GameObject(originalRenderer.name + COPY_SUFFIX);
            
            // Set position: Original position + offset
            // We use the root level position if possible, or just world position
            copyGO.transform.position = originalRenderer.transform.position + offset;
            copyGO.transform.rotation = Quaternion.identity; // Reset rotation for easier viewing
            copyGO.transform.localScale = Vector3.one; // Reset scale to 1

            // Add components
            var meshFilter = copyGO.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = meshToCopy;

            var meshRenderer = copyGO.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterials = originalRenderer.sharedMaterials;

            Undo.RegisterCreatedObjectUndo(copyGO, "Create Work Copy");
            Selection.activeGameObject = copyGO;

            return copyGO;
        }

        /// <summary>
        /// Checks if the given object is a work copy created by this tool.
        /// </summary>
        public bool IsWorkCopy(GameObject go)
        {
            return go != null && go.name.EndsWith(COPY_SUFFIX);
        }

        /// <summary>
        /// Removes the work copy object.
        /// </summary>
        public void CleanupWorkCopy(GameObject workCopy)
        {
            if (workCopy != null)
            {
                Undo.DestroyObjectImmediate(workCopy);
            }
        }
    }
}
