// UVPreviewDrawer.cs - Handles UV preview rendering, click selection, zoom and pan in the editor window
// Performance: Uses cached island mask + tile-grid incremental updates for painting.
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
    /// Supports click-based island selection, mouse wheel zoom, middle-click pan,
    /// and hand-painting with incremental tile-based texture updates.
    /// </summary>
    public class UVPreviewDrawer
    {
        // --- Textures ---
        private Texture2D _previewTex;
        private Texture2D _overlayTex;
        private bool _dirty = true;
        private int _lastSize = 0;

        // --- Cached data for incremental painting ---
        private byte[] _cachedIslandMask;        // Cached BuildUnionMask result (no invert/dilate)
        private Color32[] _cachedPixels;          // Reusable Color32 buffer for _previewTex
        private Color32[] _cachedOverlayPixels;   // Reusable Color32 buffer for _overlayTex
        private bool _paintDirty = false;         // Only paint changed (fast incremental path)

        // --- Label map for click detection ---
        private int[] _labelMap;
        private int _labelMapSize = 0;

        // --- Viewport / zoom / pan state ---
        private Rect _viewportRect;
        private Vector2 _viewCenter;
        private float _baseSide;
        private Rect _lastImgRect;

        private float _zoomLevel = 1f;
        private Vector2 _panOffset = Vector2.zero;
        private bool _isDragging = false;

        // --- Style constants ---
        private static readonly Color UVFrameColor = new Color(0.25f, 0.25f, 0.25f, 1f);
        private static readonly Color BackgroundColor = new Color(0.12f, 0.12f, 0.12f, 1f);

        // --- Events ---
        /// <summary>Fired when an island is clicked in the preview. -1 = empty area.</summary>
        public event Action<int> OnIslandClicked;
        public event Action OnPaintStrokeFinished;

        /// <summary>Fired when zoom or pan changes (caller should Repaint).</summary>
        public event Action OnViewChanged;

        // --- Paint State (Brush) ---
        private Vector2 _lastPaintUV;
        private bool _isPaintingValid = false;
        private int _paintControlId;

        // --- Paint State (Rectangle) ---
        private Vector2 _rectStartUV;
        private Vector2 _rectCurrentUV;
        private bool _isRectPainting = false;

        // --- Paint State (Lasso) ---
        private List<Vector2> _lassoPoints = new List<Vector2>();
        private bool _isLassoPainting = false;
        private Vector2 _lastLassoUV;
        private const float LassoMinUVStep = 0.003f;

        // --- Public API ---

        /// <summary>True while a paint stroke is in progress (mouse held down in paint mode).</summary>
        public bool IsPainting => _isPaintingValid || _isRectPainting || _isLassoPainting;

        /// <summary>Current zoom level (1.0 = fit).</summary>
        public float ZoomLevel => _zoomLevel;

        /// <summary>Marks the preview texture as needing full regeneration.</summary>
        public void MarkDirty()
        {
            _dirty = true;
            _cachedIslandMask = null; // Invalidate island cache
        }

        /// <summary>Resets zoom and pan to default (fit view).</summary>
        public void ResetView()
        {
            _zoomLevel = 1f;
            _panOffset = Vector2.zero;
            OnViewChanged?.Invoke();
        }

        /// <summary>
        /// Draws the UV preview inside the given viewport rect.
        /// </summary>
        public void Draw(
            Rect viewportRect,
            UVAnalysis analysis,
            HashSet<int> selectedIslands,
            MaskSettings settings,
            Texture baseTexture,
            MaskPainter painter,
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

            // Process input (may set _paintDirty or invoke island click)
            HandleInteraction(analysis, settings, painter, viewportRect);

            // Incremental paint update runs immediately on any event type (MouseDrag etc.)
            // so the texture data is ready when the next Repaint draws it.
            if (_paintDirty && !_dirty && painter != null)
            {
                RegeneratePaintIncremental(settings, painter);
                _paintDirty = false;
            }

            // Full regeneration + drawing only on Repaint
            if (Event.current.type == EventType.Repaint)
            {
                if (_dirty)
                {
                    RegenerateTexturesFull(analysis, selectedIslands, settings, painter);
                    _dirty = false;
                    _paintDirty = false;
                }

                DrawPreviewContent(analysis, settings, baseTexture);
            }
        }

        /// <summary>Disposes texture resources.</summary>
        public void Dispose()
        {
            // Release hotControl if still held
            if ((_isPaintingValid || _isRectPainting || _isLassoPainting) && GUIUtility.hotControl == _paintControlId)
                GUIUtility.hotControl = 0;
            _isPaintingValid = false;
            _isRectPainting = false;
            _isLassoPainting = false;
            _lassoPoints.Clear();

            DestroyTex(ref _previewTex);
            DestroyTex(ref _overlayTex);
            _labelMap = null;
            _cachedIslandMask = null;
            _cachedPixels = null;
            _cachedOverlayPixels = null;
            _tileBuffer = null;
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

            float zoomDelta = -e.delta.y * 0.08f;
            float newZoom = Mathf.Clamp(_zoomLevel * Mathf.Exp(zoomDelta), 0.1f, 20f);

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

        private void HandleInteraction(UVAnalysis analysis, MaskSettings settings, MaskPainter painter, Rect viewportRect)
        {
            var e = Event.current;

            // Allocate a stable control ID for the paint interaction (must be called every OnGUI)
            _paintControlId = GUIUtility.GetControlID(FocusType.Passive);

            // For non-mouse events, nothing to do
            if (e.type != EventType.MouseDown && e.type != EventType.MouseDrag
                && e.type != EventType.MouseUp)
                return;

            if (e.button != 0) return;

            bool inImage    = _lastImgRect.Contains(e.mousePosition);
            bool inViewport = viewportRect.Contains(e.mousePosition);

            switch (e.type)
            {
                case EventType.MouseDown when inViewport && inImage:
                    if (settings.IsPaintMode && painter != null)
                    {
                        // Claim hotControl so no other IMGUI control steals drag events
                        GUIUtility.hotControl = _paintControlId;

                        float u = (e.mousePosition.x - _lastImgRect.x) / _lastImgRect.width;
                        float v = 1f - (e.mousePosition.y - _lastImgRect.y) / _lastImgRect.height;
                        Vector2 startUV = new Vector2(Mathf.Clamp01(u), Mathf.Clamp01(v));

                        switch (settings.PaintSubMode)
                        {
                            case PaintSubMode.Brush:
                            case PaintSubMode.Eraser:
                                painter.SaveUndoState();
                                _lastPaintUV = startUV;
                                painter.PaintDot(_lastPaintUV, settings.BrushSize, settings.PaintSubMode == PaintSubMode.Eraser);
                                _isPaintingValid = true;
                                _paintDirty = true;
                                break;

                            case PaintSubMode.Rect:
                                painter.SaveUndoState();
                                _rectStartUV    = startUV;
                                _rectCurrentUV  = startUV;
                                _isRectPainting = true;
                                break;

                            case PaintSubMode.Lasso:
                                painter.SaveUndoState();
                                _lassoPoints.Clear();
                                _lassoPoints.Add(startUV);
                                _lastLassoUV     = startUV;
                                _isLassoPainting = true;
                                break;
                        }
                        e.Use();
                    }
                    else if (!settings.IsPaintMode && _labelMap != null && _labelMapSize > 0)
                    {
                        float u = (e.mousePosition.x - _lastImgRect.x) / _lastImgRect.width;
                        float v = 1f - (e.mousePosition.y - _lastImgRect.y) / _lastImgRect.height;

                        int px = Mathf.Clamp(Mathf.FloorToInt(Mathf.Clamp01(u) * _labelMapSize), 0, _labelMapSize - 1);
                        int py = Mathf.Clamp(Mathf.FloorToInt(Mathf.Clamp01(v) * _labelMapSize), 0, _labelMapSize - 1);
                        int islandIdx = _labelMap[py * _labelMapSize + px];

                        OnIslandClicked?.Invoke(islandIdx);
                        e.Use();
                    }
                    break;

                case EventType.MouseDrag when GUIUtility.hotControl == _paintControlId:
                    if (settings.IsPaintMode && painter != null)
                    {
                        float u = (e.mousePosition.x - _lastImgRect.x) / _lastImgRect.width;
                        float v = 1f - (e.mousePosition.y - _lastImgRect.y) / _lastImgRect.height;
                        Vector2 currentUV = new Vector2(Mathf.Clamp01(u), Mathf.Clamp01(v));

                        if (_isPaintingValid)
                        {
                            bool erase = settings.PaintSubMode == PaintSubMode.Eraser;
                            painter.PaintLine(_lastPaintUV, currentUV, settings.BrushSize, erase);
                            _lastPaintUV = currentUV;
                            _paintDirty = true;
                        }
                        else if (_isRectPainting)
                        {
                            _rectCurrentUV = currentUV;
                            // No texture update needed during drag — EditorUpdate drives repaints via IsPainting
                        }
                        else if (_isLassoPainting)
                        {
                            if (Vector2.Distance(currentUV, _lastLassoUV) >= LassoMinUVStep)
                            {
                                _lassoPoints.Add(currentUV);
                                _lastLassoUV = currentUV;
                            }
                        }
                        e.Use();
                    }
                    break;

                case EventType.MouseUp when GUIUtility.hotControl == _paintControlId:
                    GUIUtility.hotControl = 0;

                    if (_isPaintingValid)
                    {
                        _isPaintingValid = false;
                        _dirty = true;
                        OnPaintStrokeFinished?.Invoke();
                    }
                    else if (_isRectPainting)
                    {
                        float u = (e.mousePosition.x - _lastImgRect.x) / _lastImgRect.width;
                        float v = 1f - (e.mousePosition.y - _lastImgRect.y) / _lastImgRect.height;
                        _rectCurrentUV = new Vector2(Mathf.Clamp01(u), Mathf.Clamp01(v));

                        painter?.PaintRect(_rectStartUV, _rectCurrentUV, false);
                        _isRectPainting = false;
                        _dirty = true;
                        OnPaintStrokeFinished?.Invoke();
                    }
                    else if (_isLassoPainting)
                    {
                        float u = (e.mousePosition.x - _lastImgRect.x) / _lastImgRect.width;
                        float v = 1f - (e.mousePosition.y - _lastImgRect.y) / _lastImgRect.height;
                        _lassoPoints.Add(new Vector2(Mathf.Clamp01(u), Mathf.Clamp01(v)));

                        painter?.PaintPolygon(_lassoPoints, false);
                        _lassoPoints.Clear();
                        _isLassoPainting = false;
                        _dirty = true;
                        OnPaintStrokeFinished?.Invoke();
                    }

                    e.Use();
                    break;
            }
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

        /// <summary>
        /// Full regeneration: rebuilds island mask from scratch, applies paint, invert, dilate.
        /// Called when selection/settings change or on stroke finish.
        /// </summary>
        private void RegenerateTexturesFull(UVAnalysis analysis, HashSet<int> selectedIslands, MaskSettings settings, MaskPainter painter)
        {
            int size = settings.TextureSize;
            if (painter != null) painter.EnsureSize(size);

            // Build and cache island mask
            _cachedIslandMask = MaskBuilder.BuildUnionMask(analysis, selectedIslands, size, size);

            // Build full processed mask (merge paint + invert + dilate)
            var mask = MaskBuilder.BuildProcessedMask(analysis, selectedIslands, size, size,
                settings.PixelMargin, settings.InvertMask, painter?.Mask);

            // Ensure pixel buffers
            int pixelCount = size * size;
            if (_cachedPixels == null || _cachedPixels.Length != pixelCount)
                _cachedPixels = new Color32[pixelCount];
            if (_cachedOverlayPixels == null || _cachedOverlayPixels.Length != pixelCount)
                _cachedOverlayPixels = new Color32[pixelCount];

            // Convert to colors (reuse arrays)
            var selColor = (Color32)settings.PreviewFillSelectedColor;
            var unselColor = new Color32(255, 255, 255, 255);
            byte overlayAlpha = (byte)Mathf.Clamp(Mathf.RoundToInt(settings.PreviewOverlayAlpha * 255f), 0, 255);
            var overlayCol = selColor;
            overlayCol.a = overlayAlpha;
            var transparent = new Color32(0, 0, 0, 0);

            for (int i = 0; i < pixelCount; i++)
            {
                bool selected = mask[i] != 0;
                _cachedPixels[i] = selected ? selColor : unselColor;
                _cachedOverlayPixels[i] = selected ? overlayCol : transparent;
            }

            _previewTex.SetPixels32(_cachedPixels);
            _previewTex.Apply(false, false);

            _overlayTex.SetPixels32(_cachedOverlayPixels);
            _overlayTex.Apply(false, false);

            // Clear painter dirty tiles after full regen
            if (painter != null) painter.ClearDirtyTiles();
        }

        /// <summary>
        /// Incremental update: only recomputes pixels in dirty tiles, then uploads both textures.
        /// Skips Invert/Dilate for speed — those are applied on stroke finish via full regen.
        /// Uses cached island mask + paint mask merged directly.
        /// Updates BOTH _previewTex and _overlayTex so the preview is correct in both
        /// direct mode and overlay mode.
        /// </summary>
        private void RegeneratePaintIncremental(MaskSettings settings, MaskPainter painter)
        {
            int size = settings.TextureSize;
            if (_cachedIslandMask == null || _cachedIslandMask.Length != size * size)
            {
                // Fallback to full regen if no cache available
                _dirty = true;
                return;
            }
            int pixelCount = size * size;
            if (_cachedPixels == null || _cachedPixels.Length != pixelCount)
            {
                _dirty = true;
                return;
            }
            if (_cachedOverlayPixels == null || _cachedOverlayPixels.Length != pixelCount)
            {
                _dirty = true;
                return;
            }

            var paintMask = painter.Mask;
            var dirtyTiles = painter.DirtyTiles;
            int gridDim = MaskPainter.TileGridDim;

            var selColor = (Color32)settings.PreviewFillSelectedColor;
            var unselColor = new Color32(255, 255, 255, 255);

            // Overlay colors
            byte overlayAlpha = (byte)Mathf.Clamp(Mathf.RoundToInt(settings.PreviewOverlayAlpha * 255f), 0, 255);
            var overlayCol = selColor;
            overlayCol.a = overlayAlpha;
            var transparent = new Color32(0, 0, 0, 0);

            int tileW = size / gridDim;
            int tileH = size / gridDim;

            for (int ty = 0; ty < gridDim; ty++)
            {
                for (int tx = 0; tx < gridDim; tx++)
                {
                    if (!dirtyTiles[ty * gridDim + tx]) continue;

                    int startX = tx * tileW;
                    int startY = ty * tileH;
                    int endX = (tx == gridDim - 1) ? size : startX + tileW;
                    int endY = (ty == gridDim - 1) ? size : startY + tileH;

                    for (int y = startY; y < endY; y++)
                    {
                        int rowBase = y * size;
                        for (int x = startX; x < endX; x++)
                        {
                            int idx = rowBase + x;
                            bool selected = (_cachedIslandMask[idx] | paintMask[idx]) != 0;
                            _cachedPixels[idx] = selected ? selColor : unselColor;
                            _cachedOverlayPixels[idx] = selected ? overlayCol : transparent;
                        }
                    }
                }
            }

            // Upload full pixel arrays (reliable across all Unity versions)
            _previewTex.SetPixels32(_cachedPixels);
            _previewTex.Apply(false, false);

            _overlayTex.SetPixels32(_cachedOverlayPixels);
            _overlayTex.Apply(false, false);

            painter.ClearDirtyTiles();
        }

        /// <summary>Reusable buffer for tile sub-rect pixel uploads.</summary>
        private Color32[] _tileBuffer;

        private void EnsureTileBuffer(int minSize)
        {
            if (_tileBuffer == null || _tileBuffer.Length < minSize)
                _tileBuffer = new Color32[minSize];
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

            // Rectangle selection preview
            if (_isRectPainting)
                DrawRectPreview(localLastImgRect);

            // Lasso stroke preview
            if (_isLassoPainting && _lassoPoints.Count >= 2)
                DrawLassoPreview(localLastImgRect);

            GUI.EndGroup();

            // Frame around base square (at zoom=1 boundary indicator)
            DrawFrame();
        }

        private void DrawFrame()
        {
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

        private void DrawRectPreview(Rect localImgRect)
        {
            float x0 = localImgRect.x + _rectStartUV.x   * localImgRect.width;
            float y0 = localImgRect.y + (1f - _rectStartUV.y)   * localImgRect.height;
            float x1 = localImgRect.x + _rectCurrentUV.x * localImgRect.width;
            float y1 = localImgRect.y + (1f - _rectCurrentUV.y) * localImgRect.height;

            float rx = Mathf.Min(x0, x1);
            float ry = Mathf.Min(y0, y1);
            float rw = Mathf.Abs(x1 - x0);
            float rh = Mathf.Abs(y1 - y0);

            EditorGUI.DrawRect(new Rect(rx, ry, rw, rh), new Color(0.3f, 0.7f, 1f, 0.25f));

            Handles.BeginGUI();
            Handles.color = Color.cyan;
            var tl = new Vector3(rx,      ry);
            var tr = new Vector3(rx + rw, ry);
            var br = new Vector3(rx + rw, ry + rh);
            var bl = new Vector3(rx,      ry + rh);
            Handles.DrawLine(tl, tr);
            Handles.DrawLine(tr, br);
            Handles.DrawLine(br, bl);
            Handles.DrawLine(bl, tl);
            Handles.EndGUI();
        }

        private void DrawLassoPreview(Rect localImgRect)
        {
            Handles.BeginGUI();
            Handles.color = Color.yellow;

            for (int i = 0; i < _lassoPoints.Count - 1; i++)
            {
                float ax = localImgRect.x + _lassoPoints[i].x     * localImgRect.width;
                float ay = localImgRect.y + (1f - _lassoPoints[i].y)     * localImgRect.height;
                float bx = localImgRect.x + _lassoPoints[i + 1].x * localImgRect.width;
                float by = localImgRect.y + (1f - _lassoPoints[i + 1].y) * localImgRect.height;
                Handles.DrawLine(new Vector3(ax, ay), new Vector3(bx, by));
            }

            // Closing dashed line (last point → first point)
            if (_lassoPoints.Count >= 3)
            {
                float ax = localImgRect.x + _lassoPoints[_lassoPoints.Count - 1].x * localImgRect.width;
                float ay = localImgRect.y + (1f - _lassoPoints[_lassoPoints.Count - 1].y) * localImgRect.height;
                float bx = localImgRect.x + _lassoPoints[0].x * localImgRect.width;
                float by = localImgRect.y + (1f - _lassoPoints[0].y) * localImgRect.height;
                Handles.color = new Color(1f, 1f, 0f, 0.5f);
                Handles.DrawDottedLine(new Vector3(ax, ay), new Vector3(bx, by), 5f);
            }

            Handles.EndGUI();
        }

        private static void DestroyTex(ref Texture2D tex)
        {
            if (tex != null) { UnityEngine.Object.DestroyImmediate(tex); tex = null; }
        }
    }
}
