using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using GlyphCraft.Services;

namespace GlyphCraft.Controls
{
    /// <summary>
    /// Virtualised glyph grid - only the visible rows are drawn, so huge fonts stay fast.
    /// Supports Explorer-style multi-selection: click, Ctrl+click, Shift+click, Ctrl+A.
    /// </summary>
    public sealed class GlyphGrid : FrameworkElement
    {
        ScrollBar _bar;
        FontFace _face;
        List<int> _codes = new List<int>();
        readonly HashSet<int> _selected = new HashSet<int>();
        int _sel = -1, _hot = -1, _cols = 1, _anchor = -1;
        double _cell = 56, _offset;

        static readonly Pen GridPen = GlyphDraw.MakePen(GlyphDraw.LineBrush, 1);
        static readonly Pen FocusPen = GlyphDraw.MakePen(GlyphDraw.Accent, 1.6);
        static readonly Pen TickPen = GlyphDraw.MakePen(Brushes.White, 1.4);
        static readonly Brush SelectionFill = GlyphDraw.Frozen(Color.FromRgb(0xD9, 0xE8, 0xF4));

        public event Action<int> SelectionChanged;   // primary/focused glyph (drives the big preview)
        public event Action SelectionSetChanged;     // any change to the multi-selection
        public event Action<int> Activated;          // double-click / Enter
        public event Action<int> Hover;              // -1 when the mouse leaves
        public event Action CopyRequested;           // Ctrl+C

        public GlyphGrid()
        {
            Focusable = true;
            FocusVisualStyle = null;
            ClipToBounds = true;
        }

        public int SelectedCode { get { return _sel >= 0 && _sel < _codes.Count ? _codes[_sel] : -1; } }
        public int Count { get { return _codes.Count; } }
        public int SelectedCount { get { return _selected.Count; } }

        /// <summary>The selected glyphs in display (code point) order.</summary>
        public IReadOnlyList<int> SelectedCodes
        {
            get
            {
                var list = new List<int>(_selected.Count);
                for (int i = 0; i < _codes.Count; i++)
                    if (_selected.Contains(_codes[i])) list.Add(_codes[i]);
                return list;
            }
        }

        /// <summary>All code points currently shown (used by export/print helpers).</summary>
        public IReadOnlyList<int> Codes { get { return _codes; } }

        public bool IsSelected(int code) { return _selected.Contains(code); }

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

            _selected.Clear();
            _sel = _codes.IndexOf(preferCode);
            if (_sel < 0 && _codes.Count > 0) _sel = 0;
            _anchor = _sel;
            if (_sel >= 0) _selected.Add(_codes[_sel]);

            _hot = -1;
            LayoutCells();
            if (_sel >= 0) EnsureVisible();
            InvalidateVisual();
            RaiseSelection();
            RaiseSetChanged();
        }

        public bool Select(int code)
        {
            int i = _codes.IndexOf(code);
            if (i < 0) return false;
            SelectOnly(i);
            return true;
        }

        public void SelectAll()
        {
            if (_codes.Count == 0) return;
            for (int i = 0; i < _codes.Count; i++) _selected.Add(_codes[i]);
            if (_anchor < 0) _anchor = 0;
            InvalidateVisual();
            RaiseSetChanged();
        }

        public void ClearSelection()
        {
            if (_selected.Count == 0) return;
            _selected.Clear();
            InvalidateVisual();
            RaiseSetChanged();
        }

        void RaiseSelection()
        {
            if (SelectionChanged != null && _sel >= 0 && _sel < _codes.Count) SelectionChanged(_codes[_sel]);
        }

        void RaiseSetChanged()
        {
            if (SelectionSetChanged != null) SelectionSetChanged();
        }

        void SelectOnly(int i)
        {
            if (i < 0 || i >= _codes.Count) return;
            _sel = i;
            _anchor = i;
            _selected.Clear();
            _selected.Add(_codes[i]);
            EnsureVisible();
            InvalidateVisual();
            RaiseSelection();
            RaiseSetChanged();
        }

        void Toggle(int i)
        {
            if (i < 0 || i >= _codes.Count) return;
            _sel = i;
            _anchor = i;
            int code = _codes[i];
            if (!_selected.Remove(code)) _selected.Add(code);
            EnsureVisible();
            InvalidateVisual();
            RaiseSelection();
            RaiseSetChanged();
        }

        void SelectRange(int i, bool add)
        {
            if (i < 0 || i >= _codes.Count) return;
            int a = _anchor >= 0 ? _anchor : _sel;
            if (a < 0 || a >= _codes.Count) a = i;
            int lo = Math.Min(a, i), hi = Math.Max(a, i);
            if (!add) _selected.Clear();
            for (int k = lo; k <= hi; k++) _selected.Add(_codes[k]);
            _sel = i;
            EnsureVisible();
            InvalidateVisual();
            RaiseSelection();
            RaiseSetChanged();
        }

        void CollapseToPrimary()
        {
            if (_sel < 0 || _sel >= _codes.Count) return;
            if (_selected.Count == 1 && _selected.Contains(_codes[_sel])) return;
            _selected.Clear();
            _selected.Add(_codes[_sel]);
            _anchor = _sel;
            InvalidateVisual();
            RaiseSetChanged();
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
                    bool primary = idx == _sel;
                    bool selected = _selected.Contains(_codes[idx]);

                    Brush fill = primary && selected ? GlyphDraw.Navy
                               : selected ? SelectionFill
                               : idx == _hot ? GlyphDraw.Tint
                               : null;
                    dc.DrawRectangle(fill, GridPen, rect);
                    if (primary && !selected) dc.DrawRectangle(null, FocusPen, rect);

                    ushort gi;
                    if (!_face.Map.TryGetValue(_codes[idx], out gi)) continue;
                    double adv = _face.Advance(gi) * size;
                    var origin = new Point(rect.X + (_cell - adv) / 2, rect.Y + _cell * 0.5 + size * 0.35);
                    Brush ink = primary && selected ? Brushes.White : selected ? GlyphDraw.Accent : GlyphDraw.Navy;
                    dc.PushClip(new RectangleGeometry(rect));
                    GlyphDraw.DrawGlyph(dc, ink, _face, gi, size, origin, ppd);
                    dc.Pop();

                    // Small tick in the corner makes a multiple selection obvious at a glance.
                    if (selected && !primary)
                    {
                        dc.DrawRectangle(GlyphDraw.Accent, null, new Rect(rect.Right - 8, rect.Top, 8, 8));
                        dc.DrawLine(TickPen, new Point(rect.Right - 6.2, rect.Top + 4.2), new Point(rect.Right - 4.8, rect.Top + 5.5));
                        dc.DrawLine(TickPen, new Point(rect.Right - 4.8, rect.Top + 5.5), new Point(rect.Right - 2.2, rect.Top + 2.4));
                    }
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
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

            if (e.ChangedButton == MouseButton.Right)
            {
                if (i < 0) return;
                if (_selected.Contains(_codes[i]))
                {
                    _sel = i;
                    _anchor = i;
                    EnsureVisible();
                    InvalidateVisual();
                    RaiseSelection();
                }
                else SelectOnly(i);
                return;
            }

            if (e.ChangedButton != MouseButton.Left || i < 0) return;

            if (e.ClickCount == 2)
            {
                SelectOnly(i);
                if (Activated != null) Activated(_codes[i]);
                return;
            }

            if (shift) SelectRange(i, ctrl);
            else if (ctrl) Toggle(i);
            else SelectOnly(i);
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

            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            int page = Math.Max(1, (int)(ActualHeight / _cell) - 1) * _cols;
            int cur = _sel < 0 ? 0 : _sel;
            int target = -1;

            switch (e.Key)
            {
                case Key.Left: target = cur - 1; break;
                case Key.Right: target = cur + 1; break;
                case Key.Up: target = cur - _cols; break;
                case Key.Down: target = cur + _cols; break;
                case Key.Home: target = 0; break;
                case Key.End: target = _codes.Count - 1; break;
                case Key.PageUp: target = Math.Max(0, cur - page); break;
                case Key.PageDown: target = Math.Min(_codes.Count - 1, cur + page); break;
                case Key.A:
                    if (ctrl) { SelectAll(); e.Handled = true; return; }
                    return;
                case Key.Space:
                    if (ctrl) { Toggle(cur); e.Handled = true; return; }
                    return;
                case Key.Escape:
                    CollapseToPrimary();
                    e.Handled = true;
                    return;
                case Key.Enter:
                    if (Activated != null && _sel >= 0) Activated(_codes[_sel]);
                    e.Handled = true;
                    return;
                case Key.C:
                    if (ctrl)
                    {
                        if (CopyRequested != null) CopyRequested();
                        e.Handled = true;
                    }
                    return;
                default: return;
            }

            if (target < 0 || target >= _codes.Count) return;
            if (shift) SelectRange(target, ctrl);
            else SelectOnly(target);
            e.Handled = true;
        }
    }
}
