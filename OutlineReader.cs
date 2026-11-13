using System;
using System.Collections.Generic;

namespace GlyphCraft.Core
{
    public enum PathOp : byte { Move, Line, Quad, Cubic, Close }

    /// <summary>A glyph outline in font units, y pointing up. Pts holds x,y pairs consumed by the ops in order.</summary>
    public sealed class GlyphPath
    {
        public List<PathOp> Ops = new List<PathOp>();
        public List<double> Pts = new List<double>();

        public void Move(double x, double y) { Ops.Add(PathOp.Move); Pts.Add(x); Pts.Add(y); }
        public void Line(double x, double y) { Ops.Add(PathOp.Line); Pts.Add(x); Pts.Add(y); }
        public void Quad(double cx, double cy, double x, double y) { Ops.Add(PathOp.Quad); Pts.Add(cx); Pts.Add(cy); Pts.Add(x); Pts.Add(y); }
        public void Cubic(double x1, double y1, double x2, double y2, double x, double y)
        { Ops.Add(PathOp.Cubic); Pts.Add(x1); Pts.Add(y1); Pts.Add(x2); Pts.Add(y2); Pts.Add(x); Pts.Add(y); }
        public void Close() { Ops.Add(PathOp.Close); }
    }

    /// <summary>
    /// Reads glyph outlines straight from TrueType (glyf) and OpenType/CFF fonts, so GlyphCraft
    /// doesn't depend on Windows' font loader for fonts opened from files.
    /// </summary>
    public sealed class OutlineReader : IOutlineSource
    {
        readonly byte[] _d;
        readonly Dictionary<string, int[]> _tables = new Dictionary<string, int[]>();

        public double UnitsPerEm { get; private set; }
        public int GlyphCount { get; private set; }
        readonly bool _isCff;

        // TrueType
        int _locaOff, _locaLen, _glyfOff, _glyfLen;
        bool _longLoca;

        // CFF
        int _cffOff;
        int[] _charStrings;        // absolute start offsets, count+1
        int[] _gsubrs;
        int[][] _fdSubrs;          // per FD local subrs (or null)
        byte[] _fdSelect;          // glyph -> fd (null if single)

        public OutlineReader(byte[] sfnt)
        {
            UnitsPerEm = 1000;
            _d = sfnt;
            int n = U16(4);
            for (int i = 0; i < n; i++)
            {
                int e = 12 + 16 * i;
                string tag = "" + (char)_d[e] + (char)_d[e + 1] + (char)_d[e + 2] + (char)_d[e + 3];
                _tables[tag] = new[] { (int)U32(e + 8), (int)U32(e + 12) };
            }
            int[] t;
            if (_tables.TryGetValue("head", out t) && t[1] >= 54)
            {
                UnitsPerEm = U16(t[0] + 18);
                if (UnitsPerEm < 16) UnitsPerEm = 1000;
                _longLoca = U16(t[0] + 50) != 0;
            }
            if (_tables.TryGetValue("maxp", out t) && t[1] >= 6) GlyphCount = U16(t[0] + 4);

            if (_tables.ContainsKey("glyf") && _tables.ContainsKey("loca"))
            {
                _glyfOff = _tables["glyf"][0]; _glyfLen = _tables["glyf"][1];
                _locaOff = _tables["loca"][0]; _locaLen = _tables["loca"][1];
            }
            else if (_tables.ContainsKey("CFF "))
            {
                _isCff = true;
                _cffOff = _tables["CFF "][0];
                InitCff();
            }
            else if (_tables.ContainsKey("CFF2"))
                throw new FontDecodeException("CFF2 (variable OpenType) outlines aren't supported yet.");
            else
                throw new FontDecodeException("This font has no outline data GlyphCraft can read (it may be a bitmap font).");
        }

        int U8(int o) { return _d[o]; }
        int U16(int o) { return (_d[o] << 8) | _d[o + 1]; }
        uint U32(int o) { return (uint)((_d[o] << 24) | (_d[o + 1] << 16) | (_d[o + 2] << 8) | _d[o + 3]); }

        public GlyphPath Get(int glyph)
        {
            var p = new GlyphPath();
            if (glyph < 0 || glyph >= GlyphCount) return p;
            try
            {
                if (_isCff) CffGlyph(glyph, p);
                else TtGlyph(glyph, p, 0);
            }
            catch (Exception) { return new GlyphPath(); }
            return p;
        }

        // =================================================================== TrueType

        void TtGlyph(int g, GlyphPath path, int depth)
        {
            if (depth > 8) return;
            int o0, o1;
            if (_longLoca)
            {
                if ((g + 2) * 4 > _locaLen) return;
                o0 = (int)U32(_locaOff + 4 * g); o1 = (int)U32(_locaOff + 4 * g + 4);
            }
            else
            {
                if ((g + 2) * 2 > _locaLen) return;
                o0 = U16(_locaOff + 2 * g) * 2; o1 = U16(_locaOff + 2 * g + 2) * 2;
            }
            if (o1 <= o0 || o0 >= _glyfLen) return;
            int p = _glyfOff + o0;
            int nC = (short)U16(p);
            p += 10;

            if (nC >= 0)
            {
                var endPts = new int[nC];
                for (int i = 0; i < nC; i++) { endPts[i] = U16(p); p += 2; }
                int total = nC == 0 ? 0 : endPts[nC - 1] + 1;
                int instr = U16(p); p += 2 + instr;
                var flags = new int[total];
                for (int i = 0; i < total; i++)
                {
                    int f = U8(p++);
                    flags[i] = f;
                    if ((f & 8) != 0)
                    {
                        int rep = U8(p++);
                        while (rep-- > 0 && i + 1 < total) flags[++i] = f;
                    }
                }
                var xs = new int[total]; var ys = new int[total];
                int v = 0;
                for (int i = 0; i < total; i++)
                {
                    int f = flags[i];
                    if ((f & 2) != 0) { int dx = U8(p++); v += (f & 16) != 0 ? dx : -dx; }
                    else if ((f & 16) == 0) { v += (short)U16(p); p += 2; }
                    xs[i] = v;
                }
                v = 0;
                for (int i = 0; i < total; i++)
                {
                    int f = flags[i];
                    if ((f & 4) != 0) { int dy = U8(p++); v += (f & 32) != 0 ? dy : -dy; }
                    else if ((f & 32) == 0) { v += (short)U16(p); p += 2; }
                    ys[i] = v;
                }
                int start = 0;
                for (int c = 0; c < nC; c++)
                {
                    int end = endPts[c];
                    Contour(path, xs, ys, flags, start, end);
                    start = end + 1;
                }
            }
            else
            {
                int flagsC;
                do
                {
                    flagsC = U16(p); int gi = U16(p + 2); p += 4;
                    double dx = 0, dy = 0;
                    if ((flagsC & 1) != 0)
                    {
                        if ((flagsC & 2) != 0) { dx = (short)U16(p); dy = (short)U16(p + 2); }
                        p += 4;
                    }
                    else
                    {
                        if ((flagsC & 2) != 0) { dx = (sbyte)U8(p); dy = (sbyte)U8(p + 1); }
                        p += 2;
                    }
                    double xx = 1, xy = 0, yx = 0, yy = 1;
                    if ((flagsC & 8) != 0) { xx = yy = (short)U16(p) / 16384.0; p += 2; }
                    else if ((flagsC & 0x40) != 0) { xx = (short)U16(p) / 16384.0; yy = (short)U16(p + 2) / 16384.0; p += 4; }
                    else if ((flagsC & 0x80) != 0)
                    {
                        xx = (short)U16(p) / 16384.0; xy = (short)U16(p + 2) / 16384.0;
                        yx = (short)U16(p + 4) / 16384.0; yy = (short)U16(p + 6) / 16384.0; p += 8;
                    }
                    var sub = new GlyphPath();
                    TtGlyph(gi, sub, depth + 1);
                    int k = 0;
                    foreach (var op in sub.Ops)
                    {
                        int pairs = op == PathOp.Move || op == PathOp.Line ? 1 : op == PathOp.Quad ? 2 : op == PathOp.Cubic ? 3 : 0;
                        path.Ops.Add(op);
                        for (int q = 0; q < pairs; q++)
                        {
                            double x = sub.Pts[k++], y = sub.Pts[k++];
                            path.Pts.Add(xx * x + yx * y + dx);
                            path.Pts.Add(xy * x + yy * y + dy);
                        }
                    }
                } while ((flagsC & 0x20) != 0);
            }
        }

        static void Contour(GlyphPath path, int[] xs, int[] ys, int[] flags, int s, int e)
        {
            int n = e - s + 1;
            if (n < 1) return;
            double[] px = new double[n], py = new double[n]; bool[] on = new bool[n];
            for (int i = 0; i < n; i++) { px[i] = xs[s + i]; py[i] = ys[s + i]; on[i] = (flags[s + i] & 1) != 0; }

            // choose a start on-curve point
            int first = -1;
            for (int i = 0; i < n; i++) if (on[i]) { first = i; break; }
            double sx, sy; int begin, count;
            if (first >= 0) { sx = px[first]; sy = py[first]; begin = first + 1; count = n - 1; }
            else { sx = (px[0] + px[n - 1]) / 2; sy = (py[0] + py[n - 1]) / 2; begin = 0; count = n; }

            path.Move(sx, sy);
            double cx = 0, cy = 0; bool haveOff = false;
            for (int k = 0; k < count; k++)
            {
                int i = (begin + k) % n;
                if (on[i])
                {
                    if (haveOff) { path.Quad(cx, cy, px[i], py[i]); haveOff = false; }
                    else path.Line(px[i], py[i]);
                }
                else
                {
                    if (haveOff) path.Quad(cx, cy, (cx + px[i]) / 2, (cy + py[i]) / 2);
                    cx = px[i]; cy = py[i]; haveOff = true;
                }
            }
            if (haveOff) path.Quad(cx, cy, sx, sy);
            else if (!(Math.Abs(path.Pts[path.Pts.Count - 2] - sx) < 1e-9 && Math.Abs(path.Pts[path.Pts.Count - 1] - sy) < 1e-9))
                path.Line(sx, sy);
            path.Close();
        }

        // =================================================================== CFF

        int[] ReadIndex(int pos, out int end)
        {
            int count = U16(pos);
            if (count == 0) { end = pos + 2; return new int[1] { pos + 2 }; }
            int offSize = U8(pos + 2);
            int baseOff = pos + 3 + (count + 1) * offSize - 1;
            var starts = new int[count + 1];
            for (int i = 0; i <= count; i++)
            {
                int v = 0;
                for (int b = 0; b < offSize; b++) v = (v << 8) | U8(pos + 3 + i * offSize + b);
                starts[i] = baseOff + v;
            }
            end = starts[count];
            return starts;
        }

        Dictionary<int, double[]> ReadDict(int s, int e)
        {
            var dict = new Dictionary<int, double[]>();
            var ops = new List<double>();
            int p = s;
            while (p < e)
            {
                int b0 = U8(p++);
                if (b0 <= 21)
                {
                    int op = b0;
                    if (b0 == 12) op = 1200 + U8(p++);
                    dict[op] = ops.ToArray();
                    ops.Clear();
                }
                else if (b0 == 28) { ops.Add((short)U16(p)); p += 2; }
                else if (b0 == 29) { ops.Add((int)U32(p)); p += 4; }
                else if (b0 == 30)
                {
                    var sb = new System.Text.StringBuilder();
                    bool done = false;
                    while (!done && p < e)
                    {
                        int b = U8(p++);
                        for (int h = 0; h < 2; h++)
                        {
                            int nib = h == 0 ? b >> 4 : b & 15;
                            if (nib <= 9) sb.Append((char)('0' + nib));
                            else if (nib == 10) sb.Append('.');
                            else if (nib == 11) sb.Append('E');
                            else if (nib == 12) sb.Append("E-");
                            else if (nib == 14) sb.Append('-');
                            else if (nib == 15) { done = true; break; }
                        }
                    }
                    double v; double.TryParse(sb.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v);
                    ops.Add(v);
                }
                else if (b0 >= 32 && b0 <= 246) ops.Add(b0 - 139);
                else if (b0 >= 247 && b0 <= 250) ops.Add((b0 - 247) * 256 + U8(p++) + 108);
                else if (b0 >= 251 && b0 <= 254) ops.Add(-(b0 - 251) * 256 - U8(p++) - 108);
            }
            return dict;
        }

        int[] LocalSubrs(Dictionary<int, double[]> priv, int privOff)
        {
            double[] v;
            if (priv.TryGetValue(19, out v) && v.Length > 0)
            {
                int end; return ReadIndex(privOff + (int)v[0], out end);
            }
            return null;
        }

        void InitCff()
        {
            int hdr = U8(_cffOff + 2);
            int end;
            ReadIndex(_cffOff + hdr, out end);                       // Name INDEX
            int[] top = ReadIndex(end, out end);                     // Top DICT INDEX
            ReadIndex(end, out end);                                 // String INDEX
            _gsubrs = ReadIndex(end, out end);                       // Global Subr INDEX

            var topDict = ReadDict(top[0], top[1]);
            double[] v;
            if (!topDict.TryGetValue(17, out v)) throw new FontDecodeException("The CFF font has no glyph data.");
            int e2;
            _charStrings = ReadIndex(_cffOff + (int)v[0], out e2);
            if (GlyphCount == 0 || GlyphCount > _charStrings.Length - 1) GlyphCount = _charStrings.Length - 1;

            if (topDict.TryGetValue(1236, out v))                    // FDArray (CID-keyed)
            {
                int[] fds = ReadIndex(_cffOff + (int)v[0], out e2);
                int nfd = fds.Length - 1;
                _fdSubrs = new int[nfd][];
                for (int i = 0; i < nfd; i++)
                {
                    var fd = ReadDict(fds[i], fds[i + 1]);
                    double[] pv;
                    if (fd.TryGetValue(18, out pv) && pv.Length >= 2)
                    {
                        int po = _cffOff + (int)pv[1];
                        var pd = ReadDict(po, po + (int)pv[0]);
                        _fdSubrs[i] = LocalSubrs(pd, po);
                    }
                }
                if (topDict.TryGetValue(1237, out v))
                {
                    int fs = _cffOff + (int)v[0];
                    _fdSelect = new byte[GlyphCount];
                    int fmt = U8(fs);
                    if (fmt == 0) { for (int i = 0; i < GlyphCount; i++) _fdSelect[i] = (byte)U8(fs + 1 + i); }
                    else if (fmt == 3)
                    {
                        int nr = U16(fs + 1);
                        for (int r = 0; r < nr; r++)
                        {
                            int first = U16(fs + 3 + 3 * r), fd = U8(fs + 5 + 3 * r);
                            int next = U16(fs + 3 + 3 * (r + 1));
                            for (int g = first; g < next && g < GlyphCount; g++) _fdSelect[g] = (byte)fd;
                        }
                    }
                }
            }
            else if (topDict.TryGetValue(18, out v) && v.Length >= 2)
            {
                int po = _cffOff + (int)v[1];
                var pd = ReadDict(po, po + (int)v[0]);
                _fdSubrs = new int[1][];
                _fdSubrs[0] = LocalSubrs(pd, po);
            }
        }

        sealed class CsState
        {
            public double[] St = new double[96];
            public int Sp;
            public double X, Y;
            public bool Open, WidthDone, Done;
            public int Stems;
            public GlyphPath Path;
            public int[] Local;
        }

        void CffGlyph(int g, GlyphPath path)
        {
            var s = new CsState { Path = path };
            if (_fdSubrs != null)
                s.Local = _fdSubrs[_fdSelect != null && g < _fdSelect.Length ? Math.Min(_fdSelect[g], _fdSubrs.Length - 1) : 0];
            Run(_charStrings[g], _charStrings[g + 1], s, 0);
            if (s.Open) path.Close();
        }

        static int Bias(int count) { return count < 1240 ? 107 : count < 33900 ? 1131 : 32768; }

        void MoveTo(CsState s, double dx, double dy)
        {
            if (s.Open) s.Path.Close();
            s.X += dx; s.Y += dy;
            s.Path.Move(s.X, s.Y);
            s.Open = true;
        }

        void LineTo(CsState s, double dx, double dy)
        {
            s.X += dx; s.Y += dy;
            s.Path.Line(s.X, s.Y);
        }

        void CurveTo(CsState s, double dx1, double dy1, double dx2, double dy2, double dx3, double dy3)
        {
            double x1 = s.X + dx1, y1 = s.Y + dy1;
            double x2 = x1 + dx2, y2 = y1 + dy2;
            s.X = x2 + dx3; s.Y = y2 + dy3;
            s.Path.Cubic(x1, y1, x2, y2, s.X, s.Y);
        }

        // Returns the index of the first real argument (skips the optional width).
        int Arg0(CsState s, bool hasWidth)
        {
            if (!s.WidthDone) { s.WidthDone = true; if (hasWidth) return 1; }
            return 0;
        }

        void Run(int p, int end, CsState s, int depth)
        {
            if (depth > 12) return;
            var st = s.St;
            while (p < end && !s.Done)
            {
                int b0 = U8(p++);
                if (b0 >= 32 || b0 == 28)
                {
                    double v;
                    if (b0 == 28) { v = (short)U16(p); p += 2; }
                    else if (b0 <= 246) v = b0 - 139;
                    else if (b0 <= 250) v = (b0 - 247) * 256 + U8(p++) + 108;
                    else if (b0 <= 254) v = -(b0 - 251) * 256 - U8(p++) - 108;
                    else { v = (int)U32(p) / 65536.0; p += 4; }
                    if (s.Sp < st.Length) st[s.Sp++] = v;
                    continue;
                }

                int a, i;
                switch (b0)
                {
                    case 1: case 3: case 18: case 23:          // stems
                        a = Arg0(s, s.Sp % 2 == 1);
                        s.Stems += (s.Sp - a) / 2; s.Sp = 0; break;
                    case 19: case 20:                           // hintmask / cntrmask
                        a = Arg0(s, s.Sp % 2 == 1);
                        s.Stems += (s.Sp - a) / 2; s.Sp = 0;
                        p += (s.Stems + 7) >> 3; break;
                    case 21:                                    // rmoveto
                        a = Arg0(s, s.Sp > 2);
                        MoveTo(s, st[a], st[a + 1]); s.Sp = 0; break;
                    case 22:                                    // hmoveto
                        a = Arg0(s, s.Sp > 1);
                        MoveTo(s, st[a], 0); s.Sp = 0; break;
                    case 4:                                     // vmoveto
                        a = Arg0(s, s.Sp > 1);
                        MoveTo(s, 0, st[a]); s.Sp = 0; break;
                    case 5:
                        for (i = 0; i + 1 < s.Sp; i += 2) LineTo(s, st[i], st[i + 1]);
                        s.Sp = 0; break;
                    case 6: case 7:
                        {
                            bool horiz = b0 == 6;
                            for (i = 0; i < s.Sp; i++) { if (horiz) LineTo(s, st[i], 0); else LineTo(s, 0, st[i]); horiz = !horiz; }
                            s.Sp = 0; break;
                        }
                    case 8:
                        for (i = 0; i + 5 < s.Sp; i += 6) CurveTo(s, st[i], st[i + 1], st[i + 2], st[i + 3], st[i + 4], st[i + 5]);
                        s.Sp = 0; break;
                    case 24:                                    // rcurveline
                        for (i = 0; i + 7 < s.Sp; i += 6) CurveTo(s, st[i], st[i + 1], st[i + 2], st[i + 3], st[i + 4], st[i + 5]);
                        if (i + 1 < s.Sp) LineTo(s, st[i], st[i + 1]);
                        s.Sp = 0; break;
                    case 25:                                    // rlinecurve
                        for (i = 0; i + 7 < s.Sp; i += 2) LineTo(s, st[i], st[i + 1]);
                        if (i + 5 < s.Sp) CurveTo(s, st[i], st[i + 1], st[i + 2], st[i + 3], st[i + 4], st[i + 5]);
                        s.Sp = 0; break;
                    case 26:                                    // vvcurveto
                        {
                            i = 0; double dx1 = 0;
                            if (s.Sp % 2 == 1) { dx1 = st[0]; i = 1; }
                            for (; i + 3 < s.Sp; i += 4) { CurveTo(s, dx1, st[i], st[i + 1], st[i + 2], 0, st[i + 3]); dx1 = 0; }
                            s.Sp = 0; break;
                        }
                    case 27:                                    // hhcurveto
                        {
                            i = 0; double dy1 = 0;
                            if (s.Sp % 2 == 1) { dy1 = st[0]; i = 1; }
                            for (; i + 3 < s.Sp; i += 4) { CurveTo(s, st[i], dy1, st[i + 1], st[i + 2], st[i + 3], 0); dy1 = 0; }
                            s.Sp = 0; break;
                        }
                    case 30: case 31:                           // vhcurveto / hvcurveto
                        {
                            bool horiz = b0 == 31;
                            for (i = 0; i + 3 < s.Sp; i += 4)
                            {
                                bool last = s.Sp - i == 5;
                                double extra = last ? st[i + 4] : 0;
                                if (horiz) CurveTo(s, st[i], 0, st[i + 1], st[i + 2], extra, st[i + 3]);
                                else CurveTo(s, 0, st[i], st[i + 1], st[i + 2], st[i + 3], extra);
                                horiz = !horiz;
                                if (last) break;
                            }
                            s.Sp = 0; break;
                        }
                    case 10: case 29:                           // callsubr / callgsubr
                        {
                            if (s.Sp < 1) break;
                            int[] subrs = b0 == 10 ? s.Local : _gsubrs;
                            int idx = (int)st[--s.Sp];
                            if (subrs != null)
                            {
                                idx += Bias(subrs.Length - 1);
                                if (idx >= 0 && idx < subrs.Length - 1) Run(subrs[idx], subrs[idx + 1], s, depth + 1);
                            }
                            break;
                        }
                    case 11: return;
                    case 14:
                        if (s.Open) { s.Path.Close(); s.Open = false; }
                        s.Done = true; s.Sp = 0; return;
                    case 12:
                        {
                            int b1 = U8(p++);
                            if (b1 == 34 && s.Sp >= 7)          // hflex
                            {
                                CurveTo(s, st[0], 0, st[1], st[2], st[3], 0);
                                CurveTo(s, st[4], 0, st[5], -st[2], st[6], 0);
                            }
                            else if (b1 == 35 && s.Sp >= 12)    // flex
                            {
                                CurveTo(s, st[0], st[1], st[2], st[3], st[4], st[5]);
                                CurveTo(s, st[6], st[7], st[8], st[9], st[10], st[11]);
                            }
                            else if (b1 == 36 && s.Sp >= 9)     // hflex1
                            {
                                CurveTo(s, st[0], st[1], st[2], st[3], st[4], 0);
                                CurveTo(s, st[5], 0, st[6], st[7], st[8], -(st[1] + st[3] + st[7]));
                            }
                            else if (b1 == 37 && s.Sp >= 11)    // flex1
                            {
                                double dx = st[0] + st[2] + st[4] + st[6] + st[8];
                                double dy = st[1] + st[3] + st[5] + st[7] + st[9];
                                CurveTo(s, st[0], st[1], st[2], st[3], st[4], st[5]);
                                if (Math.Abs(dx) > Math.Abs(dy)) CurveTo(s, st[6], st[7], st[8], st[9], st[10], -dy);
                                else CurveTo(s, st[6], st[7], st[8], st[9], -dx, st[10]);
                            }
                            s.Sp = 0; break;
                        }
                    default:
                        s.Sp = 0; break;
                }
            }
        }
    }
}
