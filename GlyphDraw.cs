using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using GlyphCraft.Services;

namespace GlyphCraft.Controls
{
    internal static class GlyphDraw
    {
        public static readonly Brush Navy = Frozen(Color.FromRgb(0x0E, 0x3A, 0x5B));
        public static readonly Brush Accent = Frozen(Color.FromRgb(0x2E, 0x6D, 0xA4));
        public static readonly Brush Tint = Frozen(Color.FromRgb(0xEE, 0xF3, 0xF8));
        public static readonly Brush Panel = Frozen(Color.FromRgb(0xF7, 0xFA, 0xFC));
        public static readonly Brush LineBrush = Frozen(Color.FromRgb(0xD0, 0xDB, 0xE6));

        public static Brush Frozen(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        public static Pen MakePen(Brush b, double thickness)
        {
            var p = new Pen(b, thickness);
            p.Freeze();
            return p;
        }

        public static GlyphRun Run(GlyphTypeface gt, IList<ushort> glyphs, IList<double> advances, double size, Point origin, double ppd)
        {
            return new GlyphRun(gt, 0, false, size, (float)ppd, glyphs, origin, advances,
                                null, null, null, null, null, null);
        }

        public static GlyphRun Single(FontFace face, ushort glyph, double size, Point origin, double ppd)
        {
            return Run(face.Glyphs, new List<ushort> { glyph }, new List<double> { face.Advance(glyph) * size }, size, origin, ppd);
        }

        public static FormattedText Label(string text, double size, Brush brush, double ppd)
        {
            var family = (FontFamily)Application.Current.FindResource("Brand");
            var tf = new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            return new FormattedText(text, System.Globalization.CultureInfo.InvariantCulture,
                                     FlowDirection.LeftToRight, tf, size, brush, ppd);
        }
    }
}
