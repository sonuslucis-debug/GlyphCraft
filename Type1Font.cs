using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace GlyphCraft.Core
{
    /// <summary>Reader for PostScript Type 1 fonts (.pfb binary and .pfa ASCII).</summary>
    public sealed class Type1Font : IOutlineSource
    {
        public double UnitsPerEm { get; private set; }
        public int GlyphCount { get { return _names.Count; } }

        public string FontName = "", FullName = "", FamilyName = "", Weight = "";
        public double ItalicAngle;
        public double[] BBox = new double[] { 0, -200, 1000, 800 };

        readonly List<string> _names = new List<string>();
        readonly List<byte[]> _charStrings = new List<byte[]>();
        readonly Dictionary<string, int> _index = new Dictionary<string, int>();
        readonly Dictionary<int, byte[]> _subrs = new Dictionary<int, byte[]>();
        readonly Dictionary<int, string> _encoding = new Dictionary<int, string>();
        bool _standardEncoding;
        int _lenIV = 4;

        GlyphPath[] _paths;
        double[] _widths;

        public double[] Widths { get { return _widths; } }

        // ------------------------------------------------------------ detection

        public static bool LooksLikeType1(byte[] d)
        {
            if (d == null || d.Length < 16) return false;
            if (d[0] == 0x80 && d[1] == 1)
                return d.Length > 8 && d[6] == '%' && d[7] == '!';
            return d[0] == '%' && d[1] == '!' &&
                   (Encoding.ASCII.GetString(d, 0, Math.Min(24, d.Length)).Contains("Font"));
        }

        // ------------------------------------------------------------ parsing

        public Type1Font(byte[] data)
        {
            byte[] clear, encrypted;
            Split(data, out clear, out encrypted);
            ParseClear(Encoding.Latin1.GetString(clear));
            byte[] priv = Eexec(encrypted);
            ParsePrivate(priv);
            if (_names.Count == 0) throw new FontDecodeException("No glyph data found in this Type 1 font.");

            _paths = new GlyphPath[_names.Count];
            _widths = new double[_names.Count];
            for (int i = 0; i < _names.Count; i++)
            {
                try { Run(i); } catch (Exception) { _paths[i] = new GlyphPath(); }
            }
        }

        static void Split(byte[] d, out byte[] clear, out byte[] encrypted)
        {
            var clearMs = new System.IO.MemoryStream();
            var binMs = new System.IO.MemoryStream();
            if (d[0] == 0x80)
            {
                int p = 0;
                bool seenBinary = false;
                while (p + 6 <= d.Length && d[p] == 0x80)
                {
                    int type = d[p + 1];
                    if (type == 3) break;
                    int len = d[p + 2] | (d[p + 3] << 8) | (d[p + 4] << 16) | (d[p + 5] << 24);
                    p += 6;
                    if (len < 0 || p + len > d.Length) len = Math.Max(0, d.Length - p);
                    if (type == 1 && !seenBinary) clearMs.Write(d, p, len);
                    else if (type == 2) { seenBinary = true; binMs.Write(d, p, len); }
                    p += len;
                }
                clear = clearMs.ToArray();
                encrypted = binMs.ToArray();
            }
            else
            {
                string s = Encoding.Latin1.GetString(d);
                int k = s.IndexOf("eexec", StringComparison.Ordinal);
                if (k < 0) throw new FontDecodeException("This Type 1 font has no encrypted section.");
                int start = k + 5;
                while (start < d.Length && (d[start] == ' ' || d[start] == '\r' || d[start] == '\n' || d[start] == '\t')) start++;
                clear = new byte[k + 5];
                Buffer.BlockCopy(d, 0, clear, 0, k + 5);
                int rest = d.Length - start;
                encrypted = new byte[rest];
                Buffer.BlockCopy(d, start, encrypted, 0, rest);
            }
            // eexec section may be hex-encoded (always in PFA, occasionally in PFB)
            if (encrypted.Length >= 4 && IsHex(encrypted[0]) && IsHex(encrypted[1]) && IsHex(encrypted[2]) && IsHex(encrypted[3]))
            {
                var outp = new List<byte>(encrypted.Length / 2);
                int hi = -1;
                foreach (byte b in encrypted)
                {
                    int v = HexVal(b);
                    if (v < 0) continue;
                    if (hi < 0) hi = v; else { outp.Add((byte)((hi << 4) | v)); hi = -1; }
                }
                encrypted = outp.ToArray();
            }
        }

        static bool IsHex(byte b) { return HexVal(b) >= 0; }
        static int HexVal(byte b)
        {
            if (b >= '0' && b <= '9') return b - '0';
            if (b >= 'a' && b <= 'f') return b - 'a' + 10;
            if (b >= 'A' && b <= 'F') return b - 'A' + 10;
            return -1;
        }

        static byte[] Decrypt(byte[] src, int r, int skip)
        {
            var o = new byte[Math.Max(0, src.Length - skip)];
            for (int i = 0; i < src.Length; i++)
            {
                int c = src[i];
                int plain = c ^ (r >> 8);
                r = ((c + r) * 52845 + 22719) & 0xFFFF;
                if (i >= skip) o[i - skip] = (byte)plain;
            }
            return o;
        }

        static byte[] Eexec(byte[] enc) { return Decrypt(enc, 55665, 4); }

        void ParseClear(string t)
        {
            FontName = Str(t, "/FontName\\s*/(\\S+)");
            FullName = Paren(t, "/FullName");
            FamilyName = Paren(t, "/FamilyName");
            Weight = Paren(t, "/Weight");
            double.TryParse(Str(t, "/ItalicAngle\\s+(-?[\\d.]+)"), NumberStyles.Float, CultureInfo.InvariantCulture, out ItalicAngle);

            var fm = Regex.Match(t, "/FontMatrix\\s*\\[\\s*([-\\d.eE+]+)");
            double m = 0.001;
            if (fm.Success) double.TryParse(fm.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out m);
            UnitsPerEm = m > 0 ? Math.Round(1.0 / m) : 1000;
            if (UnitsPerEm < 16) UnitsPerEm = 1000;

            var bb = Regex.Match(t, "/FontBBox\\s*[\\{\\[]\\s*([-\\d.]+)\\s+([-\\d.]+)\\s+([-\\d.]+)\\s+([-\\d.]+)");
            if (bb.Success)
                for (int i = 0; i < 4; i++)
                    double.TryParse(bb.Groups[i + 1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out BBox[i]);

            int e = t.IndexOf("/Encoding", StringComparison.Ordinal);
            if (e >= 0)
            {
                string tail = t.Substring(e, Math.Min(t.Length - e, 20000));
                if (Regex.IsMatch(tail.Substring(0, Math.Min(tail.Length, 60)), "/Encoding\\s+StandardEncoding")) _standardEncoding = true;
                else
                    foreach (Match mm in Regex.Matches(tail, "dup\\s+(\\d+)\\s*/(\\S+)\\s+put"))
                        _encoding[int.Parse(mm.Groups[1].Value)] = mm.Groups[2].Value;
            }
        }

        static string Str(string t, string pattern)
        {
            var m = Regex.Match(t, pattern);
            return m.Success ? m.Groups[1].Value : "";
        }

        static string Paren(string t, string key)
        {
            int k = t.IndexOf(key, StringComparison.Ordinal);
            if (k < 0) return "";
            int a = t.IndexOf('(', k), b = a < 0 ? -1 : t.IndexOf(')', a);
            return a >= 0 && b > a && a - k < 20 ? t.Substring(a + 1, b - a - 1) : "";
        }

        // ---- tokenizer over the decrypted private section

        sealed class Tok
        {
            public byte[] D; public int P;
            public string Next()
            {
                while (P < D.Length && (D[P] == ' ' || D[P] == '\n' || D[P] == '\r' || D[P] == '\t' || D[P] == 0)) P++;
                if (P >= D.Length) return null;
                int s = P;
                byte c = D[P];
                if (c == '/') { P++; while (P < D.Length && !IsDelim(D[P])) P++; }
                else if (c == '{' || c == '}' || c == '[' || c == ']') P++;
                else if (c == '(') { while (P < D.Length && D[P] != ')') P++; P++; }
                else while (P < D.Length && !IsDelim(D[P]) && D[P] != '/') P++;
                if (P > D.Length) P = D.Length;
                return Encoding.Latin1.GetString(D, s, P - s);
            }
            static bool IsDelim(byte b) { return b == ' ' || b == '\n' || b == '\r' || b == '\t' || b == 0 || b == '{' || b == '}' || b == '[' || b == ']' || b == '(' ; }
            public byte[] Binary(int len)
            {
                P++;                                    // the single separator after RD / -|
                if (len < 0 || P + len > D.Length) throw new FontDecodeException("Damaged Type 1 font data.");
                var r = new byte[len];
                Buffer.BlockCopy(D, P, r, 0, len);
                P += len;
                return r;
            }
        }

        void ParsePrivate(byte[] priv)
        {
            var tk = new Tok { D = priv };
            string t;
            while ((t = tk.Next()) != null)
            {
                if (t == "/lenIV") { int.TryParse(tk.Next(), out _lenIV); }
                else if (t == "/Subrs")
                {
                    tk.Next();                          // count
                    while ((t = tk.Next()) != null)
                    {
                        if (t == "dup")
                        {
                            int idx, len;
                            if (!int.TryParse(tk.Next(), out idx) || !int.TryParse(tk.Next(), out len)) break;
                            tk.Next();                  // RD or -|
                            _subrs[idx] = Dec(tk.Binary(len));
                        }
                        else if (t == "/CharStrings" || t == "/OtherSubrs") { tk.P -= t.Length; break; }
                        else if (t == "ND" || t == "|-" || t == "def") break;
                    }
                }
                else if (t == "/CharStrings")
                {
                    tk.Next();                          // count
                    while ((t = tk.Next()) != null)
                    {
                        if (t.Length > 1 && t[0] == '/')
                        {
                            int len;
                            if (!int.TryParse(tk.Next(), out len)) continue;
                            tk.Next();                  // RD or -|
                            string name = t.Substring(1);
                            if (!_index.ContainsKey(name))
                            {
                                _index[name] = _names.Count;
                                _names.Add(name);
                                _charStrings.Add(Dec(tk.Binary(len)));
                            }
                            else tk.Binary(len);
                        }
                        else if (t == "end") break;
                    }
                    break;
                }
            }
        }

        byte[] Dec(byte[] b)
        {
            if (_lenIV < 0) return b;
            return Decrypt(b, 4330, _lenIV);
        }

        // ------------------------------------------------------------ outlines

        public GlyphPath Get(int glyph)
        {
            if (glyph < 0 || glyph >= _paths.Length) return new GlyphPath();
            return _paths[glyph] ?? new GlyphPath();
        }

        sealed class St
        {
            public double[] S = new double[64];
            public int Sp;
            public List<double> Ps = new List<double>();
            public double X, Y, Sbx, Wx;
            public bool Open, Flex, Done;
            public List<double[]> FlexPts = new List<double[]>();
            public GlyphPath Path = new GlyphPath();
            public double OffX, OffY;
        }

        void Run(int g)
        {
            var s = new St();
            Exec(_charStrings[g], s, 0);
            if (s.Open) s.Path.Close();
            _paths[g] = s.Path;
            _widths[g] = s.Wx / UnitsPerEm;
        }

        void MoveTo(St s)
        {
            if (s.Flex) { s.FlexPts.Add(new[] { s.X, s.Y }); return; }
            if (s.Open) s.Path.Close();
            s.Path.Move(s.X, s.Y);
            s.Open = true;
        }

        void Curve(St s, double dx1, double dy1, double dx2, double dy2, double dx3, double dy3)
        {
            double x1 = s.X + dx1, y1 = s.Y + dy1, x2 = x1 + dx2, y2 = y1 + dy2;
            s.X = x2 + dx3; s.Y = y2 + dy3;
            s.Path.Cubic(x1, y1, x2, y2, s.X, s.Y);
        }

        void Exec(byte[] cs, St s, int depth)
        {
            if (depth > 12) return;
            var st = s.S;
            int p = 0;
            while (p < cs.Length && !s.Done)
            {
                int b = cs[p++];
                if (b >= 32)
                {
                    double v;
                    if (b <= 246) v = b - 139;
                    else if (b <= 250) v = (b - 247) * 256 + cs[p++] + 108;
                    else if (b <= 254) v = -(b - 251) * 256 - cs[p++] - 108;
                    else { v = (cs[p] << 24) | (cs[p + 1] << 16) | (cs[p + 2] << 8) | cs[p + 3]; p += 4; }
                    if (s.Sp < st.Length) st[s.Sp++] = v;
                    continue;
                }
                switch (b)
                {
                    case 1: case 3: s.Sp = 0; break;
                    case 4: s.Y += st[0]; MoveTo(s); s.Sp = 0; break;
                    case 5: s.X += st[0]; s.Y += st[1]; s.Path.Line(s.X, s.Y); s.Sp = 0; break;
                    case 6: s.X += st[0]; s.Path.Line(s.X, s.Y); s.Sp = 0; break;
                    case 7: s.Y += st[0]; s.Path.Line(s.X, s.Y); s.Sp = 0; break;
                    case 8: Curve(s, st[0], st[1], st[2], st[3], st[4], st[5]); s.Sp = 0; break;
                    case 9: if (s.Open) { s.Path.Close(); s.Open = false; } s.Sp = 0; break;
                    case 10:
                        {
                            int idx = (int)st[--s.Sp];
                            byte[] sub;
                            if (_subrs.TryGetValue(idx, out sub)) Exec(sub, s, depth + 1);
                            break;
                        }
                    case 11: return;
                    case 13:                                    // hsbw
                        s.Sbx = st[0]; s.Wx = st[1]; s.X = s.Sbx + s.OffX; s.Y = s.OffY; s.Sp = 0; break;
                    case 14: if (s.Open) { s.Path.Close(); s.Open = false; } s.Done = true; return;
                    case 21: s.X += st[0]; s.Y += st[1]; MoveTo(s); s.Sp = 0; break;
                    case 22: s.X += st[0]; MoveTo(s); s.Sp = 0; break;
                    case 30: Curve(s, 0, st[0], st[1], st[2], st[3], 0); s.Sp = 0; break;
                    case 31: Curve(s, st[0], 0, st[1], st[2], 0, st[3]); s.Sp = 0; break;
                    case 12:
                        {
                            int b1 = cs[p++];
                            switch (b1)
                            {
                                case 0: case 1: case 2: s.Sp = 0; break;
                                case 6: Seac(s, st[0], st[1], st[2], (int)st[3], (int)st[4]); s.Sp = 0; s.Done = true; return;
                                case 7:                          // sbw
                                    s.Sbx = st[0]; s.Wx = st[2]; s.X = st[0] + s.OffX; s.Y = st[1] + s.OffY; s.Sp = 0; break;
                                case 12:                         // div
                                    if (s.Sp >= 2) { double d = st[--s.Sp]; st[s.Sp - 1] = d != 0 ? st[s.Sp - 1] / d : 0; }
                                    break;
                                case 16:                         // callothersubr
                                    {
                                        int osn = (int)st[--s.Sp];
                                        int n = (int)st[--s.Sp];
                                        var args = new double[n];
                                        for (int i = n - 1; i >= 0; i--) args[i] = st[--s.Sp];
                                        if (osn == 1) { s.Flex = true; s.FlexPts.Clear(); }
                                        else if (osn == 2) { /* point already recorded by the rmoveto */ }
                                        else if (osn == 0)
                                        {
                                            s.Flex = false;
                                            var f = s.FlexPts;
                                            if (f.Count >= 7)
                                            {
                                                s.Path.Cubic(f[1][0], f[1][1], f[2][0], f[2][1], f[3][0], f[3][1]);
                                                s.Path.Cubic(f[4][0], f[4][1], f[5][0], f[5][1], f[6][0], f[6][1]);
                                                s.X = f[6][0]; s.Y = f[6][1];
                                            }
                                            s.Ps.Clear();
                                            if (n >= 3) { s.Ps.Add(args[2]); s.Ps.Add(args[1]); }   // pops return x then y
                                        }
                                        else
                                        {
                                            s.Ps.Clear();
                                            for (int i = n - 1; i >= 0; i--) s.Ps.Add(args[i]);
                                        }
                                        break;
                                    }
                                case 17:                         // pop
                                    if (s.Ps.Count > 0)
                                    {
                                        double v = s.Ps[s.Ps.Count - 1];
                                        s.Ps.RemoveAt(s.Ps.Count - 1);
                                        if (s.Sp < st.Length) st[s.Sp++] = v;
                                    }
                                    break;
                                case 33: s.X = st[0]; s.Y = st[1]; s.Sp = 0; break;   // setcurrentpoint
                                default: s.Sp = 0; break;
                            }
                            break;
                        }
                    default: s.Sp = 0; break;
                }
            }
        }

        void Seac(St s, double asb, double adx, double ady, int bchar, int achar)
        {
            string bn = GlyphNames.StandardName(bchar), an = GlyphNames.StandardName(achar);
            int bi, ai;
            if (bn != null && _index.TryGetValue(bn, out bi))
            {
                var bs = new St { OffX = s.OffX, OffY = s.OffY };
                Exec(_charStrings[bi], bs, 1);
                if (bs.Open) bs.Path.Close();
                Append(s.Path, bs.Path, 0, 0);
            }
            if (an != null && _index.TryGetValue(an, out ai))
            {
                var ac = new St();
                Exec(_charStrings[ai], ac, 1);
                if (ac.Open) ac.Path.Close();
                Append(s.Path, ac.Path, s.Sbx - asb + adx, ady);
            }
            if (s.Open) s.Open = false;
        }

        static void Append(GlyphPath dst, GlyphPath src, double dx, double dy)
        {
            int k = 0;
            foreach (PathOp op in src.Ops)
            {
                int pairs = op == PathOp.Move || op == PathOp.Line ? 1 : op == PathOp.Quad ? 2 : op == PathOp.Cubic ? 3 : 0;
                dst.Ops.Add(op);
                for (int i = 0; i < pairs; i++) { dst.Pts.Add(src.Pts[k++] + dx); dst.Pts.Add(src.Pts[k++] + dy); }
            }
        }

        // ------------------------------------------------------------ everything the app needs

        public SfntData ToSfntData()
        {
            var d = new SfntData();
            d.Advances = _widths;

            // character map from glyph names
            var unmapped = new List<int>();
            for (int i = 0; i < _names.Count; i++)
            {
                string n = _names[i];
                if (n == ".notdef") continue;
                int u = GlyphNames.ToUnicode(n);
                if (u > 0 && !d.Map.ContainsKey(u)) d.Map[u] = (ushort)i;
                else unmapped.Add(i);
            }
            // glyphs with no Unicode name: keep them reachable in the private-use area
            var byName = new Dictionary<string, int>();
            foreach (var kv in _encoding) if (!byName.ContainsKey(kv.Value)) byName[kv.Value] = kv.Key;
            int next = 0xE000;
            foreach (int i in unmapped)
            {
                int code;
                if (byName.TryGetValue(_names[i], out code) && !d.Map.ContainsKey(0xF000 + code)) d.Map[0xF000 + code] = (ushort)i;
                else
                {
                    while (d.Map.ContainsKey(next) && next < 0xF8FF) next++;
                    if (next < 0xF8FF) d.Map[next] = (ushort)i;
                }
            }

            d.Family = FamilyName.Length > 0 ? FamilyName : (FullName.Length > 0 ? FullName : FontName);
            string style = Weight.Length > 0 ? Weight : "Regular";
            if (Math.Abs(ItalicAngle) > 0.01 && style.IndexOf("italic", StringComparison.OrdinalIgnoreCase) < 0 &&
                style.IndexOf("oblique", StringComparison.OrdinalIgnoreCase) < 0)
                style = style == "Regular" || style == "Roman" || style == "Medium" ? "Italic" : style + " Italic";
            d.Style = style;

            double upem = UnitsPerEm;
            double asc = Math.Min(Math.Max(BBox[3], 0.7 * upem), 1.1 * upem);
            double desc = Math.Min(Math.Max(-BBox[1], 0.15 * upem), 0.45 * upem);
            d.Baseline = asc / upem;
            d.LineHeight = (asc + desc) / upem + 0.05;
            d.CapsHeight = TopOf("H") / upem;
            d.XHeight = TopOf("x") / upem;
            return d;
        }

        double TopOf(string glyphName)
        {
            int i;
            if (!_index.TryGetValue(glyphName, out i)) return 0;
            var p = _paths[i];
            double max = 0;
            for (int k = 1; k < p.Pts.Count; k += 2) if (p.Pts[k] > max) max = p.Pts[k];
            return max;
        }
    }
}
