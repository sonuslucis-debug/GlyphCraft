using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using GlyphCraft.Services;

namespace GlyphCraft.Controls
{
    /// <summary>Virtualised glyph grid - only the visible rows are drawn, so huge fonts stay fast.</summary>
    public sealed class GlyphGrid : FrameworkElement
    {
        ScrollBar _bar;
        FontFace _face;
        List<int> _codes = new List<int>();
        int _sel = -1, _hot = -1, _cols = 1;
        double _cell = 56, _offset;

        static readonly Pen GridPen = GlyphDraw.MakePen(GlyphDraw.LineBrush, 1);

        public event Action<int> SelectionChanged;
        public event Action<int> Activated;      // double-click / Enter
        public event Action<int> Hover;          // -1 when the mouse leaves
        public event Action CopyRequested;       // Ctrl+C

        public GlyphGrid()
        {
            Focusable = true;
            FocusVisualStyle = null;
            ClipToBounds = true;
        }

        public int SelectedCode { get { return _sel >= 0 && _sel < _codes.Count ? _codes[_sel] : -1; } }
        public int Count { get { return _codes.Count; } }

        public void Attach(ScrollBar bar)
        {
            _bar = bar;
            _bar.ValueChanged += delegate { _offset = _bar.Value; InvalidateVisual(); };
        }

        public void SetData(FontFace face, List<int> codes, int preferCode)
        {
            _face = face;
            _codes = codes ?? new List<int>();
            _offset = 0;
            if (_bar != null) _bar.Value = 0;
            _sel = _codes.IndexOf(preferCode);
            if (_sel < 0 && _codes.Count > 0) _sel = 0;
            _hot = -1;
            LayoutCells();
            if (_sel >= 0) EnsureVisible();
            InvalidateVisual();
            RaiseSelection();
        }

        public bool Select(int code)
        {
            int i = _codes.IndexOf(code);
            if (i < 0) return false;
            SetSel(i);
            return true;
        }

        void RaiseSelection()
        {
            if (SelectionChanged != null && _sel >= 0) SelectionChanged(_codes[_sel]);
        }

        void SetSel(int i)
        {
            if (i < 0 || i >= _codes.Count) return;
            _sel = i;
            EnsureVisible();
            InvalidateVisual();
            RaiseSelection();
        }

        void LayoutCells()
        {
            double w = ActualWidth, h = ActualHeight;
            if (w <= 0 || h <= 0) return;
            _cols = Math.Max(1, (int)(w / 54));
            _cell = w / _cols;
            int rows = (_codes.Count + _cols - 1) / _cols;
            double total = rows * _cell;
            if (_bar != null)
            {
                _bar.Minimum = 0;
                _bar.Maximum = Math.Max(0, total - h);
                _bar.ViewportSize = h;
                _bar.LargeChange = Math.Max(_cell, h - _cell);
                _bar.SmallChange = _cell;
                _bar.IsEnabled = total > h;
                if (_bar.Value > _bar.Maximum) _bar.Value = _bar.Maximum;
                _offset = _bar.Value;
            }
        }

        void EnsureVisible()
        {
            if (_bar == null || _sel < 0) return;
            double h = ActualHeight;
            double top = (_sel / _cols) * _cell;
            if (top < _offset) _bar.Value = top;
            else if (top + _cell > _offset + h) _bar.Value = Math.Min(_bar.Maximum, top + _cell - h);
            _offset = _bar.Value;
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            LayoutCells();
            if (_sel >= 0) EnsureVisible();
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, w, h));
            if (_face == null || _codes.Count == 0) return;

            double ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            double size = _cell * 0.5;
            int firstRow = Math.Max(0, (int)(_offset / _cell));
            int lastRow = (int)((_offset + h) / _cell);

            for (int r = firstRow; r <= lastRow; r++)
            {
                for (int c = 0; c < _cols; c++)
                {
                    int idx = r * _cols + c;
                    if (idx >= _codes.Count) break;
                    var rect = new Rect(c * _cell, r * _cell - _offset, _cell, _cell);
                    bool sel = idx == _sel;
                    Brush fill = sel ? GlyphDraw.Navy : (idx == _hot ? GlyphDraw.Tint : null);
                    dc.DrawRectangle(fill, GridPen, rect);

                    ushort gi;
                    if (!_face.Map.TryGetValue(_codes[idx], out gi)) continue;
                    double adv = _face.Advance(gi) * size;
                    var origin = new Point(rect.X + (_cell - adv) / 2, rect.Y + _cell * 0.5 + size * 0.35);
                    dc.PushClip(new RectangleGeometry(rect));
                    GlyphDraw.DrawGlyph(dc, sel ? Brushes.White : GlyphDraw.Navy, _face, gi, size, origin, ppd);
                    dc.Pop();
                }
            }
        }

        int IndexAt(Point p)
        {
            if (_cols < 1 || p.X < 0 || p.X >= _cols * _cell || p.Y < 0) return -1;
            int c = (int)(p.X / _cell);
            int r = (int)((p.Y + _offset) / _cell);
            int i = r * _cols + c;
            return i >= 0 && i < _codes.Count ? i : -1;
        }

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            int i = IndexAt(e.GetPosition(this));
            if (i < 0) return;
            SetSel(i);
            if (e.ChangedButton == MouseButton.Left && e.ClickCount == 2 && Activated != null)
                Activated(_codes[i]);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int i = IndexAt(e.GetPosition(this));
            if (i != _hot)
            {
                _hot = i;
                InvalidateVisual();
                if (Hover != null) Hover(i >= 0 ? _codes[i] : -1);
            }
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            base.OnMouseLeave(e);
            _hot = -1;
            InvalidateVisual();
            if (Hover != null) Hover(-1);
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            base.OnMouseWheel(e);
            if (_bar == null) return;
            double v = _bar.Value - e.Delta / 120.0 * _cell * 1.5;
            _bar.Value = Math.Max(0, Math.Min(_bar.Maximum, v));
            e.Handled = true;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (_codes.Count == 0) return;
            int page = Math.Max(1, (int)(ActualHeight / _cell) - 1) * _cols;
            int cur = _sel < 0 ? 0 : _sel;
            switch (e.Key)
            {
                case Key.Left: SetSel(cur - 1); break;
                case Key.Right: SetSel(cur + 1); break;
                case Key.Up: SetSel(cur - _cols); break;
                case Key.Down: SetSel(cur + _cols); break;
                case Key.Home: SetSel(0); break;
                case Key.End: SetSel(_codes.Count - 1); break;
                case Key.PageUp: SetSel(Math.Max(0, cur - page)); break;
                case Key.PageDown: SetSel(Math.Min(_codes.Count - 1, cur + page)); break;
                case Key.Enter:
                    if (Activated != null && _sel >= 0) Activated(_codes[_sel]);
                    break;
                case Key.C:
                    if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
                    {
                        if (CopyRequested != null) CopyRequested();
                        break;
                    }
                    return;
                default: return;
            }
            e.Handled = true;
        }
    }
}
