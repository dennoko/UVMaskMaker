// UVPreviewDrawer.cs - Handles UV preview rendering, click selection, zoom and pan in the editor window
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
    /// Supports click-based island selection, mouse wheel zoom, and middle-click pan.
    /// Call Draw(Rect viewportRect, ...) — the viewport rect is provided by the caller.
    /// </summary>
    public class UVPreviewDrawer
    {
        // --- Textures ---
        private Texture2D _previewTex;
        private Texture2D _overlayTex;
        private bool _dirty = true;
        private int _lastSize = 0;

        // --- Label map for click detection ---
        private int[] _labelMap;
        private int _labelMapSize = 0;

        // --- Viewport / zoom / pan state ---
        private Rect _viewportRect;     // the full rect passed to Draw()
        private Vector2 _viewCenter;    // center of the base square (screen space, local coords)
        private float _baseSide;        // side length of the base 1:1 square at zoom=1
        private Rect _lastImgRect;      // actual drawn texture rect (after zoom/pan)

        private float _zoomLevel = 1f;
        private Vector2 _panOffset = Vector2.zero;
        private bool _isDragging = false;

        // --- Style constants ---
        private static readonly Color UVFrameColor = new Color(0.25f, 0.25f, 0.25f, 1f);
        private static readonly Color BackgroundColor = new Color(0.12f, 0.12f, 0.12f, 1f);

        // --- Events ---
        /// <summary>Fired when an island is clicked in the preview. -1 = empty area.</summary>
        public event Action<int> OnIslandClicked;

        /// <summary>Fired when zoom or pan changes (caller should Repaint).</summary>
        public event Action OnViewChanged;

        // --- Public API ---

        /// <summary>Current zoom level (1.0 = fit).</summary>
        public float ZoomLevel => _zoomLevel;

        /// <summary>Marks the preview texture as needing regeneration.</summary>
        public void MarkDirty() => _dirty = true;

        /// <summary>Resets zoom and pan to default (fit view).</summary>
        public void ResetView()
        {
            _zoomLevel = 1f;
            _panOffset = Vector2.zero;
            OnViewChanged?.Invoke();
        }

        /// <summary>
        /// Draws the UV preview inside the given viewport rect.
        /// Should be called from within a GUILayout.BeginArea / equivalent context.
        /// </summary>
        public void Draw(
            Rect viewportRect,
            UVAnalysis analysis,
            HashSet<int> selectedIslands,
            MaskSettings settings,
            Texture baseTexture,
            Services.LocalizationService localization)
        {
            _viewportRect = viewportRect;

            // Background fill
            EditorGUI.DrawRect(viewportRect, BackgroundColor);

            // Compute base square (centered, with padding, at zoom=1)
            const float pad = 6f;
            var inner = new Rect(
                viewportRect.x + pad,
                viewportRect.y + pad,
                viewportRect.width  - pad * 2f,
                viewportRect.height - pad * 2f);

            _baseSide   = Mathf.Min(inner.width, inner.height);
            _viewCenter = new Vector2(
                inner.x + inner.width  * 0.5f,
                inner.y + inner.height * 0.5f);

            // Compute actual drawn rect (zoom + pan applied)
            float zoomedSize = _baseSide * _zoomLevel;
            _lastImgRect = new Rect(
                _viewCenter.x - zoomedSize * 0.5f + _panOffset.x,
                _viewCenter.y - zoomedSize * 0.5f + _panOffset.y,
                zoomedSize, zoomedSize);

            // Handle interaction events (zoom, pan, click) regardless of analysis state
            HandleScrollZoom(viewportRect);
            HandleMiddleDrag(viewportRect);

            if (analysis == null)
            {
                string hint = localization?.Get("preview_hint") ?? "Run analysis to preview UVs";
                var hintStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel) { alignment = TextAnchor.MiddleCenter };
                GUI.Label(viewportRect, hint, hintStyle);
                DrawFrame();
                return;
            }

            // Ensure textures and label map
            EnsureTextures(settings.TextureSize);
            EnsureLabelMap(analysis, settings.TextureSize);

            if (_dirty)
            {
                RegenerateTextures(analysis, selectedIslands, settings);
                _dirty = false;
            }

            // Click handling (left button, island selection)
            HandleClickEvent(analysis, viewportRect);

            if (Event.current.type == EventType.Repaint)
            {
                DrawPreviewContent(analysis, settings, baseTexture);
            }
        }

        /// <summary>Disposes texture resources.</summary>
        public void Dispose()
        {
            DestroyTex(ref _previewTex);
            DestroyTex(ref _overlayTex);
            _labelMap = null;
        }

        /// <summary>Invalidates the label map. Call when analysis or resolution changes.</summary>
        public void InvalidateLabelMap()
        {
            _labelMap = null;
            _labelMapSize = 0;
        }

        // ────────────────────────────────────────────────────────────────────────
        // Event handlers
        // ────────────────────────────────────────────────────────────────────────

        private void HandleScrollZoom(Rect viewportRect)
        {
            var e = Event.current;
            if (e.type != EventType.ScrollWheel) return;
            if (!viewportRect.Contains(e.mousePosition)) return;

            // e.delta.y > 0 → scroll down → zoom out
            float zoomDelta = -e.delta.y * 0.08f;
            float newZoom = Mathf.Clamp(_zoomLevel * Mathf.Exp(zoomDelta), 0.1f, 20f);

            // Zoom centred on mouse position
            Vector2 mouseOffset = e.mousePosition - _viewCenter;
            _panOffset = mouseOffset + (_panOffset - mouseOffset) * (newZoom / _zoomLevel);
            _zoomLevel = newZoom;

            e.Use();
            OnViewChanged?.Invoke();
        }

        private void HandleMiddleDrag(Rect viewportRect)
        {
            var e = Event.current;

            if (e.type == EventType.MouseDown && e.button == 2 && viewportRect.Contains(e.mousePosition))
            {
                _isDragging = true;
                e.Use();
                return;
            }

            if (e.type == EventType.MouseUp && e.button == 2)
            {
                if (_isDragging) e.Use();
                _isDragging = false;
                return;
            }

            if (e.type == EventType.MouseDrag && e.button == 2 && _isDragging)
            {
                _panOffset += e.delta;
                e.Use();
                OnViewChanged?.Invoke();
            }
        }

        private void HandleClickEvent(UVAnalysis analysis, Rect viewportRect)
        {
            var e = Event.current;
            if (e.type != EventType.MouseDown || e.button != 0) return;
            if (_labelMap == null || _labelMapSize == 0) return;

            // Must be inside the viewport
            if (!viewportRect.Contains(e.mousePosition)) return;
            // Must be inside the drawn texture rect
            if (!_lastImgRect.Contains(e.mousePosition)) return;

            // Convert to UV coordinates (accounting for zoom/pan via _lastImgRect)
            float u = (e.mousePosition.x - _lastImgRect.x) / _lastImgRect.width;
            float v = 1f - (e.mousePosition.y - _lastImgRect.y) / _lastImgRect.height;

            u = Mathf.Clamp01(u);
            v = Mathf.Clamp01(v);

            int px = Mathf.Clamp(Mathf.FloorToInt(u * _labelMapSize), 0, _labelMapSize - 1);
            int py = Mathf.Clamp(Mathf.FloorToInt(v * _labelMapSize), 0, _labelMapSize - 1);
            int islandIdx = _labelMap[py * _labelMapSize + px];

            OnIslandClicked?.Invoke(islandIdx);
            e.Use();
        }

        // ────────────────────────────────────────────────────────────────────────
        // Texture management
        // ────────────────────────────────────────────────────────────────────────

        private void EnsureTextures(int size)
        {
            if (_previewTex == null || _previewTex.width != size)
            {
                DestroyTex(ref _previewTex);
                _previewTex = new Texture2D(size, size, TextureFormat.RGBA32, false, true)
                {
                    filterMode = FilterMode.Point,
                    wrapMode   = TextureWrapMode.Clamp,
                    name       = "UVMaskPreview"
                };
                _dirty = true;
            }

            if (_overlayTex == null || _overlayTex.width != size)
            {
                DestroyTex(ref _overlayTex);
                _overlayTex = new Texture2D(size, size, TextureFormat.RGBA32, false, true)
                {
                    filterMode = FilterMode.Point,
                    wrapMode   = TextureWrapMode.Clamp,
                    name       = "UVMaskOverlay"
                };
                _dirty = true;
            }

            _lastSize = size;
        }

        private void EnsureLabelMap(UVAnalysis analysis, int size)
        {
            if (_labelMap == null || _labelMapSize != size)
            {
                _labelMap = UVMaskExport.BuildLabelMapTransient(analysis, size, size);
                _labelMapSize = size;
            }
        }

        private void RegenerateTextures(UVAnalysis analysis, HashSet<int> selectedIslands, MaskSettings settings)
        {
            int size = settings.TextureSize;
            var mask = MaskBuilder.BuildProcessedMask(analysis, selectedIslands, size, size, settings.PixelMargin, settings.InvertMask);

            var pixels = MaskBuilder.MaskToColors(mask, (Color32)settings.PreviewFillSelectedColor, new Color32(255, 255, 255, 255));
            _previewTex.SetPixels32(pixels);
            _previewTex.Apply(false, false);

            var overlay = MaskBuilder.MaskToOverlay(mask, settings.PreviewFillSelectedColor, settings.PreviewOverlayAlpha);
            _overlayTex.SetPixels32(overlay);
            _overlayTex.Apply(false, false);
        }

        // ────────────────────────────────────────────────────────────────────────
        // Drawing
        // ────────────────────────────────────────────────────────────────────────

        private void DrawPreviewContent(UVAnalysis analysis, MaskSettings settings, Texture baseTexture)
        {
            // Clip drawing to viewport (avoid overdrawing toolbar etc.)
            GUI.BeginGroup(_viewportRect);
            var localLastImgRect = new Rect(
                _lastImgRect.x - _viewportRect.x,
                _lastImgRect.y - _viewportRect.y,
                _lastImgRect.width,
                _lastImgRect.height);
            var localViewport = new Rect(0, 0, _viewportRect.width, _viewportRect.height);

            if (settings.PreviewOverlayBaseTex && baseTexture != null)
            {
                GUI.DrawTexture(localLastImgRect, baseTexture, ScaleMode.StretchToFill, true);
                if (_overlayTex != null)
                    GUI.DrawTexture(localLastImgRect, _overlayTex, ScaleMode.StretchToFill, true);
            }
            else
            {
                GUI.DrawTexture(localLastImgRect, _previewTex, ScaleMode.StretchToFill, false);
            }

            // UV island boundary lines
            if (settings.ShowIslandPreview && analysis.BorderEdges != null && analysis.BorderEdges.Count > 0)
            {
                Handles.BeginGUI();
                Handles.color = new Color(1f, 0.5f, 0f, 1f);
                foreach (var be in analysis.BorderEdges)
                {
                    float ax = Mathf.Lerp(localLastImgRect.x, localLastImgRect.xMax, Mathf.Clamp01(be.uv0.x));
                    float ay = Mathf.Lerp(localLastImgRect.yMax, localLastImgRect.y,  Mathf.Clamp01(be.uv0.y));
                    float bx = Mathf.Lerp(localLastImgRect.x, localLastImgRect.xMax, Mathf.Clamp01(be.uv1.x));
                    float by = Mathf.Lerp(localLastImgRect.yMax, localLastImgRect.y,  Mathf.Clamp01(be.uv1.y));
                    Handles.DrawLine(new Vector3(ax, ay), new Vector3(bx, by));
                }
                Handles.EndGUI();
            }

            GUI.EndGroup();

            // Frame around base square (at zoom=1 boundary indicator)
            DrawFrame();
        }

        private void DrawFrame()
        {
            // Draw 1px border around the _viewportRect
            float x = _viewportRect.x, y = _viewportRect.y;
            float xMax = _viewportRect.xMax, yMax = _viewportRect.yMax;
            Handles.BeginGUI();
            Handles.color = UVFrameColor;
            Handles.DrawLine(new Vector3(x,    y),    new Vector3(xMax, y));
            Handles.DrawLine(new Vector3(xMax, y),    new Vector3(xMax, yMax));
            Handles.DrawLine(new Vector3(xMax, yMax), new Vector3(x,    yMax));
            Handles.DrawLine(new Vector3(x,    yMax), new Vector3(x,    y));
            Handles.EndGUI();
        }

        private static void DestroyTex(ref Texture2D tex)
        {
            if (tex != null) { UnityEngine.Object.DestroyImmediate(tex); tex = null; }
        }
    }
}
