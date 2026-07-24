// UVPreviewElement.cs - UI Toolkit custom element for the UV mask preview.
// Replaces the IMGUI-based UVPreviewDrawer. The texture generation pipeline
// (cached island mask + tile-grid incremental updates) is ported unchanged;
// only rendering (generateVisualContent + Painter2D) and input handling
// (Pointer/Wheel events with pointer capture) are UI Toolkit based.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Dennoko.UVTools.Core;
using Dennoko.UVTools.Data;

namespace Dennoko.UVTools.UI
{
    /// <summary>
    /// Draws the UV mask preview texture and border overlays.
    /// Supports click-based island selection, mouse wheel zoom, middle-click pan,
    /// and hand-painting (brush / rect / lasso / eraser) with incremental
    /// tile-based texture updates.
    /// </summary>
    public class UVPreviewElement : VisualElement
    {
        public new class UxmlFactory : UxmlFactory<UVPreviewElement, UxmlTraits> { }

        // --- Context (supplied by the window; not owned) ---
        private UVAnalysis _analysis;
        private HashSet<int> _selectedIslands;
        private MaskSettings _settings;
        private MaskPainter _painter;
        private Texture _baseTexture;

        // --- Textures ---
        private Texture2D _previewTex;
        private Texture2D _overlayTex;
        private bool _dirty = true;

        // --- Cached data for incremental painting ---
        private byte[] _cachedIslandMask;        // Cached BuildUnionMask result (no invert/dilate)
        private Color32[] _cachedPixels;          // Reusable Color32 buffer for _previewTex
        private Color32[] _cachedOverlayPixels;   // Reusable Color32 buffer for _overlayTex
        private bool _paintDirty = false;         // Only paint changed (fast incremental path)

        // --- Label map for click detection ---
        private int[] _labelMap;
        private int _labelMapSize = 0;

        // --- Viewport / zoom / pan state ---
        private float _zoomLevel = 1f;
        private Vector2 _panOffset = Vector2.zero;
        private Rect _lastImgRect;               // local coords, updated on render/input
        private bool _isPanning = false;
        private int _capturedPointerId = -1;

        // --- Style constants (functional editing colours, not theme colours) ---
        private static readonly Color UVFrameColor = new Color(0.25f, 0.25f, 0.25f, 1f);
        private static readonly Color IslandBorderColor = new Color(1f, 0.5f, 0f, 1f);
        private static readonly Color RectFillColor = new Color(0.3f, 0.7f, 1f, 0.25f);
        private static readonly Color LassoCloseColor = new Color(1f, 1f, 0f, 0.5f);

        // Border edges are rendered in dedicated child "layers" (see UpdateBorderLayers).
        // UI Toolkit's 65535-vertex cap is PER VisualElement and applies to the CUMULATIVE
        // geometry generated for that element, so flushing Painter2D strokes within a single
        // element does NOT avoid it — dense meshes produce far more than 65535 border-line
        // vertices. Unity's recommended workaround is to distribute the geometry across
        // multiple VisualElements; each layer draws at most MaxEdgesPerLayer edges.
        // Each edge is one MoveTo+LineTo (width 1) tessellating to a handful of vertices;
        // 6000 edges keeps a single layer's mesh comfortably under 65535.
        private const int MaxEdgesPerLayer = 6000;
        private readonly List<BorderLayer> _borderLayers = new List<BorderLayer>();

        // --- Events ---
        /// <summary>Fired when an island is clicked in the preview. -1 = empty area.</summary>
        public event Action<int> OnIslandClicked;
        public event Action OnPaintStrokeFinished;
        /// <summary>Fired when zoom or pan changes.</summary>
        public event Action OnViewChanged;

        // --- Paint State (Brush) ---
        private Vector2 _lastPaintUV;
        private bool _isPaintingValid = false;

        // --- Paint State (Rectangle) ---
        private Vector2 _rectStartUV;
        private Vector2 _rectCurrentUV;
        private bool _isRectPainting = false;

        // --- Paint State (Lasso) ---
        private readonly List<Vector2> _lassoPoints = new List<Vector2>();
        private bool _isLassoPainting = false;
        private Vector2 _lastLassoUV;
        private const float LassoMinUVStep = 0.003f;

        // --- Hint label (shown while no analysis is available) ---
        private readonly Label _hintLabel;

        // ────────────────────────────────────────────────────────────────────
        // Public API
        // ────────────────────────────────────────────────────────────────────

        /// <summary>True while a paint stroke is in progress (pointer held down in paint mode).</summary>
        public bool IsPainting => _isPaintingValid || _isRectPainting || _isLassoPainting;

        /// <summary>Current zoom level (1.0 = fit).</summary>
        public float ZoomLevel => _zoomLevel;

        /// <summary>
        /// The semi-transparent overlay texture that encodes the current selection and
        /// hand-painted mask. Projected onto the 3D mesh in the scene view.
        /// Returns null until the first preview has been rendered.
        /// </summary>
        public Texture2D OverlayTexture => _overlayTex;

        public UVPreviewElement()
        {
            _hintLabel = new Label();
            _hintLabel.AddToClassList("maskmaker-preview-hint");
            _hintLabel.pickingMode = PickingMode.Ignore;
            Add(_hintLabel);

            focusable = false;

            generateVisualContent += OnGenerateVisualContent;

            RegisterCallback<WheelEvent>(OnWheel);
            RegisterCallback<PointerDownEvent>(OnPointerDown);
            RegisterCallback<PointerMoveEvent>(OnPointerMove);
            RegisterCallback<PointerUpEvent>(OnPointerUp);
            RegisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
            RegisterCallback<DetachFromPanelEvent>(_ => Dispose());
            RegisterCallback<GeometryChangedEvent>(_ => { MarkDirtyRepaint(); UpdateBorderLayers(); });
        }

        /// <summary>
        /// Supplies the data the preview renders from. Call whenever the analysis,
        /// selection set instance, settings or painter change. References are held;
        /// the window remains the owner.
        /// </summary>
        public void SetContext(UVAnalysis analysis, HashSet<int> selectedIslands,
            MaskSettings settings, MaskPainter painter)
        {
            _analysis = analysis;
            _selectedIslands = selectedIslands;
            _settings = settings;
            _painter = painter;
            UpdateHintVisibility();
            UpdateBorderLayers();
            MarkDirtyRepaint();
        }

        /// <summary>Updates the selection set reference (window may reassign the HashSet).</summary>
        public void SetSelection(HashSet<int> selectedIslands)
        {
            _selectedIslands = selectedIslands;
        }

        /// <summary>Base texture drawn under the overlay when PreviewOverlayBaseTex is on.</summary>
        public void SetBaseTexture(Texture baseTexture)
        {
            _baseTexture = baseTexture;
            MarkDirtyRepaint();
        }

        /// <summary>Hint text shown while there is no analysis (localized by the window).</summary>
        public void SetHintText(string text)
        {
            _hintLabel.text = text;
        }

        /// <summary>Marks the preview texture as needing full regeneration.</summary>
        public void MarkDirty()
        {
            _dirty = true;
            _cachedIslandMask = null; // Invalidate island cache
            UpdateHintVisibility();
            UpdateBorderLayers();
            MarkDirtyRepaint();
        }

        /// <summary>Resets zoom and pan to default (fit view).</summary>
        public void ResetView()
        {
            _zoomLevel = 1f;
            _panOffset = Vector2.zero;
            MarkDirtyRepaint();
            RepaintBorderLayers();
            OnViewChanged?.Invoke();
        }

        /// <summary>Invalidates the label map. Call when analysis or resolution changes.</summary>
        public void InvalidateLabelMap()
        {
            _labelMap = null;
            _labelMapSize = 0;
        }

        /// <summary>Disposes texture resources and releases any active pointer capture.</summary>
        public void Dispose()
        {
            if (_capturedPointerId >= 0 && this.HasPointerCapture(_capturedPointerId))
                this.ReleasePointer(_capturedPointerId);
            _capturedPointerId = -1;
            _isPaintingValid = false;
            _isRectPainting = false;
            _isLassoPainting = false;
            _isPanning = false;
            _lassoPoints.Clear();

            DestroyTex(ref _previewTex);
            DestroyTex(ref _overlayTex);
            _labelMap = null;
            _cachedIslandMask = null;
            _cachedPixels = null;
            _cachedOverlayPixels = null;
        }

        private void UpdateHintVisibility()
        {
            _hintLabel.style.display = _analysis == null ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // ────────────────────────────────────────────────────────────────────
        // View math (local element coordinates)
        // ────────────────────────────────────────────────────────────────────

        private Rect ComputeImgRect()
        {
            const float pad = 6f;
            Rect viewport = contentRect;
            var inner = new Rect(
                viewport.x + pad,
                viewport.y + pad,
                viewport.width - pad * 2f,
                viewport.height - pad * 2f);

            float baseSide = Mathf.Min(inner.width, inner.height);
            var viewCenter = new Vector2(
                inner.x + inner.width * 0.5f,
                inner.y + inner.height * 0.5f);

            float zoomedSize = baseSide * _zoomLevel;
            return new Rect(
                viewCenter.x - zoomedSize * 0.5f + _panOffset.x,
                viewCenter.y - zoomedSize * 0.5f + _panOffset.y,
                zoomedSize, zoomedSize);
        }

        private Vector2 ViewCenter()
        {
            Rect viewport = contentRect;
            const float pad = 6f;
            return new Vector2(
                viewport.x + pad + (viewport.width - pad * 2f) * 0.5f,
                viewport.y + pad + (viewport.height - pad * 2f) * 0.5f);
        }

        private Vector2 LocalToUV(Vector2 localPos)
        {
            float u = (localPos.x - _lastImgRect.x) / _lastImgRect.width;
            float v = 1f - (localPos.y - _lastImgRect.y) / _lastImgRect.height;
            return new Vector2(Mathf.Clamp01(u), Mathf.Clamp01(v));
        }

        // ────────────────────────────────────────────────────────────────────
        // Input handlers
        // ────────────────────────────────────────────────────────────────────

        private void OnWheel(WheelEvent e)
        {
            if (float.IsNaN(contentRect.width) || contentRect.width <= 0) return;

            float zoomDelta = -e.delta.y * 0.08f;
            float newZoom = Mathf.Clamp(_zoomLevel * Mathf.Exp(zoomDelta), 0.1f, 20f);

            Vector2 mouseOffset = (Vector2)e.localMousePosition - ViewCenter();
            _panOffset = mouseOffset + (_panOffset - mouseOffset) * (newZoom / _zoomLevel);
            _zoomLevel = newZoom;

            e.StopPropagation();
            MarkDirtyRepaint();
            RepaintBorderLayers();
            OnViewChanged?.Invoke();
        }

        private void OnPointerDown(PointerDownEvent e)
        {
            // Middle button: start panning
            if (e.button == 2)
            {
                _isPanning = true;
                _capturedPointerId = e.pointerId;
                this.CapturePointer(e.pointerId);
                e.StopPropagation();
                return;
            }

            if (e.button != 0 || _analysis == null || _settings == null) return;

            _lastImgRect = ComputeImgRect();
            Vector2 local = (Vector2)e.localPosition;
            if (!_lastImgRect.Contains(local)) return;

            if (_settings.IsPaintMode && _painter != null)
            {
                Vector2 startUV = LocalToUV(local);

                switch (_settings.PaintSubMode)
                {
                    case PaintSubMode.Brush:
                    case PaintSubMode.Eraser:
                        _painter.SaveUndoState();
                        _lastPaintUV = startUV;
                        _painter.PaintDot(_lastPaintUV, _settings.BrushSize,
                            _settings.PaintSubMode == PaintSubMode.Eraser);
                        _isPaintingValid = true;
                        _paintDirty = true;
                        break;

                    case PaintSubMode.Rect:
                        _painter.SaveUndoState();
                        _rectStartUV = startUV;
                        _rectCurrentUV = startUV;
                        _isRectPainting = true;
                        break;

                    case PaintSubMode.Lasso:
                        _painter.SaveUndoState();
                        _lassoPoints.Clear();
                        _lassoPoints.Add(startUV);
                        _lastLassoUV = startUV;
                        _isLassoPainting = true;
                        break;
                }

                _capturedPointerId = e.pointerId;
                this.CapturePointer(e.pointerId);
                e.StopPropagation();
                MarkDirtyRepaint();
            }
            else if (!_settings.IsPaintMode && _labelMap != null && _labelMapSize > 0)
            {
                Vector2 uv = LocalToUV(local);
                int px = Mathf.Clamp(Mathf.FloorToInt(uv.x * _labelMapSize), 0, _labelMapSize - 1);
                int py = Mathf.Clamp(Mathf.FloorToInt(uv.y * _labelMapSize), 0, _labelMapSize - 1);
                int islandIdx = _labelMap[py * _labelMapSize + px];

                OnIslandClicked?.Invoke(islandIdx);
                e.StopPropagation();
            }
        }

        private void OnPointerMove(PointerMoveEvent e)
        {
            if (_isPanning && this.HasPointerCapture(e.pointerId))
            {
                _panOffset += (Vector2)e.deltaPosition;
                e.StopPropagation();
                MarkDirtyRepaint();
                RepaintBorderLayers();
                OnViewChanged?.Invoke();
                return;
            }

            if (!IsPainting || !this.HasPointerCapture(e.pointerId)) return;
            if (_settings == null || _painter == null) return;

            _lastImgRect = ComputeImgRect();
            Vector2 currentUV = LocalToUV((Vector2)e.localPosition);

            if (_isPaintingValid)
            {
                bool erase = _settings.PaintSubMode == PaintSubMode.Eraser;
                _painter.PaintLine(_lastPaintUV, currentUV, _settings.BrushSize, erase);
                _lastPaintUV = currentUV;
                _paintDirty = true;
            }
            else if (_isRectPainting)
            {
                _rectCurrentUV = currentUV;
            }
            else if (_isLassoPainting)
            {
                if (Vector2.Distance(currentUV, _lastLassoUV) >= LassoMinUVStep)
                {
                    _lassoPoints.Add(currentUV);
                    _lastLassoUV = currentUV;
                }
            }

            e.StopPropagation();
            MarkDirtyRepaint();
        }

        private void OnPointerUp(PointerUpEvent e)
        {
            if (!this.HasPointerCapture(e.pointerId)) return;

            // Finish the stroke BEFORE releasing the pointer: ReleasePointer
            // dispatches PointerCaptureOutEvent synchronously, and the capture-out
            // handler would otherwise abandon the still-pending rect/lasso shape.
            if (!_isPanning)
            {
                _lastImgRect = ComputeImgRect();
                Vector2 endUV = LocalToUV((Vector2)e.localPosition);

                if (_isPaintingValid)
                {
                    _isPaintingValid = false;
                    _dirty = true;
                    OnPaintStrokeFinished?.Invoke();
                }
                else if (_isRectPainting)
                {
                    _rectCurrentUV = endUV;
                    _painter?.PaintRect(_rectStartUV, _rectCurrentUV, false);
                    _isRectPainting = false;
                    _dirty = true;
                    OnPaintStrokeFinished?.Invoke();
                }
                else if (_isLassoPainting)
                {
                    _lassoPoints.Add(endUV);
                    _painter?.PaintPolygon(_lassoPoints, false);
                    _lassoPoints.Clear();
                    _isLassoPainting = false;
                    _dirty = true;
                    OnPaintStrokeFinished?.Invoke();
                }
            }

            _isPanning = false;
            _capturedPointerId = -1;
            this.ReleasePointer(e.pointerId);
            e.StopPropagation();
            MarkDirtyRepaint();
        }

        private void OnPointerCaptureOut(PointerCaptureOutEvent e)
        {
            // Capture was lost unexpectedly (window closed, another element grabbed it):
            // abandon the in-progress interaction without applying the pending shape.
            if (_isPaintingValid || _isRectPainting || _isLassoPainting)
            {
                _isPaintingValid = false;
                _isRectPainting = false;
                _isLassoPainting = false;
                _lassoPoints.Clear();
                _dirty = true;
                OnPaintStrokeFinished?.Invoke();
                MarkDirtyRepaint();
            }
            _isPanning = false;
            _capturedPointerId = -1;
        }

        // ────────────────────────────────────────────────────────────────────
        // Rendering
        // ────────────────────────────────────────────────────────────────────

        private void OnGenerateVisualContent(MeshGenerationContext mgc)
        {
            Rect viewport = contentRect;
            if (viewport.width <= 0 || viewport.height <= 0) return;

            if (_analysis == null || _settings == null)
            {
                DrawFrame(mgc.painter2D, viewport);
                return;
            }

            // Ensure textures and label map
            EnsureTextures(_settings.TextureSize);
            EnsureLabelMap(_analysis, _settings.TextureSize);

            // Lazy regeneration right before drawing (same semantics as the old
            // IMGUI implementation which regenerated on the Repaint event).
            if (_dirty)
            {
                RegenerateTexturesFull(_analysis, _selectedIslands, _settings, _painter);
                _dirty = false;
                _paintDirty = false;
            }
            else if (_paintDirty && _painter != null)
            {
                RegeneratePaintIncremental(_settings, _painter);
                _paintDirty = false;
            }

            _lastImgRect = ComputeImgRect();

            // Textured quads (clipped by overflow: hidden on this element)
            if (_settings.PreviewOverlayBaseTex && _baseTexture != null)
            {
                DrawTexturedQuad(mgc, _lastImgRect, _baseTexture);
                if (_overlayTex != null)
                    DrawTexturedQuad(mgc, _lastImgRect, _overlayTex);
            }
            else if (_previewTex != null)
            {
                DrawTexturedQuad(mgc, _lastImgRect, _previewTex);
            }

            var p = mgc.painter2D;

            // UV island boundary lines are drawn in child BorderLayer elements
            // (see UpdateBorderLayers / DrawBorderEdgeRange), NOT here, because a
            // dense mesh's border geometry exceeds UI Toolkit's per-VisualElement
            // 65535-vertex cap and must be spread across multiple elements.

            // Rectangle selection preview
            if (_isRectPainting)
                DrawRectPreview(p, _lastImgRect);

            // Lasso stroke preview
            if (_isLassoPainting && _lassoPoints.Count >= 2)
                DrawLassoPreview(p, _lastImgRect);

            // Frame around the viewport
            DrawFrame(p, viewport);
        }

        /// <summary>Draws a textured quad covering <paramref name="rect"/> (v=1 at top, GUI convention).</summary>
        private static void DrawTexturedQuad(MeshGenerationContext mgc, Rect rect, Texture tex)
        {
            MeshWriteData mwd = mgc.Allocate(4, 6, tex);

            // Remap UVs into the atlas sub-region (uvRegion is (0,0,1,1) for non-atlased textures)
            Rect uvR = mwd.uvRegion;
            Vector2 uvTL = new Vector2(uvR.x, uvR.y + uvR.height);
            Vector2 uvTR = new Vector2(uvR.x + uvR.width, uvR.y + uvR.height);
            Vector2 uvBR = new Vector2(uvR.x + uvR.width, uvR.y);
            Vector2 uvBL = new Vector2(uvR.x, uvR.y);

            Color32 tint = new Color32(255, 255, 255, 255);
            mwd.SetNextVertex(new Vertex { position = new Vector3(rect.xMin, rect.yMin, Vertex.nearZ), tint = tint, uv = uvTL });
            mwd.SetNextVertex(new Vertex { position = new Vector3(rect.xMax, rect.yMin, Vertex.nearZ), tint = tint, uv = uvTR });
            mwd.SetNextVertex(new Vertex { position = new Vector3(rect.xMax, rect.yMax, Vertex.nearZ), tint = tint, uv = uvBR });
            mwd.SetNextVertex(new Vertex { position = new Vector3(rect.xMin, rect.yMax, Vertex.nearZ), tint = tint, uv = uvBL });

            mwd.SetNextIndex(0); mwd.SetNextIndex(1); mwd.SetNextIndex(2);
            mwd.SetNextIndex(2); mwd.SetNextIndex(3); mwd.SetNextIndex(0);
        }

        private static void DrawFrame(Painter2D p, Rect viewport)
        {
            p.strokeColor = UVFrameColor;
            p.lineWidth = 1f;
            p.BeginPath();
            p.MoveTo(new Vector2(viewport.xMin + 0.5f, viewport.yMin + 0.5f));
            p.LineTo(new Vector2(viewport.xMax - 0.5f, viewport.yMin + 0.5f));
            p.LineTo(new Vector2(viewport.xMax - 0.5f, viewport.yMax - 0.5f));
            p.LineTo(new Vector2(viewport.xMin + 0.5f, viewport.yMax - 0.5f));
            p.ClosePath();
            p.Stroke();
        }

        private void DrawRectPreview(Painter2D p, Rect imgRect)
        {
            float x0 = imgRect.x + _rectStartUV.x * imgRect.width;
            float y0 = imgRect.y + (1f - _rectStartUV.y) * imgRect.height;
            float x1 = imgRect.x + _rectCurrentUV.x * imgRect.width;
            float y1 = imgRect.y + (1f - _rectCurrentUV.y) * imgRect.height;

            float rx = Mathf.Min(x0, x1);
            float ry = Mathf.Min(y0, y1);
            float rw = Mathf.Abs(x1 - x0);
            float rh = Mathf.Abs(y1 - y0);

            p.fillColor = RectFillColor;
            p.BeginPath();
            p.MoveTo(new Vector2(rx, ry));
            p.LineTo(new Vector2(rx + rw, ry));
            p.LineTo(new Vector2(rx + rw, ry + rh));
            p.LineTo(new Vector2(rx, ry + rh));
            p.ClosePath();
            p.Fill();

            p.strokeColor = Color.cyan;
            p.lineWidth = 1f;
            p.Stroke();
        }

        private void DrawLassoPreview(Painter2D p, Rect imgRect)
        {
            p.strokeColor = Color.yellow;
            p.lineWidth = 1f;
            p.BeginPath();
            for (int i = 0; i < _lassoPoints.Count; i++)
            {
                float x = imgRect.x + _lassoPoints[i].x * imgRect.width;
                float y = imgRect.y + (1f - _lassoPoints[i].y) * imgRect.height;
                if (i == 0) p.MoveTo(new Vector2(x, y));
                else p.LineTo(new Vector2(x, y));
            }
            p.Stroke();

            // Closing line (last point → first point). Painter2D has no dashed
            // strokes, so a semi-transparent solid line stands in for the old
            // dotted closing line.
            if (_lassoPoints.Count >= 3)
            {
                float ax = imgRect.x + _lassoPoints[_lassoPoints.Count - 1].x * imgRect.width;
                float ay = imgRect.y + (1f - _lassoPoints[_lassoPoints.Count - 1].y) * imgRect.height;
                float bx = imgRect.x + _lassoPoints[0].x * imgRect.width;
                float by = imgRect.y + (1f - _lassoPoints[0].y) * imgRect.height;
                p.strokeColor = LassoCloseColor;
                p.BeginPath();
                p.MoveTo(new Vector2(ax, ay));
                p.LineTo(new Vector2(bx, by));
                p.Stroke();
            }
        }

        // ────────────────────────────────────────────────────────────────────
        // Island border overlay (distributed across child VisualElements)
        // ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Ensures the pool of child <see cref="BorderLayer"/> elements matches the
        /// current border-edge count and assigns each layer its edge range, then
        /// requests a repaint. Called when the analysis, border visibility, or size
        /// changes. Splitting the border lines across multiple elements is required
        /// because UI Toolkit's 65535-vertex cap is per VisualElement.
        /// </summary>
        private void UpdateBorderLayers()
        {
            int edgeCount = 0;
            bool show = _analysis != null && _settings != null && _settings.ShowIslandPreview
                        && _analysis.BorderEdges != null;
            if (show) edgeCount = _analysis.BorderEdges.Count;

            int needed = (edgeCount + MaxEdgesPerLayer - 1) / MaxEdgesPerLayer;

            // Grow the pool as needed (layers are reused/hidden, never destroyed, to
            // avoid hierarchy churn when the edge count fluctuates).
            while (_borderLayers.Count < needed)
            {
                var layer = new BorderLayer(this);
                _borderLayers.Add(layer);
                Add(layer);
            }

            for (int i = 0; i < _borderLayers.Count; i++)
            {
                var layer = _borderLayers[i];
                if (i < needed)
                {
                    int start = i * MaxEdgesPerLayer;
                    int end = Mathf.Min(start + MaxEdgesPerLayer, edgeCount);
                    layer.SetRange(start, end);
                    layer.style.display = DisplayStyle.Flex;
                    layer.MarkDirtyRepaint();
                }
                else if (layer.style.display != DisplayStyle.None)
                {
                    layer.style.display = DisplayStyle.None;
                }
            }
        }

        /// <summary>
        /// Requests a repaint of the currently visible border layers without changing
        /// the pool (used on view changes — zoom/pan — where only the transform moved).
        /// </summary>
        private void RepaintBorderLayers()
        {
            for (int i = 0; i < _borderLayers.Count; i++)
            {
                var layer = _borderLayers[i];
                if (layer.style.display != DisplayStyle.None)
                    layer.MarkDirtyRepaint();
            }
        }

        /// <summary>
        /// Refreshes the island border overlay. Call when the "Show Island Borders"
        /// setting toggles.
        /// </summary>
        public void RefreshBorderOverlay()
        {
            UpdateBorderLayers();
            MarkDirtyRepaint();
        }

        /// <summary>
        /// Draws a slice of the island border edges (indices [startEdge, endEdge))
        /// into <paramref name="mgc"/>. Invoked by a child <see cref="BorderLayer"/>;
        /// the coordinates are computed from this (parent) element's view transform,
        /// which is valid because each layer exactly overlays the parent's content box.
        /// </summary>
        internal void DrawBorderEdgeRange(MeshGenerationContext mgc, int startEdge, int endEdge)
        {
            if (_analysis == null || _settings == null || !_settings.ShowIslandPreview) return;
            var edges = _analysis.BorderEdges;
            if (edges == null) return;

            int count = edges.Count;
            if (startEdge < 0) startEdge = 0;
            if (endEdge > count) endEdge = count;
            if (startEdge >= endEdge) return;

            Rect r = ComputeImgRect();
            var p = mgc.painter2D;
            p.strokeColor = IslandBorderColor;
            p.lineWidth = 1f;
            p.BeginPath();
            for (int i = startEdge; i < endEdge; i++)
            {
                var be = edges[i];
                float ax = Mathf.Lerp(r.x, r.xMax, Mathf.Clamp01(be.uv0.x));
                float ay = Mathf.Lerp(r.yMax, r.y, Mathf.Clamp01(be.uv0.y));
                float bx = Mathf.Lerp(r.x, r.xMax, Mathf.Clamp01(be.uv1.x));
                float by = Mathf.Lerp(r.yMax, r.y, Mathf.Clamp01(be.uv1.y));
                p.MoveTo(new Vector2(ax, ay));
                p.LineTo(new Vector2(bx, by));
            }
            p.Stroke();
        }

        /// <summary>
        /// A transparent child element that renders one slice of the island border
        /// lines. Exists solely to keep each element's Painter2D vertex count under
        /// UI Toolkit's 65535-per-element cap. It overlays the parent's content box
        /// (absolute, full-size) so it shares the parent's coordinate space and clip.
        /// </summary>
        private sealed class BorderLayer : VisualElement
        {
            private readonly UVPreviewElement _owner;
            private int _startEdge;
            private int _endEdge;

            public BorderLayer(UVPreviewElement owner)
            {
                _owner = owner;
                pickingMode = PickingMode.Ignore;
                style.position = Position.Absolute;
                style.left = 0f;
                style.top = 0f;
                style.right = 0f;
                style.bottom = 0f;
                generateVisualContent += mgc => _owner.DrawBorderEdgeRange(mgc, _startEdge, _endEdge);
            }

            public void SetRange(int startEdge, int endEdge)
            {
                _startEdge = startEdge;
                _endEdge = endEdge;
            }
        }

        // ────────────────────────────────────────────────────────────────────
        // Texture management (ported unchanged from UVPreviewDrawer)
        // ────────────────────────────────────────────────────────────────────

        private void EnsureTextures(int size)
        {
            if (_previewTex == null || _previewTex.width != size)
            {
                DestroyTex(ref _previewTex);
                _previewTex = new Texture2D(size, size, TextureFormat.RGBA32, false, true)
                {
                    filterMode = FilterMode.Point,
                    wrapMode   = TextureWrapMode.Clamp,
                    name       = "UVMaskPreview",
                    hideFlags  = HideFlags.HideAndDontSave
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
                    name       = "UVMaskOverlay",
                    hideFlags  = HideFlags.HideAndDontSave
                };
                _dirty = true;
            }
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
        /// Full regeneration: rebuilds island mask from scratch, dilates it, then applies paint and invert.
        /// Called when selection/settings change or on stroke finish.
        /// </summary>
        private void RegenerateTexturesFull(UVAnalysis analysis, HashSet<int> selectedIslands, MaskSettings settings, MaskPainter painter)
        {
            int size = settings.TextureSize;
            if (painter != null) painter.EnsureSize(size);

            // Build and cache the island mask with the pixel margin already applied.
            // The margin never touches hand-painted strokes, so the incremental paint path
            // can simply OR the paint layer on top of this cache.
            _cachedIslandMask = MaskBuilder.BuildIslandMaskWithMargin(
                analysis, selectedIslands, size, size, settings.PixelMargin);

            // Working copy: merge paint + invert
            var mask = new byte[_cachedIslandMask.Length];
            Array.Copy(_cachedIslandMask, mask, mask.Length);
            MaskBuilder.ComposeFinalMask(mask, painter?.Mask, settings.InvertMask);

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
        /// Skips Invert for speed — it is applied on stroke finish via full regen.
        /// Uses the cached (already dilated) island mask + paint mask merged directly.
        /// </summary>
        private void RegeneratePaintIncremental(MaskSettings settings, MaskPainter painter)
        {
            int size = settings.TextureSize;
            if (_cachedIslandMask == null || _cachedIslandMask.Length != size * size)
            {
                // Fallback to full regen if no cache available
                RegenerateTexturesFull(_analysis, _selectedIslands, settings, painter);
                return;
            }
            int pixelCount = size * size;
            if (_cachedPixels == null || _cachedPixels.Length != pixelCount
                || _cachedOverlayPixels == null || _cachedOverlayPixels.Length != pixelCount)
            {
                RegenerateTexturesFull(_analysis, _selectedIslands, settings, painter);
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

        private static void DestroyTex(ref Texture2D tex)
        {
            if (tex != null) { UnityEngine.Object.DestroyImmediate(tex); tex = null; }
        }
    }
}
