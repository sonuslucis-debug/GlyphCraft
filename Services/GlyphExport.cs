using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GlyphCraft.Services
{
    public static class GlyphExport
    {
        public const double Em = 1000;

        interface ISink
        {
            void Move(Point p);
            void Line(Point p);
            void Cubic(Point a, Point b, Point c);
            void Close();
        }

        public static PathGeometry Outline(FontFace face, ushort glyph)
        {
            Geometry g = face.Glyphs.GetGlyphOutline(glyph, Em, Em);
            return PathGeometry.CreateFromGeometry(g);
        }

        static string F(double v)
        {
            return v.ToString("0.###", CultureInfo.InvariantCulture);
        }

        static void Walk(PathGeometry pg, ISink s)
        {
            foreach (PathFigure fig in pg.Figures)
            {
                Point cur = fig.StartPoint;
                s.Move(cur);
                foreach (PathSegment seg in fig.Segments)
                {
                    var line = seg as LineSegment;
                    if (line != null) { s.Line(line.Point); cur = line.Point; continue; }

                    var poly = seg as PolyLineSegment;
                    if (poly != null)
                    {
                        foreach (Point p in poly.Points) { s.Line(p); cur = p; }
                        continue;
                    }

                    var bez = seg as BezierSegment;
                    if (bez != null) { s.Cubic(bez.Point1, bez.Point2, bez.Point3); cur = bez.Point3; continue; }

                    var pbez = seg as PolyBezierSegment;
                    if (pbez != null)
                    {
                        for (int i = 0; i + 2 < pbez.Points.Count; i += 3)
                        {
                            s.Cubic(pbez.Points[i], pbez.Points[i + 1], pbez.Points[i + 2]);
                            cur = pbez.Points[i + 2];
                        }
                        continue;
                    }

                    var quad = seg as QuadraticBezierSegment;
                    if (quad != null) { Quad(s, cur, quad.Point1, quad.Point2); cur = quad.Point2; continue; }

                    var pquad = seg as PolyQuadraticBezierSegment;
                    if (pquad != null)
                    {
                        for (int i = 0; i + 1 < pquad.Points.Count; i += 2)
                        {
                            Quad(s, cur, pquad.Points[i], pquad.Points[i + 1]);
                            cur = pquad.Points[i + 1];
                        }
                        continue;
                    }

                    var arc = seg as ArcSegment;      // not expected in font outlines
                    if (arc != null) { s.Line(arc.Point); cur = arc.Point; }
                }
                if (fig.IsClosed) s.Close();
            }
        }

        static void Quad(ISink s, Point p0, Point c, Point p2)
        {
            var c1 = new Point(p0.X + 2.0 / 3.0 * (c.X - p0.X), p0.Y + 2.0 / 3.0 * (c.Y - p0.Y));
            var c2 = new Point(p2.X + 2.0 / 3.0 * (c.X - p2.X), p2.Y + 2.0 / 3.0 * (c.Y - p2.Y));
            s.Cubic(c1, c2, p2);
        }

        // ------------------------------------------------------------ EPS

        sealed class EpsSink : ISink
        {
            public StringBuilder Sb = new StringBuilder();
            public double OffX, MaxY;
            string P(Point p) { return F(p.X - OffX) + " " + F(MaxY - p.Y); }
            public void Move(Point p) { Sb.Append(P(p)).Append(" m\n"); }
            public void Line(Point p) { Sb.Append(P(p)).Append(" l\n"); }
            public void Cubic(Point a, Point b, Point c) { Sb.Append(P(a)).Append(' ').Append(P(b)).Append(' ').Append(P(c)).Append(" c\n"); }
            public void Close() { Sb.Append("h\n"); }
        }

        public static string BuildEps(PathGeometry pg, string title)
        {
            Rect b = pg.Bounds;
            var sink = new EpsSink { OffX = b.Left, MaxY = b.Bottom };
            Walk(pg, sink);

            double w = b.Width, h = b.Height;
            var sb = new StringBuilder();
            sb.Append("%!PS-Adobe-3.0 EPSF-3.0\n");
            sb.Append("%%Creator: GlyphCraft by Sonus Lucis (sonuslucis.com)\n");
            sb.Append("%%Title: ").Append(Ascii(title)).Append('\n');
            sb.Append("%%CreationDate: ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("%%BoundingBox: 0 0 ").Append(Math.Ceiling(w).ToString(CultureInfo.InvariantCulture)).Append(' ')
              .Append(Math.Ceiling(h).ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("%%HiResBoundingBox: 0 0 ").Append(F(w)).Append(' ').Append(F(h)).Append('\n');
            sb.Append("%%LanguageLevel: 2\n");
            sb.Append("%%Pages: 1\n");
            sb.Append("%%EndComments\n");
            sb.Append("%%BeginProlog\n");
            sb.Append("/m {moveto} bind def\n/l {lineto} bind def\n/c {curveto} bind def\n/h {closepath} bind def\n");
            sb.Append("%%EndProlog\n");
            sb.Append("%%Page: 1 1\n");
            sb.Append("gsave\n");
            sb.Append("0 0 0 1 setcmykcolor\n");
            sb.Append("newpath\n");
            sb.Append(sink.Sb);
            sb.Append(pg.FillRule == FillRule.EvenOdd ? "eofill\n" : "fill\n");
            sb.Append("grestore\n");
            sb.Append("showpage\n");
            sb.Append("%%EOF\n");
            return sb.ToString();
        }

        static string Ascii(string s)
        {
            var sb = new StringBuilder();
            foreach (char ch in s) sb.Append(ch >= 32 && ch < 127 ? ch : '?');
            return sb.ToString();
        }

        // ------------------------------------------------------------ SVG

        sealed class SvgSink : ISink
        {
            public StringBuilder Sb = new StringBuilder();
            public double OffX, OffY;
            string P(Point p) { return F(p.X - OffX) + " " + F(p.Y - OffY); }
            public void Move(Point p) { Sb.Append('M').Append(P(p)); }
            public void Line(Point p) { Sb.Append('L').Append(P(p)); }
            public void Cubic(Point a, Point b, Point c) { Sb.Append('C').Append(P(a)).Append(' ').Append(P(b)).Append(' ').Append(P(c)); }
            public void Close() { Sb.Append('Z'); }
        }

        public static string BuildSvg(PathGeometry pg, string title)
        {
            Rect b = pg.Bounds;
            var sink = new SvgSink { OffX = b.Left, OffY = b.Top };
            Walk(pg, sink);
            string w = F(b.Width), h = F(b.Height);
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
            sb.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"").Append(w).Append("\" height=\"").Append(h)
              .Append("\" viewBox=\"0 0 ").Append(w).Append(' ').Append(h).Append("\">\n");
            sb.Append("  <title>").Append(System.Security.SecurityElement.Escape(title)).Append("</title>\n");
            sb.Append("  <path fill=\"#000000\" fill-rule=\"")
              .Append(pg.FillRule == FillRule.EvenOdd ? "evenodd" : "nonzero").Append("\" d=\"").Append(sink.Sb).Append("\"/>\n");
            sb.Append("</svg>\n");
            return sb.ToString();
        }

        // ------------------------------------------------------------ image for the clipboard

        public static BitmapSource RenderBitmap(PathGeometry pg, double maxSide, int pad)
        {
            Rect b = pg.Bounds;
            double scale = maxSide / Math.Max(b.Width, b.Height);
            int w = (int)Math.Ceiling(b.Width * scale) + 2 * pad;
            int h = (int)Math.Ceiling(b.Height * scale) + 2 * pad;
            var dv = new DrawingVisual();
            using (DrawingContext dc = dv.RenderOpen())
            {
                dc.PushTransform(new MatrixTransform(scale, 0, 0, scale, pad - b.Left * scale, pad - b.Top * scale));
                dc.DrawGeometry(Brushes.Black, null, pg);
                dc.Pop();
            }
            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            return rtb;
        }

        public static MemoryStream ToPng(BitmapSource bmp)
        {
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            var ms = new MemoryStream();
            enc.Save(ms);
            ms.Position = 0;
            return ms;
        }
    }
}
