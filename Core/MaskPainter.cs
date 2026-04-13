// MaskPainter.cs - Handles hand-painting over UV masks with brush and eraser
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Dennoko.UVTools.Core
{
    /// <summary>
    /// Manages the hand-painted mask layer, brush operations, and undo/redo history.
    /// Uses a tile grid to track dirty regions for incremental texture updates.
    /// </summary>
    public class MaskPainter
    {
        private byte[] _paintMask;
        private int _width;
        private int _height;

        private Stack<byte[]> _undoStack = new Stack<byte[]>();
        private Stack<byte[]> _redoStack = new Stack<byte[]>();
        private const int MaxHistory = 20;

        // --- Dirty tile tracking (grid-based) ---
        public const int TileGridDim = 16; // 16x16 grid → 256 tiles
        private bool[] _dirtyTiles = new bool[TileGridDim * TileGridDim];

        public byte[] Mask => _paintMask;
        public int Width => _width;
        public int Height => _height;
        public bool HasUndo => _undoStack.Count > 0;
        public bool HasRedo => _redoStack.Count > 0;
        public bool[] DirtyTiles => _dirtyTiles;

        /// <summary>Returns true if any tile is dirty.</summary>
        public bool HasDirtyTiles
        {
            get
            {
                for (int i = 0; i < _dirtyTiles.Length; i++)
                    if (_dirtyTiles[i]) return true;
                return false;
            }
        }

        public void ClearDirtyTiles()
        {
            Array.Clear(_dirtyTiles, 0, _dirtyTiles.Length);
        }

        public void MarkAllTilesDirty()
        {
            for (int i = 0; i < _dirtyTiles.Length; i++)
                _dirtyTiles[i] = true;
        }

        public MaskPainter(int size)
        {
            _width = size;
            _height = size;
            _paintMask = new byte[size * size];
        }

        public void EnsureSize(int size)
        {
            if (_width == size && _height == size) return;

            int oldWidth = _width;
            int oldHeight = _height;
            byte[] oldMask = _paintMask;

            _width = size;
            _height = size;
            _paintMask = new byte[size * size];

            // Nearest-neighbor resize
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int srcX = Mathf.Clamp((x * oldWidth) / size, 0, oldWidth - 1);
                    int srcY = Mathf.Clamp((y * oldHeight) / size, 0, oldHeight - 1);
                    _paintMask[y * size + x] = oldMask[srcY * oldWidth + srcX];
                }
            }

            // Clear history on resize to save memory/complexity, 
            // or we could resize history as well. Clearing is safer.
            _undoStack.Clear();
            _redoStack.Clear();
            MarkAllTilesDirty();
        }

        public void SaveUndoState()
        {
            if (_undoStack.Count >= MaxHistory)
            {
                var arr = _undoStack.ToArray();
                _undoStack.Clear();
                // array is top-to-bottom. We want to skip the last element (bottom-most).
                for (int i = arr.Length - 2; i >= 0; i--)
                {
                    _undoStack.Push(arr[i]);
                }
            }

            byte[] snapshot = new byte[_paintMask.Length];
            Array.Copy(_paintMask, snapshot, _paintMask.Length);
            _undoStack.Push(snapshot);
            _redoStack.Clear();
        }

        public void Undo()
        {
            if (_undoStack.Count == 0) return;

            byte[] current = new byte[_paintMask.Length];
            Array.Copy(_paintMask, current, _paintMask.Length);
            _redoStack.Push(current);

            byte[] previous = _undoStack.Pop();
            Array.Copy(previous, _paintMask, _paintMask.Length);
            MarkAllTilesDirty();
        }

        public void Redo()
        {
            if (_redoStack.Count == 0) return;

            byte[] current = new byte[_paintMask.Length];
            Array.Copy(_paintMask, current, _paintMask.Length);
            _undoStack.Push(current);

            byte[] next = _redoStack.Pop();
            Array.Copy(next, _paintMask, _paintMask.Length);
            MarkAllTilesDirty();
        }

        public void Clear()
        {
            SaveUndoState();
            Array.Clear(_paintMask, 0, _paintMask.Length);
            MarkAllTilesDirty();
        }

        /// <summary>
        /// Paints a line with a circular brush using Bresenham's line algorithm.
        /// (uv coordinates are 0-1)
        /// </summary>
        public void PaintLine(Vector2 uvStart, Vector2 uvEnd, int brushRadius, bool erase)
        {
            int x0 = Mathf.Clamp(Mathf.FloorToInt(uvStart.x * _width), 0, _width - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(uvStart.y * _height), 0, _height - 1);
            int x1 = Mathf.Clamp(Mathf.FloorToInt(uvEnd.x * _width), 0, _width - 1);
            int y1 = Mathf.Clamp(Mathf.FloorToInt(uvEnd.y * _height), 0, _height - 1);

            int dx = Mathf.Abs(x1 - x0);
            int dy = Mathf.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1;
            int sy = y0 < y1 ? 1 : -1;
            int err = dx - dy;

            while (true)
            {
                PaintCircle(x0, y0, brushRadius, erase);

                if (x0 == x1 && y0 == y1) break;

                int e2 = 2 * err;
                if (e2 > -dy) { err -= dy; x0 += sx; }
                if (e2 < dx) { err += dx; y0 += sy; }
            }
        }

        public void PaintDot(Vector2 uv, int brushRadius, bool erase)
        {
            int cx = Mathf.Clamp(Mathf.FloorToInt(uv.x * _width), 0, _width - 1);
            int cy = Mathf.Clamp(Mathf.FloorToInt(uv.y * _height), 0, _height - 1);
            PaintCircle(cx, cy, brushRadius, erase);
        }

        /// <summary>
        /// Fills a rectangle defined by two UV corners.
        /// </summary>
        public void PaintRect(Vector2 uvStart, Vector2 uvEnd, bool erase)
        {
            int x0 = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(uvStart.x, uvEnd.x) * _width),  0, _width  - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(uvStart.y, uvEnd.y) * _height), 0, _height - 1);
            int x1 = Mathf.Clamp(Mathf.FloorToInt(Mathf.Max(uvStart.x, uvEnd.x) * _width),  0, _width  - 1);
            int y1 = Mathf.Clamp(Mathf.FloorToInt(Mathf.Max(uvStart.y, uvEnd.y) * _height), 0, _height - 1);

            if (x0 > x1 || y0 > y1) return;

            byte val = erase ? (byte)0 : (byte)255;

            int tMinX = Mathf.Clamp(x0 * TileGridDim / _width,  0, TileGridDim - 1);
            int tMaxX = Mathf.Clamp(x1 * TileGridDim / _width,  0, TileGridDim - 1);
            int tMinY = Mathf.Clamp(y0 * TileGridDim / _height, 0, TileGridDim - 1);
            int tMaxY = Mathf.Clamp(y1 * TileGridDim / _height, 0, TileGridDim - 1);
            for (int ty = tMinY; ty <= tMaxY; ty++)
                for (int tx = tMinX; tx <= tMaxX; tx++)
                    _dirtyTiles[ty * TileGridDim + tx] = true;

            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                    _paintMask[y * _width + x] = val;
        }

        /// <summary>
        /// Fills the interior of a polygon defined by UV points using a scanline algorithm.
        /// The polygon is automatically closed (last point connects back to first).
        /// </summary>
        public void PaintPolygon(List<Vector2> uvPoints, bool erase)
        {
            if (uvPoints == null || uvPoints.Count < 3) return;

            int n = uvPoints.Count;
            int[] px = new int[n];
            int[] py = new int[n];
            int minY = int.MaxValue, maxY = int.MinValue;

            for (int i = 0; i < n; i++)
            {
                px[i] = Mathf.Clamp(Mathf.FloorToInt(uvPoints[i].x * _width),  0, _width  - 1);
                py[i] = Mathf.Clamp(Mathf.FloorToInt(uvPoints[i].y * _height), 0, _height - 1);
                if (py[i] < minY) minY = py[i];
                if (py[i] > maxY) maxY = py[i];
            }

            byte val = erase ? (byte)0 : (byte)255;
            var intersections = new List<int>();

            for (int y = minY; y <= maxY; y++)
            {
                intersections.Clear();
                for (int i = 0; i < n; i++)
                {
                    int j = (i + 1) % n;
                    int ay = py[i], by = py[j];
                    if ((ay <= y && y < by) || (by <= y && y < ay))
                    {
                        float t = (float)(y - ay) / (by - ay);
                        int x = Mathf.RoundToInt(px[i] + t * (px[j] - px[i]));
                        intersections.Add(x);
                    }
                }
                intersections.Sort();

                for (int i = 0; i + 1 < intersections.Count; i += 2)
                {
                    int xStart = Mathf.Clamp(intersections[i],     0, _width - 1);
                    int xEnd   = Mathf.Clamp(intersections[i + 1], 0, _width - 1);

                    int tMinX = Mathf.Clamp(xStart * TileGridDim / _width, 0, TileGridDim - 1);
                    int tMaxX = Mathf.Clamp(xEnd   * TileGridDim / _width, 0, TileGridDim - 1);
                    int ty    = Mathf.Clamp(y * TileGridDim / _height,     0, TileGridDim - 1);
                    for (int tx = tMinX; tx <= tMaxX; tx++)
                        _dirtyTiles[ty * TileGridDim + tx] = true;

                    for (int x = xStart; x <= xEnd; x++)
                        _paintMask[y * _width + x] = val;
                }
            }
        }

        private void PaintCircle(int cx, int cy, int r, bool erase)
        {
            byte val = erase ? (byte)0 : (byte)255;
            int r2 = r * r;

            int minX = Mathf.Max(0, cx - r);
            int maxX = Mathf.Min(_width - 1, cx + r);
            int minY = Mathf.Max(0, cy - r);
            int maxY = Mathf.Min(_height - 1, cy + r);

            // Mark affected tiles dirty (bounding box of brush)
            int tMinX = Mathf.Clamp(minX * TileGridDim / _width, 0, TileGridDim - 1);
            int tMaxX = Mathf.Clamp(maxX * TileGridDim / _width, 0, TileGridDim - 1);
            int tMinY = Mathf.Clamp(minY * TileGridDim / _height, 0, TileGridDim - 1);
            int tMaxY = Mathf.Clamp(maxY * TileGridDim / _height, 0, TileGridDim - 1);
            for (int ty = tMinY; ty <= tMaxY; ty++)
                for (int tx = tMinX; tx <= tMaxX; tx++)
                    _dirtyTiles[ty * TileGridDim + tx] = true;

            for (int y = minY; y <= maxY; y++)
            {
                int dy = y - cy;
                int dy2 = dy * dy;
                for (int x = minX; x <= maxX; x++)
                {
                    int dx = x - cx;
                    if (dx * dx + dy2 <= r2)
                    {
                        _paintMask[y * _width + x] = val;
                    }
                }
            }
        }
    }
}
