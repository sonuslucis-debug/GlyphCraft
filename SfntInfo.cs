using System;
using System.Collections.Generic;
using System.Text;

namespace GlyphCraft.Core
{
    /// <summary>
    /// Small independent reader for the cmap and name tables. Used as a safety net when
    /// Windows' own font API can't supply them for an unusual font.
    /// </summary>
    public static class SfntInfo
    {
        static int U16(byte[] d, int o) { return (d[o] << 8) | d[o + 1]; }
        static uint U32(byte[] d, int o) { return (uint)((d[o] << 24) | (d[o + 1] << 16) | (d[o + 2] << 8) | d[o + 3]); }

        static bool FindTable(byte[] d, string tag, out int offset, out int length)
        {
            offset = 0; length = 0;
            if (d == null || d.Length < 12) return false;
            int n = U16(d, 4);
            uint want = (uint)((tag[0] << 24) | (tag[1] << 16) | (tag[2] << 8) | tag[3]);
            for (int i = 0; i < n; i++)
            {
                int e = 12 + 16 * i;
                if (e + 16 > d.Length) break;
                if (U32(d, e) == want)
                {
                    offset = (int)U32(d, e + 8);
                    length = (int)U32(d, e + 12);
                    return offset >= 0 && offset + length <= d.Length;
                }
            }
            return false;
        }

        // ------------------------------------------------------------ cmap

        public static Dictionary<int, ushort> ReadCmap(byte[] d)
        {
            var map = new Dictionary<int, ushort>();
            try
            {
                int off, len;
                if (!FindTable(d, "cmap", out off, out len) || len < 4) return map;
                int n = U16(d, off + 2);

                int best = -1, bestScore = -1;
                for (int i = 0; i < n; i++)
                {
                    int r = off + 4 + 8 * i;
                    int plat = U16(d, r), enc = U16(d, r + 2);
                    int sub = off + (int)U32(d, r + 4);
                    if (sub + 6 > d.Length) continue;
                    int fmt = U16(d, sub);
                    if (fmt != 0 && fmt != 4 && fmt != 6 && fmt != 12) continue;
                    int score = 0;
                    if (plat == 3 && enc == 10) score = 6;
                    else if (plat == 0 && (enc == 4 || enc == 6)) score = 5;
                    else if (plat == 3 && enc == 1) score = 4;
                    else if (plat == 0) score = 3;
                    else if (plat == 3 && enc == 0) score = 2;
                    else if (plat == 1 && enc == 0) score = 1;
                    if (score > bestScore) { bestScore = score; best = sub; }
                }
                if (best < 0) return map;
                ParseSubtable(d, best, map);
            }
            catch { }
            return map;
        }

        static void Add(Dictionary<int, ushort> map, int cp, int g)
        {
            if (g != 0 && !map.ContainsKey(cp)) map[cp] = (ushort)g;
        }

        static void ParseSubtable(byte[] d, int s, Dictionary<int, ushort> map)
        {
            int fmt = U16(d, s);
            if (fmt == 0)
            {
                for (int c = 0; c < 256; c++) Add(map, c, d[s + 6 + c]);
            }
            else if (fmt == 6)
            {
                int first = U16(d, s + 6), count = U16(d, s + 8);
                for (int i = 0; i < count; i++) Add(map, first + i, U16(d, s + 10 + 2 * i));
            }
            else if (fmt == 4)
            {
                int segX2 = U16(d, s + 6);
                int endP = s + 14, startP = endP + segX2 + 2, deltaP = startP + segX2, rangeP = deltaP + segX2;
                for (int i = 0; i < segX2 / 2; i++)
                {
                    int end = U16(d, endP + 2 * i), start = U16(d, startP + 2 * i);
                    int delta = U16(d, deltaP + 2 * i), range = U16(d, rangeP + 2 * i);
                    if (start == 0xFFFF) continue;
                    for (int c = start; c <= end && c < 0xFFFF; c++)
                    {
                        int g;
                        if (range == 0) g = (c + delta) & 0xFFFF;
                        else
                        {
                            int a = rangeP + 2 * i + range + 2 * (c - start);
                            if (a + 2 > d.Length) continue;
                            g = U16(d, a);
                            if (g != 0) g = (g + delta) & 0xFFFF;
                        }
                        Add(map, c, g);
                    }
                }
            }
            else if (fmt == 12)
            {
                uint groups = U32(d, s + 12);
                for (uint i = 0; i < groups; i++)
                {
                    int p = s + 16 + 12 * (int)i;
                    uint start = U32(d, p), end = U32(d, p + 4), g = U32(d, p + 8);
                    if (end < start || end > 0x10FFFF || end - start > 0x20000) continue;
                    for (uint c = start; c <= end; c++) Add(map, (int)c, (int)(g + (c - start)));
                }
            }
        }

        // ------------------------------------------------------------ names

        public static void ReadNames(byte[] d, out string family, out string style)
        {
            family = ""; style = "";
            try
            {
                int off, len;
                if (!FindTable(d, "name", out off, out len) || len < 6) return;
                int count = U16(d, off + 2), strOff = off + U16(d, off + 4);
                string f1 = null, f16 = null, s2 = null, s17 = null;
                for (int i = 0; i < count; i++)
                {
                    int r = off + 6 + 12 * i;
                    if (r + 12 > d.Length) break;
                    int plat = U16(d, r), lang = U16(d, r + 4), id = U16(d, r + 6);
                    int l = U16(d, r + 8), o = strOff + U16(d, r + 10);
                    if ((id != 1 && id != 2 && id != 16 && id != 17) || o + l > d.Length) continue;
                    bool wide = plat == 3 || plat == 0;
                    if (plat == 3 && lang != 0x409 && !(id == 1 || id == 2 || id == 16 || id == 17)) continue;
                    if (plat == 3 && lang != 0x409) continue;
                    string v = wide ? Encoding.BigEndianUnicode.GetString(d, o, l) : Encoding.Latin1.GetString(d, o, l);
                    if (id == 1 && f1 == null) f1 = v;
                    if (id == 16 && f16 == null) f16 = v;
                    if (id == 2 && s2 == null) s2 = v;
                    if (id == 17 && s17 == null) s17 = v;
                }
                family = f16 ?? f1 ?? "";
                style = s17 ?? s2 ?? "";
            }
            catch { }
        }
    }
}
