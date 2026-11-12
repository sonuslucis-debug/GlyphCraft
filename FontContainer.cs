using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace GlyphCraft.Core
{
    public sealed class FontDecodeException : Exception
    {
        public FontDecodeException(string message) : base(message) { }
    }

    /// <summary>
    /// Turns any supported font file (TTF, OTF, TTC/OTC, WOFF, WOFF2, EOT) into
    /// plain sfnt (TrueType/OpenType) byte arrays that Windows can load.
    /// </summary>
    public static class FontContainer
    {
        public static List<byte[]> ExtractFaces(byte[] data)
        {
            if (data == null || data.Length < 12)
                throw new FontDecodeException("The file is too small to be a font.");

            uint tag = ReadU32(data, 0);
            if (tag == 0x00010000 || tag == 0x4F54544F || tag == 0x74727565)   // 1.0, OTTO, true
                return new List<byte[]> { data };
            if (tag == 0x74746366)                                              // ttcf
                return SplitCollection(data);
            if (tag == 0x774F4646)                                              // wOFF
                return new List<byte[]> { DecodeWoff(data) };
            if (tag == 0x774F4632)                                              // wOF2
                return new List<byte[]> { DecodeWoff2(data) };
            if (data.Length > 36 && data[34] == 0x4C && data[35] == 0x50)       // EOT magic 0x504C (LE)
                return new List<byte[]> { DecodeEot(data) };

            throw new FontDecodeException("Not a supported font format (TTF, OTF, TTC, WOFF, WOFF2 or EOT).");
        }

        // ---------------------------------------------------------------- helpers

        static uint ReadU32(byte[] b, int o)
        {
            if (o < 0 || o + 4 > b.Length) throw new FontDecodeException("The font file is truncated.");
            return (uint)((b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3]);
        }

        static int ReadU16(byte[] b, int o)
        {
            if (o < 0 || o + 2 > b.Length) throw new FontDecodeException("The font file is truncated.");
            return (b[o] << 8) | b[o + 1];
        }

        static void W16(Stream s, int v) { s.WriteByte((byte)(v >> 8)); s.WriteByte((byte)v); }
        static void W32(Stream s, uint v)
        {
            s.WriteByte((byte)(v >> 24)); s.WriteByte((byte)(v >> 16)); s.WriteByte((byte)(v >> 8)); s.WriteByte((byte)v);
        }

        static uint TagOf(string s)
        {
            return (uint)((s[0] << 24) | (s[1] << 16) | (s[2] << 8) | s[3]);
        }

        sealed class Table
        {
            public uint Tag;
            public byte[] Data;
        }

        static uint Checksum(byte[] buf, int off, int len)
        {
            uint sum = 0;
            int end = off + ((len + 3) & ~3);
            for (int i = off; i < end; i += 4)
                sum += (uint)((buf[i] << 24) | (buf[i + 1] << 16) | (buf[i + 2] << 8) | buf[i + 3]);
            return sum;
        }

        static byte[] BuildSfnt(uint flavor, List<Table> tables)
        {
            tables.Sort((a, b) => a.Tag.CompareTo(b.Tag));
            int n = tables.Count;
            int entrySelector = 0;
            while ((1 << (entrySelector + 1)) <= n) entrySelector++;
            int searchRange = (1 << entrySelector) * 16;
            int rangeShift = n * 16 - searchRange;

            int offset = 12 + 16 * n;
            var offsets = new int[n];
            for (int i = 0; i < n; i++)
            {
                offsets[i] = offset;
                offset += (tables[i].Data.Length + 3) & ~3;
            }

            var buf = new byte[offset];
            Put32(buf, 0, flavor);
            Put16(buf, 4, n);
            Put16(buf, 6, searchRange);
            Put16(buf, 8, entrySelector);
            Put16(buf, 10, rangeShift);

            int headIndex = -1;
            for (int i = 0; i < n; i++)
            {
                var t = tables[i];
                Buffer.BlockCopy(t.Data, 0, buf, offsets[i], t.Data.Length);
                if (t.Tag == 0x68656164 && t.Data.Length >= 12)
                {
                    headIndex = i;
                    Put32(buf, offsets[i] + 8, 0);   // checkSumAdjustment zeroed for table checksum
                }
                uint cs = Checksum(buf, offsets[i], t.Data.Length);
                int e = 12 + 16 * i;
                Put32(buf, e, t.Tag);
                Put32(buf, e + 4, cs);
                Put32(buf, e + 8, (uint)offsets[i]);
                Put32(buf, e + 12, (uint)t.Data.Length);
            }

            if (headIndex >= 0)
            {
                uint total = Checksum(buf, 0, buf.Length);
                Put32(buf, offsets[headIndex] + 8, unchecked(0xB1B0AFBAu - total));
            }
            return buf;
        }

        static void Put32(byte[] b, int o, uint v)
        {
            b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v;
        }

        static void Put16(byte[] b, int o, int v)
        {
            b[o] = (byte)(v >> 8); b[o + 1] = (byte)v;
        }

        static byte[] ReadAll(Stream s, int expected)
        {
            using (var ms = new MemoryStream(expected > 0 ? expected : 4096))
            {
                s.CopyTo(ms);
                return ms.ToArray();
            }
        }

        // ---------------------------------------------------------------- TTC / OTC

        static List<byte[]> SplitCollection(byte[] d)
        {
            uint numFonts = ReadU32(d, 8);
            if (numFonts == 0 || numFonts > 1024) throw new FontDecodeException("Invalid font collection.");
            var faces = new List<byte[]>();
            for (int i = 0; i < numFonts; i++)
            {
                int off = (int)ReadU32(d, 12 + 4 * i);
                uint flavor = ReadU32(d, off);
                int numTables = ReadU16(d, off + 4);
                var tables = new List<Table>();
                for (int k = 0; k < numTables; k++)
                {
                    int e = off + 12 + 16 * k;
                    uint tag = ReadU32(d, e);
                    int tOff = (int)ReadU32(d, e + 8);
                    int tLen = (int)ReadU32(d, e + 12);
                    if (tOff < 0 || tLen < 0 || (long)tOff + tLen > d.Length)
                        throw new FontDecodeException("The font collection is truncated.");
                    var t = new byte[tLen];
                    Buffer.BlockCopy(d, tOff, t, 0, tLen);
                    tables.Add(new Table { Tag = tag, Data = t });
                }
                faces.Add(BuildSfnt(flavor, tables));
            }
            return faces;
        }

        // ---------------------------------------------------------------- WOFF 1

        static byte[] DecodeWoff(byte[] d)
        {
            uint flavor = ReadU32(d, 4);
            int numTables = ReadU16(d, 12);
            var tables = new List<Table>();
            for (int i = 0; i < numTables; i++)
            {
                int e = 44 + 20 * i;
                uint tag = ReadU32(d, e);
                int off = (int)ReadU32(d, e + 4);
                int comp = (int)ReadU32(d, e + 8);
                int orig = (int)ReadU32(d, e + 12);
                if (off < 0 || comp < 0 || orig < 0 || (long)off + comp > d.Length)
                    throw new FontDecodeException("The WOFF file is truncated.");
                byte[] t;
                if (comp < orig)
                {
                    using (var ms = new MemoryStream(d, off, comp))
                    using (var z = new ZLibStream(ms, CompressionMode.Decompress))
                        t = ReadAll(z, orig);
                    if (t.Length != orig) throw new FontDecodeException("A WOFF table failed to decompress correctly.");
                }
                else
                {
                    t = new byte[orig];
                    Buffer.BlockCopy(d, off, t, 0, orig);
                }
                tables.Add(new Table { Tag = tag, Data = t });
            }
            return BuildSfnt(flavor, tables);
        }

        // ---------------------------------------------------------------- EOT

        static byte[] DecodeEot(byte[] d)
        {
            uint eotSize = (uint)(d[0] | (d[1] << 8) | (d[2] << 16) | (d[3] << 24));
            uint fontSize = (uint)(d[4] | (d[5] << 8) | (d[6] << 16) | (d[7] << 24));
            uint flags = (uint)(d[12] | (d[13] << 8) | (d[14] << 16) | (d[15] << 24));
            if ((flags & 0x4) != 0)
                throw new FontDecodeException("This EOT file uses MicroType Express compression, which isn't supported.");
            long end = Math.Min((long)eotSize, d.Length);
            long start = end - fontSize;
            if (fontSize == 0 || start < 0) throw new FontDecodeException("The EOT file is damaged.");
            var f = new byte[fontSize];
            Buffer.BlockCopy(d, (int)start, f, 0, (int)fontSize);
            if ((flags & 0x10000000) != 0)
                for (int i = 0; i < f.Length; i++) f[i] ^= 0x50;
            uint tag = ReadU32(f, 0);
            if (tag != 0x00010000 && tag != 0x4F54544F && tag != 0x74727565)
                throw new FontDecodeException("The EOT file doesn't contain a usable font.");
            return f;
        }

        // ---------------------------------------------------------------- WOFF 2

        static readonly string[] KnownTags =
        {
            "cmap","head","hhea","hmtx","maxp","name","OS/2","post","cvt ","fpgm","glyf","loca","prep","CFF ","VORG",
            "EBDT","EBLC","gasp","hdmx","kern","LTSH","PCLT","VDMX","vhea","vmtx","BASE","GDEF","GPOS","GSUB","EBSC",
            "JSTF","MATH","CBDT","CBLC","COLR","CPAL","SVG ","sbix","acnt","avar","bdat","bloc","bsln","cvar","fdsc",
            "feat","fmtx","fvar","gvar","hsty","just","lcar","mort","morx","opbd","prop","trak","Zapf","Silf","Glat",
            "Gloc","Feat","Sill"
        };

        static uint ReadBase128(byte[] d, ref int pos)
        {
            uint acc = 0;
            for (int i = 0; i < 5; i++)
            {
                if (pos >= d.Length) throw new FontDecodeException("The WOFF2 file is truncated.");
                byte b = d[pos++];
                if (i == 0 && b == 0x80) throw new FontDecodeException("Invalid WOFF2 data.");
                if ((acc & 0xFE000000u) != 0) throw new FontDecodeException("Invalid WOFF2 data.");
                acc = (acc << 7) | (uint)(b & 0x7F);
                if ((b & 0x80) == 0) return acc;
            }
            throw new FontDecodeException("Invalid WOFF2 data.");
        }

        sealed class W2Entry
        {
            public uint Tag;
            public uint OrigLength;
            public uint TransformLength;
            public bool Transformed;
            public byte[] Data;
        }

        static byte[] DecodeWoff2(byte[] d)
        {
            uint flavor = ReadU32(d, 4);
            if (flavor == 0x74746366) throw new FontDecodeException("WOFF2 font collections aren't supported.");
            int numTables = ReadU16(d, 12);
            int totalCompressed = (int)ReadU32(d, 20);

            const uint GLYF = 0x676C7966, LOCA = 0x6C6F6361, HMTX = 0x686D7478, HEAD = 0x68656164,
                       HHEA = 0x68686561, MAXP = 0x6D617870;

            int pos = 48;
            var entries = new List<W2Entry>();
            for (int i = 0; i < numTables; i++)
            {
                if (pos >= d.Length) throw new FontDecodeException("The WOFF2 file is truncated.");
                int flags = d[pos++];
                int tagIdx = flags & 0x3F;
                int xform = flags >> 6;
                uint tag;
                if (tagIdx == 63) { tag = ReadU32(d, pos); pos += 4; }
                else tag = TagOf(KnownTags[tagIdx]);
                uint orig = ReadBase128(d, ref pos);
                bool glyfLoca = tag == GLYF || tag == LOCA;
                bool transformed = glyfLoca ? xform == 0 : xform != 0;
                uint tlen = orig;
                if (transformed) tlen = ReadBase128(d, ref pos);
                entries.Add(new W2Entry { Tag = tag, OrigLength = orig, TransformLength = tlen, Transformed = transformed });
            }

            if (pos + totalCompressed > d.Length) throw new FontDecodeException("The WOFF2 file is truncated.");
            byte[] raw;
            using (var ms = new MemoryStream(d, pos, totalCompressed))
            using (var br = new BrotliStream(ms, CompressionMode.Decompress))
                raw = ReadAll(br, 0);

            long cursor = 0;
            foreach (var e in entries)
            {
                if (cursor + e.TransformLength > raw.Length) throw new FontDecodeException("The WOFF2 data is damaged.");
                e.Data = new byte[e.TransformLength];
                Buffer.BlockCopy(raw, (int)cursor, e.Data, 0, (int)e.TransformLength);
                cursor += e.TransformLength;
            }

            W2Entry Find(uint t) { foreach (var e in entries) if (e.Tag == t) return e; return null; }

            int[] xMins = null;
            int numGlyphs = 0;
            var glyf = Find(GLYF);
            var loca = Find(LOCA);
            if (glyf != null && glyf.Transformed)
            {
                if (loca == null) throw new FontDecodeException("The WOFF2 file is missing its loca table.");
                byte[] glyfOut, locaOut;
                ReconstructGlyf(glyf.Data, out glyfOut, out locaOut, out xMins, out numGlyphs);
                glyf.Data = glyfOut; glyf.Transformed = false;
                loca.Data = locaOut; loca.Transformed = false;
                var head = Find(HEAD);
                if (head != null && head.Data.Length >= 52) Put16(head.Data, 50, 1);   // long loca
            }

            var hmtx = Find(HMTX);
            if (hmtx != null && hmtx.Transformed)
            {
                var hhea = Find(HHEA);
                var maxp = Find(MAXP);
                if (hhea == null || maxp == null || xMins == null)
                    throw new FontDecodeException("The WOFF2 hmtx table can't be rebuilt.");
                int numHMetrics = ReadU16(hhea.Data, 34);
                int ng = ReadU16(maxp.Data, 4);
                hmtx.Data = ReconstructHmtx(hmtx.Data, ng, numHMetrics, xMins);
                hmtx.Transformed = false;
            }

            var tables = new List<Table>();
            foreach (var e in entries) tables.Add(new Table { Tag = e.Tag, Data = e.Data });
            return BuildSfnt(flavor, tables);
        }

        sealed class Rd
        {
            public byte[] D; public int P; public int End;
            public Rd(byte[] d, int start, int len) { D = d; P = start; End = start + len; }
            public int U8()
            {
                if (P >= End) throw new FontDecodeException("The WOFF2 glyph data is damaged.");
                return D[P++];
            }
            public int U16() { int a = U8(); return (a << 8) | U8(); }
            public int Read255()
            {
                int code = U8();
                if (code == 253) return U16();
                if (code == 255) return U8() + 253;
                if (code == 254) return U8() + 506;
                return code;
            }
            public byte[] Bytes(int n)
            {
                if (n < 0 || P + n > End) throw new FontDecodeException("The WOFF2 glyph data is damaged.");
                var r = new byte[n];
                Buffer.BlockCopy(D, P, r, 0, n);
                P += n;
                return r;
            }
        }

        static int WithSign(int flag, int v) { return (flag & 1) != 0 ? v : -v; }

        static void ReconstructGlyf(byte[] s, out byte[] glyfOut, out byte[] locaOut, out int[] xMins, out int numGlyphsOut)
        {
            int optionFlags = ReadU16(s, 2);
            int numGlyphs = ReadU16(s, 4);
            int[] sizes = new int[7];
            for (int i = 0; i < 7; i++) sizes[i] = (int)ReadU32(s, 8 + 4 * i);
            int p = 36;
            var nContour = new Rd(s, p, sizes[0]); p += sizes[0];
            var nPoints = new Rd(s, p, sizes[1]); p += sizes[1];
            var flagS = new Rd(s, p, sizes[2]); p += sizes[2];
            var glyphS = new Rd(s, p, sizes[3]); p += sizes[3];
            var compS = new Rd(s, p, sizes[4]); p += sizes[4];
            var bboxS = new Rd(s, p, sizes[5]); p += sizes[5];
            var instrS = new Rd(s, p, sizes[6]); p += sizes[6];
            byte[] overlap = null;
            if ((optionFlags & 1) != 0) overlap = new Rd(s, p, (numGlyphs + 7) / 8).Bytes((numGlyphs + 7) / 8);

            int bitmapLen = ((numGlyphs + 31) >> 5) << 2;
            byte[] bitmap = bboxS.Bytes(bitmapLen);

            var glyf = new MemoryStream();
            var offsets = new uint[numGlyphs + 1];
            xMins = new int[numGlyphs];

            for (int g = 0; g < numGlyphs; g++)
            {
                offsets[g] = (uint)glyf.Length;
                int nC = (short)nContour.U16();
                bool hasBbox = (bitmap[g >> 3] & (0x80 >> (g & 7))) != 0;
                if (nC == 0)
                {
                    if (hasBbox) throw new FontDecodeException("The WOFF2 glyph data is damaged.");
                    continue;
                }

                if (nC > 0)
                {
                    var endPts = new int[nC];
                    int total = 0;
                    for (int c = 0; c < nC; c++) { total += nPoints.Read255(); endPts[c] = total - 1; }
                    var xs = new int[total]; var ys = new int[total]; var on = new bool[total];
                    int x = 0, y = 0;
                    for (int k = 0; k < total; k++)
                    {
                        int f = flagS.U8();
                        on[k] = (f & 0x80) == 0;
                        f &= 0x7F;
                        int dx, dy;
                        if (f < 10)
                        {
                            int b0 = glyphS.U8();
                            dx = 0; dy = WithSign(f, ((f & 14) << 7) + b0);
                        }
                        else if (f < 20)
                        {
                            int b0 = glyphS.U8();
                            dx = WithSign(f, (((f - 10) & 14) << 7) + b0); dy = 0;
                        }
                        else if (f < 84)
                        {
                            int b0 = f - 20; int b1 = glyphS.U8();
                            dx = WithSign(f, 1 + (b0 & 0x30) + (b1 >> 4));
                            dy = WithSign(f >> 1, 1 + ((b0 & 0x0C) << 2) + (b1 & 0x0F));
                        }
                        else if (f < 120)
                        {
                            int b0 = f - 84; int b1 = glyphS.U8(); int b2 = glyphS.U8();
                            dx = WithSign(f, 1 + ((b0 / 12) << 8) + b1);
                            dy = WithSign(f >> 1, 1 + (((b0 % 12) >> 2) << 8) + b2);
                        }
                        else if (f < 124)
                        {
                            int b1 = glyphS.U8(); int b2 = glyphS.U8(); int b3 = glyphS.U8();
                            dx = WithSign(f, (b1 << 4) + (b2 >> 4));
                            dy = WithSign(f >> 1, ((b2 & 0x0F) << 8) + b3);
                        }
                        else
                        {
                            int b1 = glyphS.U8(); int b2 = glyphS.U8(); int b3 = glyphS.U8(); int b4 = glyphS.U8();
                            dx = WithSign(f, (b1 << 8) + b2);
                            dy = WithSign(f >> 1, (b3 << 8) + b4);
                        }
                        x += dx; y += dy;
                        xs[k] = x; ys[k] = y;
                    }
                    int instrLen = glyphS.Read255();
                    byte[] instr = instrS.Bytes(instrLen);

                    int xMin, yMin, xMax, yMax;
                    if (hasBbox)
                    {
                        xMin = (short)bboxS.U16(); yMin = (short)bboxS.U16(); xMax = (short)bboxS.U16(); yMax = (short)bboxS.U16();
                    }
                    else
                    {
                        xMin = xMax = xs[0]; yMin = yMax = ys[0];
                        for (int k = 1; k < total; k++)
                        {
                            if (xs[k] < xMin) xMin = xs[k]; if (xs[k] > xMax) xMax = xs[k];
                            if (ys[k] < yMin) yMin = ys[k]; if (ys[k] > yMax) yMax = ys[k];
                        }
                    }
                    xMins[g] = xMin;

                    W16(glyf, nC); W16(glyf, xMin); W16(glyf, yMin); W16(glyf, xMax); W16(glyf, yMax);
                    for (int c = 0; c < nC; c++) W16(glyf, endPts[c]);
                    W16(glyf, instrLen);
                    glyf.Write(instr, 0, instr.Length);
                    bool ov = overlap != null && (overlap[g >> 3] & (0x80 >> (g & 7))) != 0;
                    for (int k = 0; k < total; k++)
                    {
                        int fl = on[k] ? 1 : 0;
                        if (k == 0 && ov) fl |= 0x40;
                        glyf.WriteByte((byte)fl);
                    }
                    int px = 0;
                    for (int k = 0; k < total; k++) { W16(glyf, xs[k] - px); px = xs[k]; }
                    int py = 0;
                    for (int k = 0; k < total; k++) { W16(glyf, ys[k] - py); py = ys[k]; }
                }
                else
                {
                    // composite glyph
                    var comp = new MemoryStream();
                    bool haveInstr = false;
                    int cf;
                    do
                    {
                        cf = compS.U16();
                        int gi = compS.U16();
                        W16(comp, cf); W16(comp, gi);
                        int argBytes = (cf & 1) != 0 ? 4 : 2;
                        int scaleBytes = (cf & 0x08) != 0 ? 2 : (cf & 0x40) != 0 ? 4 : (cf & 0x80) != 0 ? 8 : 0;
                        var extra = compS.Bytes(argBytes + scaleBytes);
                        comp.Write(extra, 0, extra.Length);
                        if ((cf & 0x100) != 0) haveInstr = true;
                    } while ((cf & 0x20) != 0);

                    int instrLen = 0; byte[] instr = new byte[0];
                    if (haveInstr) { instrLen = glyphS.Read255(); instr = instrS.Bytes(instrLen); }

                    int xMin = (short)bboxS.U16(), yMin = (short)bboxS.U16(), xMax = (short)bboxS.U16(), yMax = (short)bboxS.U16();
                    xMins[g] = xMin;
                    W16(glyf, -1); W16(glyf, xMin); W16(glyf, yMin); W16(glyf, xMax); W16(glyf, yMax);
                    var cb = comp.ToArray();
                    glyf.Write(cb, 0, cb.Length);
                    if (haveInstr) { W16(glyf, instrLen); glyf.Write(instr, 0, instr.Length); }
                }

                while ((glyf.Length & 3) != 0) glyf.WriteByte(0);
            }
            offsets[numGlyphs] = (uint)glyf.Length;

            var loca = new MemoryStream();
            foreach (var o in offsets) W32(loca, o);
            glyfOut = glyf.ToArray();
            locaOut = loca.ToArray();
            numGlyphsOut = numGlyphs;
        }

        static byte[] ReconstructHmtx(byte[] s, int numGlyphs, int numHMetrics, int[] xMins)
        {
            var r = new Rd(s, 0, s.Length);
            int flags = r.U8();
            bool lsbOmitted = (flags & 1) != 0;
            bool monoOmitted = (flags & 2) != 0;
            var adv = new int[numHMetrics];
            for (int i = 0; i < numHMetrics; i++) adv[i] = r.U16();
            var lsb = new int[numGlyphs];
            for (int i = 0; i < numHMetrics; i++)
                lsb[i] = lsbOmitted ? (i < xMins.Length ? xMins[i] : 0) : (short)r.U16();
            for (int i = numHMetrics; i < numGlyphs; i++)
                lsb[i] = monoOmitted ? (i < xMins.Length ? xMins[i] : 0) : (short)r.U16();

            var o = new MemoryStream();
            for (int i = 0; i < numHMetrics; i++) { W16(o, adv[i]); W16(o, lsb[i]); }
            for (int i = numHMetrics; i < numGlyphs; i++) W16(o, lsb[i]);
            return o.ToArray();
        }
    }
}
