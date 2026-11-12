using System;
using System.Windows;
using System.Windows.Media;
using GlyphCraft.Services;

namespace GlyphCraft.Controls
{
    /// <summary>Large single-glyph preview with optional typographic guides.</summary>
    public sealed class GlyphPreview : FrameworkElement
    {
        FontFace _face;
        int _code = -1;
        bool _guides = true;

        static readonly Pen Border = GlyphDraw.MakePen(GlyphDraw.LineBrush, 1);
        static readonly Pen Dash;
        static readonly Brush BoxFill = GlyphDraw.Frozen(Color.FromArgb(0x16, 0x2E, 0x6D, 0xA4));
        static readonly Brush Muted = GlyphDraw.Frozen(Color.FromRgb(0x6B, 0x84, 0x9A));

        static GlyphPreview()
        {
            var p = new Pen(GlyphDraw.Frozen(Color.FromRgb(0x8F, 0xB1, 0xD0)), 1);
            p.DashStyle = DashStyles.Dash;
            p.Freeze();
            Dash = p;
        }

        public GlyphPreview() { ClipToBounds = true; }

        public bool ShowGuides
        {
            get { return _guides; }
            set { _guides = value; InvalidateVisual(); }
        }

        public void Show(FontFace face, int code)
        {
            _face = face;
            _code = code;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            dc.DrawRectangle(GlyphDraw.Panel, Border, new Rect(0.5, 0.5, Math.Max(0, w - 1), Math.Max(0, h - 1)));
            if (_face == null || _code < 0) return;

            GlyphTypeface gt = _face.Glyphs;
            ushort gi;
            if (!gt.CharacterToGlyphMap.TryGetValue(_code, out gi)) return;

            double ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            double lineH = gt.Height > 0.2 ? gt.Height : 1.2;
            double size = h * 0.74 / lineH;
            double adv1 = gt.AdvanceWidths[gi];
            if (adv1 * size > w * 0.8) size = w * 0.8 / adv1;

            double lineBox = lineH * size;
            double top = (h - lineBox) / 2;
            double baseY = top + gt.Baseline * size;
            double adv = adv1 * size;
            double x0 = (w - adv) / 2;

            if (_guides)
            {
                dc.DrawRectangle(BoxFill, null, new Rect(x0, top, adv, lineBox));
                HLine(dc, w, top, "ascender", ppd);
                HLine(dc, w, baseY, "baseline", ppd);
                HLine(dc, w, top + lineBox, "descender", ppd);
                if (gt.CapsHeight > 0.05) HLine(dc, w, baseY - gt.CapsHeight * size, "cap height", ppd);
                if (gt.XHeight > 0.05) HLine(dc, w, baseY - gt.XHeight * size, "x-height", ppd);
                dc.DrawLine(Dash, new Point(x0, 0), new Point(x0, h));
                dc.DrawLine(Dash, new Point(x0 + adv, 0), new Point(x0 + adv, h));
            }

            dc.DrawGlyphRun(GlyphDraw.Navy, GlyphDraw.Single(gt, gi, size, new Point(x0, baseY), ppd));
        }

        static void HLine(DrawingContext dc, double w, double y, string label, double ppd)
        {
            dc.DrawLine(Dash, new Point(0, y), new Point(w, y));
            var ft = GlyphDraw.Label(label, 9, Muted, ppd);
            dc.DrawText(ft, new Point(8, y - ft.Height - 1));
        }
    }
}
