// MAScaleAdjusterService.cs - Provides access to Modular Avatar Scale Adjuster data via reflection
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Dennoko.UVTools.Services
{
    /// <summary>
    /// Handles interaction with Modular Avatar Scale Adjuster components using reflection.
    /// This avoids direct dependency on the Modular Avatar package.
    /// </summary>
    public class MAScaleAdjusterService
    {
        private const string COMPONENT_NAME = "nadena.dev.modular_avatar.core.ModularAvatarScaleAdjuster";
        private const string PROPERTY_SCALE = "Scale";

        private Type _targetType;
        private PropertyInfo _scaleProp;
        private bool _reflectionInitialized;

        /// <summary>
        /// Retrieves scale overrides from Modular Avatar Scale Adjuster components in the hierarchy.
        /// </summary>
        /// <param name="root">Root GameObject to search from</param>
        /// <returns>Dictionary mapping Transform to its scale override</returns>
        public Dictionary<Transform, Vector3> GetScaleOverrides(GameObject targetObject)
        {
            var overrides = new Dictionary<Transform, Vector3>();
            if (targetObject == null) return overrides;

            InitializeReflection();

            if (_targetType == null || _scaleProp == null)
            {
                // Using Log instead of LogWarning to avoid spamming if MA is simply not installed
                return overrides; 
            }

            // Find the avatar root to search for adjusters
            Transform rootTransform = FindAvatarRoot(targetObject.transform);
            
            var adjusters = rootTransform.GetComponentsInChildren(_targetType, true);
            Debug.Log($"[UVMaskMaker] Searched from root '{rootTransform.name}': Found {adjusters.Length} MA Scale Adjusters.");

            foreach (var adjuster in adjusters)
            {
                var component = adjuster as Component;
                if (component == null) continue;

                try
                {
                    var scale = (Vector3)_scaleProp.GetValue(component);
                    if (!overrides.ContainsKey(component.transform))
                    {
                        overrides.Add(component.transform, scale);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[UVMaskMaker] Failed to get MA Scale Adjuster value: {e.Message}");
                }
            }

            return overrides;
        }

        private Transform FindAvatarRoot(Transform current)
        {
            // Try to find Animator as it's the most common avatar root identifier
            var animator = current.GetComponentInParent<Animator>();
            if (animator != null) return animator.transform;
            
            // Fallback to top-most parent
            return current.root;
        }

        private void InitializeReflection()
        {
            if (_reflectionInitialized) return;

            try
            {
                // Try finding by assembly-qualified name first (most reliable)
                _targetType = Type.GetType($"{COMPONENT_NAME}, nadena.dev.modular-avatar.core");

                if (_targetType == null)
                {
                    // Fallback: search all loaded assemblies
                    foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        var type = assembly.GetType(COMPONENT_NAME);
                        if (type != null)
                        {
                            _targetType = type;
                            Debug.Log($"[UVMaskMaker] Found Modular Avatar type in assembly: {assembly.GetName().Name}");
                            break;
                        }
                    }
                }

                if (_targetType != null)
                {
                    _scaleProp = _targetType.GetProperty(PROPERTY_SCALE, BindingFlags.Public | BindingFlags.Instance);
                    if (_scaleProp == null)
                    {
                         Debug.LogError($"[UVMaskMaker] Property '{PROPERTY_SCALE}' not found on type '{_targetType.FullName}'. API may have changed.");
                    }
                }
                else
                {
                    // Only log warnings if explicitly debugging or if the user expects MA to be there. 
                    // Silent failure is better here if MA is not installed.
                    // But since user reported "Found 0", we want to know if Type was found.
                    Debug.Log($"[UVMaskMaker] Modular Avatar type '{COMPONENT_NAME}' not found. MA support disabled.");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[UVMaskMaker] Failed to initialize MA reflection: {e.Message}");
            }
            finally
            {
                _reflectionInitialized = true;
            }
        }
    }
}
