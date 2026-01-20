// UVPreviewDrawer.cs - Handles UV preview rendering and click selection in the editor window
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Dennoko.UVTools.Core;
using Dennoko.UVTools.Data;

namespace Dennoko.UVTools.UI
{
    /// <summary>
    /// Draws the UV mask preview texture and border overlays in the editor window.
    /// Supports click-based island selection.
    /// </summary>
    public class UVPreviewDrawer
    {
        private Texture2D _previewTex;
        private Texture2D _overlayTex;
        private bool _dirty = true;
        private int _lastSize = 0;

        // Label map for click detection
        private int[] _labelMap;
        private int _labelMapSize = 0;
        private Rect _lastImgRect;

        private static readonly Color UVFrame = new Color(0.25f, 0.25f, 0.25f, 1);

        /// <summary>
        /// Event fired when an island is clicked in the preview.
        /// Parameter is the island index, or -1 if clicked on empty area.
        /// </summary>
        public event Action<int> OnIslandClicked;

        /// <summary>
        /// Marks the preview as needing regeneration.
        /// </summary>
        public void MarkDirty()
        {
            _dirty = true;
        }

        /// <summary>
        /// Draws the UV preview in the given area.
        /// </summary>
        /// <param name="analysis">UV analysis result</param>
        /// <param name="selectedIslands">Set of selected island indices</param>
        /// <param name="settings">Current mask settings</param>
        /// <param name="baseTexture">Optional base texture to overlay</param>
        /// <param name="localization">Localization service for text strings</param>
        public void Draw(
            UVAnalysis analysis,
            HashSet<int> selectedIslands,
            MaskSettings settings,
            Texture baseTexture,
            Services.LocalizationService localization)
        {
            var rect = GUILayoutUtility.GetAspectRect(1f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            EditorGUI.DrawRect(rect, UVFrame * 0.5f);

            if (analysis == null)
            {
                var hintText = localization?.Get("preview_hint") ?? "Run analysis to preview UVs";
                GUI.Label(rect, hintText, new GUIStyle(EditorStyles.centeredGreyMiniLabel) { alignment = TextAnchor.MiddleCenter });
                return;
            }

            int size = settings.TextureSize;
            EnsureTextures(size);
            EnsureLabelMap(analysis, size);

            if (_dirty)
            {
                RegenerateTextures(analysis, selectedIslands, settings);
                _dirty = false;
            }

            // Calculate image rect
            const float pad = 6f;
            var texRect = new Rect(rect.x + pad, rect.y + pad, rect.width - pad * 2, rect.height - pad * 2);
            float side = Mathf.Min(texRect.width, texRect.height);
            _lastImgRect = new Rect(
                texRect.x + (texRect.width - side) * 0.5f,
                texRect.y + (texRect.height - side) * 0.5f,
                side, side
            );

            // Handle click events
            HandleClickEvent(analysis);

            if (Event.current.type == EventType.Repaint)
            {
                DrawPreviewContent(rect, analysis, settings, baseTexture);
            }
        }

        /// <summary>
        /// Disposes of texture resources.
        /// </summary>
        public void Dispose()
        {
            if (_previewTex != null)
            {
                UnityEngine.Object.DestroyImmediate(_previewTex);
                _previewTex = null;
            }
            if (_overlayTex != null)
            {
                UnityEngine.Object.DestroyImmediate(_overlayTex);
                _overlayTex = null;
            }
            _labelMap = null;
        }

        private void HandleClickEvent(UVAnalysis analysis)
        {
            var e = Event.current;
            if (e.type != EventType.MouseDown || e.button != 0) return;
            if (!_lastImgRect.Contains(e.mousePosition)) return;
            if (_labelMap == null || _labelMapSize == 0) return;

            // Convert mouse position to UV coordinates
            float u = (e.mousePosition.x - _lastImgRect.x) / _lastImgRect.width;
            float v = 1f - (e.mousePosition.y - _lastImgRect.y) / _lastImgRect.height;

            // Clamp to [0,1]
            u = Mathf.Clamp01(u);
            v = Mathf.Clamp01(v);

            // Convert to pixel coordinates
            int px = Mathf.Clamp(Mathf.FloorToInt(u * _labelMapSize), 0, _labelMapSize - 1);
            int py = Mathf.Clamp(Mathf.FloorToInt(v * _labelMapSize), 0, _labelMapSize - 1);

            // Get island index from label map
            int islandIdx = _labelMap[py * _labelMapSize + px];

            // Fire event
            OnIslandClicked?.Invoke(islandIdx);

            e.Use();
        }

        private void EnsureTextures(int size)
        {
            if (_previewTex == null || _previewTex.width != size)
            {
                if (_previewTex != null) UnityEngine.Object.DestroyImmediate(_previewTex);
                _previewTex = new Texture2D(size, size, TextureFormat.RGBA32, false, true)
                {
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    name = "UVMaskPreview"
                };
                _dirty = true;
            }

            if (_overlayTex == null || _overlayTex.width != size)
            {
                if (_overlayTex != null) UnityEngine.Object.DestroyImmediate(_overlayTex);
                _overlayTex = new Texture2D(size, size, TextureFormat.RGBA32, false, true)
                {
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    name = "UVMaskOverlay"
                };
                _dirty = true;
            }

            _lastSize = size;
        }

        private void EnsureLabelMap(UVAnalysis analysis, int size)
        {
            // Rebuild label map if size changed or not initialized
            if (_labelMap == null || _labelMapSize != size)
            {
                _labelMap = Dennoko.UVTools.UVMaskExport.BuildLabelMapTransient(analysis, size, size);
                _labelMapSize = size;
            }
        }

        /// <summary>
        /// Invalidates the label map cache. Call when analysis changes.
        /// </summary>
        public void InvalidateLabelMap()
        {
            _labelMap = null;
            _labelMapSize = 0;
        }

        private void RegenerateTextures(UVAnalysis analysis, HashSet<int> selectedIslands, MaskSettings settings)
        {
            int size = settings.TextureSize;
            var mask = MaskBuilder.BuildProcessedMask(analysis, selectedIslands, size, size, settings.PixelMargin, settings.InvertMask);

            // Main preview texture
            var pixels = MaskBuilder.MaskToColors(mask, (Color32)settings.PreviewFillSelectedColor, new Color32(255, 255, 255, 255));
            _previewTex.SetPixels32(pixels);
            _previewTex.Apply(false, false);

            // Overlay texture (semi-transparent)
            var overlay = MaskBuilder.MaskToOverlay(mask, settings.PreviewFillSelectedColor, settings.PreviewOverlayAlpha);
            _overlayTex.SetPixels32(overlay);
            _overlayTex.Apply(false, false);
        }

        private void DrawPreviewContent(Rect rect, UVAnalysis analysis, MaskSettings settings, Texture baseTexture)
        {
            if (settings.PreviewOverlayBaseTex && baseTexture != null)
            {
                // Draw base texture first
                GUI.DrawTexture(_lastImgRect, baseTexture, ScaleMode.ScaleToFit, true);
                // Then draw semi-transparent overlay
                if (_overlayTex != null)
                {
                    GUI.DrawTexture(_lastImgRect, _overlayTex, ScaleMode.ScaleToFit, true);
                }
            }
            else
            {
                // Default: mask alone
                GUI.DrawTexture(_lastImgRect, _previewTex, ScaleMode.ScaleToFit, true);
            }

            // Draw UV island boundaries
            if (settings.ShowIslandPreview && analysis.BorderEdges != null && analysis.BorderEdges.Count > 0)
            {
                GUI.BeginGroup(_lastImgRect);
                var localRect = new Rect(0, 0, _lastImgRect.width, _lastImgRect.height);
                Handles.BeginGUI();
                Handles.color = new Color(1f, 0.5f, 0f, 1f);
                foreach (var be in analysis.BorderEdges)
                {
                    float ax = Mathf.Lerp(localRect.x, localRect.xMax, Mathf.Clamp01(be.uv0.x));
                    float ay = Mathf.Lerp(localRect.yMax, localRect.y, Mathf.Clamp01(be.uv0.y));
                    float bx = Mathf.Lerp(localRect.x, localRect.xMax, Mathf.Clamp01(be.uv1.x));
                    float by = Mathf.Lerp(localRect.yMax, localRect.y, Mathf.Clamp01(be.uv1.y));
                    Handles.DrawLine(new Vector3(ax, ay, 0), new Vector3(bx, by, 0));
                }
                Handles.EndGUI();
                GUI.EndGroup();
            }

            // Draw frame
            Handles.color = UVFrame;
            Handles.DrawLine(new Vector3(_lastImgRect.x, _lastImgRect.y), new Vector3(_lastImgRect.xMax, _lastImgRect.y));
            Handles.DrawLine(new Vector3(_lastImgRect.xMax, _lastImgRect.y), new Vector3(_lastImgRect.xMax, _lastImgRect.yMax));
            Handles.DrawLine(new Vector3(_lastImgRect.xMax, _lastImgRect.yMax), new Vector3(_lastImgRect.x, _lastImgRect.yMax));
            Handles.DrawLine(new Vector3(_lastImgRect.x, _lastImgRect.yMax), new Vector3(_lastImgRect.x, _lastImgRect.y));
        }
    }
}
