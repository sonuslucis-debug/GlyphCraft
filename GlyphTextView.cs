using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using GlyphCraft.Services;

namespace GlyphCraft.Controls
{
    /// <summary>Draws a line of text using a specific font face (even one loaded from a file), wrapping to the width.</summary>
    public sealed class GlyphTextView : FrameworkElement
    {
        FontFace _face;
        string _text = "";
        double _size = 56;

        sealed class Line
        {
            public List<ushort> Glyphs = new List<ushort>();
            public List<double> Advances = new List<double>();
        }

        public void Set(FontFace face, string text, double size)
        {
            _face = face;
            _text = text ?? "";
            _size = size;
            InvalidateMeasure();
            InvalidateVisual();
        }

        double LineHeight()
        {
            if (_face == null) return _size * 1.2;
            double h = _face.LineHeight;
            return (h > 0.2 ? h : 1.2) * _size;
        }

        List<Line> Layout(double maxW)
        {
            var lines = new List<Line>();
            if (_face == null) return lines;
            var cur = new Line();
            double x = 0;
            foreach (char ch in _text)
            {
                ushort gi; int actual;
                if (!_face.Resolve(ch, out gi, out actual)) continue;
                double adv = _face.Advance(gi) * _size;
                if (ch == ' ' && cur.Glyphs.Count == 0) continue;
                if (cur.Glyphs.Count > 0 && x + adv > maxW)
                {
                    lines.Add(cur);
                    cur = new Line();
                    x = 0;
                    if (ch == ' ') continue;
                }
                cur.Glyphs.Add(gi);
                cur.Advances.Add(adv);
                x += adv;
            }
            if (cur.Glyphs.Count > 0) lines.Add(cur);
            return lines;
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            double w = double.IsInfinity(availableSize.Width) ? 900 : availableSize.Width;
            int n = Math.Max(1, Layout(Math.Max(w, _size)).Count);
            return new Size(w, n * LineHeight());
        }

        protected override void OnRender(DrawingContext dc)
        {
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
            if (_face == null) return;
            double ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            var lines = Layout(Math.Max(ActualWidth, _size));
            double lh = LineHeight();
            double baseline = _face.Baseline * _size;
            if (_face.LineHeight <= 0.2) baseline = _size;
            for (int i = 0; i < lines.Count; i++)
            {
                double y = i * lh + baseline;
                if (_face.Outlines == null)
                {
                    dc.DrawGlyphRun(GlyphDraw.Navy, GlyphDraw.Run(_face.Glyphs, lines[i].Glyphs, lines[i].Advances, _size,
                                                                  new Point(0, y), ppd));
                }
                else
                {
                    double x = 0;
                    for (int k = 0; k < lines[i].Glyphs.Count; k++)
                    {
                        GlyphDraw.DrawGlyph(dc, GlyphDraw.Navy, _face, lines[i].Glyphs[k], _size, new Point(x, y), ppd);
                        x += lines[i].Advances[k];
                    }
                }
            }
        }
    }
}
