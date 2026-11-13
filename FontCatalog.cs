using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using GlyphCraft.Core;

namespace GlyphCraft.Services
{
    public sealed class FontFace
    {
        public string Family { get; set; }
        public string Style { get; set; }
        public string Source { get; set; }   // "Installed" or the file name
        public GlyphTypeface Glyphs;
        public bool IsSymbol;
        public List<int> Codes = new List<int>();
        public IDictionary<int, ushort> Map;
        public double Baseline = 0.8, LineHeight = 1.2, CapsHeight, XHeight;
        double[] _adv;

        /// <summary>Installed font: metrics come from Windows.</summary>
        public static FontFace CreateInstalled(string family, string style, GlyphTypeface gt)
        {
            var f = new FontFace { Family = family, Style = style, Source = "Installed", Glyphs = gt };
            f.Map = gt.CharacterToGlyphMap;
            try { f.Baseline = gt.Baseline; f.LineHeight = gt.Height > 0.2 ? gt.Height : 1.2; f.CapsHeight = gt.CapsHeight; f.XHeight = gt.XHeight; }
            catch { }
            f.Finish();
            return f;
        }

        /// <summary>Font from a file: cmap, widths, metrics AND outlines are read from the font bytes.</summary>
        public static FontFace CreateFromFile(string family, string style, string source, SfntData data, IOutlineSource outlines)
        {
            var f = new FontFace { Family = family, Style = style, Source = source };
            f.Map = data.Map;
            f._adv = data.Advances;
            f.Outlines = outlines;
            f.Baseline = data.Baseline; f.LineHeight = data.LineHeight;
            f.CapsHeight = data.CapsHeight; f.XHeight = data.XHeight;
            f.Finish();
            return f;
        }

        public IOutlineSource Outlines;      // set for fonts opened from files
        readonly Dictionary<ushort, PathGeometry> _geo = new Dictionary<ushort, PathGeometry>();

        /// <summary>Glyph outline at 1000 units per em, y down, origin on the baseline.</summary>
        public PathGeometry GetOutline(ushort glyph)
        {
            PathGeometry pg;
            if (_geo.TryGetValue(glyph, out pg)) return pg;
            if (Outlines != null)
            {
                GlyphPath gp = Outlines.Get(glyph);
                double sc = 1000.0 / Outlines.UnitsPerEm;
                pg = new PathGeometry();
                pg.FillRule = FillRule.Nonzero;
                PathFigure fig = null;
                int k = 0;
                foreach (PathOp op in gp.Ops)
                {
                    switch (op)
                    {
                        case PathOp.Move:
                            fig = new PathFigure { StartPoint = Pt(gp, k, sc), IsClosed = false, IsFilled = true };
                            pg.Figures.Add(fig); k += 2; break;
                        case PathOp.Line:
                            if (fig != null) fig.Segments.Add(new LineSegment(Pt(gp, k, sc), true)); k += 2; break;
                        case PathOp.Quad:
                            if (fig != null) fig.Segments.Add(new QuadraticBezierSegment(Pt(gp, k, sc), Pt(gp, k + 2, sc), true)); k += 4; break;
                        case PathOp.Cubic:
                            if (fig != null) fig.Segments.Add(new BezierSegment(Pt(gp, k, sc), Pt(gp, k + 2, sc), Pt(gp, k + 4, sc), true)); k += 6; break;
                        case PathOp.Close:
                            if (fig != null) fig.IsClosed = true; break;
                    }
                }
            }
            else
            {
                pg = PathGeometry.CreateFromGeometry(Glyphs.GetGlyphOutline(glyph, 1000, 1000));
            }
            pg.Freeze();
            _geo[glyph] = pg;
            return pg;
        }

        static Point Pt(GlyphPath gp, int k, double sc)
        {
            return new Point(gp.Pts[k] * sc, -gp.Pts[k + 1] * sc);
        }

        void Finish()
        {
            var map = Map;
            IsSymbol = map.ContainsKey(0xF041) && !map.ContainsKey(0x41);
            var codes = new List<int>(map.Count);
            foreach (int cp in map.Keys)
            {
                if (cp < 0x20 || (cp >= 0x7F && cp <= 0x9F)) continue;
                if (cp >= 0xD800 && cp <= 0xDFFF) continue;
                if (cp == 0xFFFE || cp == 0xFFFF || cp > 0x10FFFF) continue;
                codes.Add(cp);
            }
            codes.Sort();
            Codes = codes;
        }

        /// <summary>Advance width of a glyph as a fraction of the em.</summary>
        public double Advance(ushort glyph)
        {
            if (_adv != null && glyph < _adv.Length) return _adv[glyph];
            if (Glyphs == null) return 0.5;
            return Glyphs.AdvanceWidths[glyph];
        }

        /// <summary>Finds the glyph for a code point (symbol fonts keep A-Z etc. up at U+F0xx).</summary>
        public bool Resolve(int cp, out ushort glyph, out int actualCp)
        {
            var map = Map;
            if (map.TryGetValue(cp, out glyph)) { actualCp = cp; return true; }
            if (IsSymbol && map.TryGetValue(0xF000 + cp, out glyph)) { actualCp = 0xF000 + cp; return true; }
            actualCp = cp;
            return false;
        }

        public string Title { get { return Family + (string.IsNullOrEmpty(Style) ? "" : " " + Style); } }
    }

    public sealed class FontEntry
    {
        public string Name { get; set; }
        public string Badge { get; set; }
        public bool Installed;
        public Visibility BadgeVisibility { get { return Installed ? Visibility.Collapsed : Visibility.Visible; } }
        public Func<List<FontFace>> Loader;
        public List<FontFace> Faces;

        public List<FontFace> GetFaces()
        {
            if (Faces == null)
            {
                try { Faces = Loader != null ? Loader() : new List<FontFace>(); }
                catch { Faces = new List<FontFace>(); }
            }
            return Faces;
        }
    }

    public sealed class PrepareResult
    {
        public string Source;
        public List<string> CachePaths = new List<string>();
        public string Error;
    }

    public static class FontCatalog
    {
        public static readonly string CacheDir = Path.Combine(Path.GetTempPath(), "GlyphCraft");

        static readonly HashSet<string> FontExt = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { ".ttf", ".otf", ".ttc", ".otc", ".woff", ".woff2", ".eot", ".pfb", ".pfa", ".t1" };

        // Companion files that sit beside Type 1 fonts: nothing to open, so they're skipped quietly.
        static readonly HashSet<string> SidecarExt = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { ".afm", ".pfm", ".inf", ".mmm", ".otm", ".fon" };

        public static void CleanCache()
        {
            try
            {
                if (!Directory.Exists(CacheDir)) return;
                foreach (var f in Directory.GetFiles(CacheDir))
                {
                    try { File.Delete(f); } catch { }
                }
            }
            catch { }
        }

        // ------------------------------------------------------------ installed fonts

        public static List<FontEntry> InstalledFonts()
        {
            var list = new List<FontEntry>();
            foreach (var ff in Fonts.SystemFontFamilies)
            {
                FontFamily family = ff;
                string name = family.Source;
                if (string.IsNullOrEmpty(name)) continue;
                list.Add(new FontEntry
                {
                    Name = name,
                    Badge = "FILE",
                    Installed = true,
                    Loader = () => LoadInstalled(family, name)
                });
            }
            list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
            return list;
        }

        static List<FontFace> LoadInstalled(FontFamily family, string name)
        {
            var faces = new List<FontFace>();
            var seen = new HashSet<string>();
            var typefaces = family.GetTypefaces().OrderBy(t => t.Weight.ToOpenTypeWeight())
                                  .ThenBy(t => t.Style.ToString()).ToList();
            foreach (var tf in typefaces)
            {
                GlyphTypeface gt;
                if (!tf.TryGetGlyphTypeface(out gt)) continue;
                string style = PickName(tf.FaceNames);
                if (string.IsNullOrEmpty(style)) style = "Regular";
                if (!seen.Add(style)) continue;
                try { faces.Add(FontFace.CreateInstalled(name, style, gt)); } catch { }
            }
            return faces;
        }

        static string PickName(IDictionary<XmlLanguage, string> names)
        {
            if (names == null || names.Count == 0) return "";
            string v;
            if (names.TryGetValue(XmlLanguage.GetLanguage("en-us"), out v)) return v;
            return names.Values.First();
        }

        static string PickName(IDictionary<CultureInfo, string> names)
        {
            if (names == null || names.Count == 0) return "";
            foreach (var kv in names)
                if (string.Equals(kv.Key.Name, "en-US", StringComparison.OrdinalIgnoreCase)) return kv.Value;
            return names.Values.First();
        }

        // ------------------------------------------------------------ fonts from files

        /// <summary>Background-safe: reads files/folders, decodes to plain sfnt and caches to temp.</summary>
        public static List<PrepareResult> PrepareAll(IEnumerable<string> paths)
        {
            var files = new List<string>();
            foreach (var p in paths)
            {
                try
                {
                    if (Directory.Exists(p))
                    {
                        var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
                        foreach (var f in Directory.EnumerateFiles(p, "*.*", opts))
                            if (FontExt.Contains(Path.GetExtension(f))) files.Add(f);
                    }
                    else if (File.Exists(p) && !SidecarExt.Contains(Path.GetExtension(p))) files.Add(p);
                }
                catch { }
                if (files.Count > 5000) break;
            }

            var results = new List<PrepareResult>();
            foreach (var f in files) results.Add(Prepare(f));
            return results;
        }

        static PrepareResult Prepare(string path)
        {
            var r = new PrepareResult { Source = path };
            try
            {
                var info = new FileInfo(path);
                if (info.Length > 200L * 1024 * 1024) throw new FontDecodeException("The file is too large.");
                var bytes = File.ReadAllBytes(path);
                if (Type1Font.LooksLikeType1(bytes))
                {
                    Directory.CreateDirectory(CacheDir);
                    string hash1;
                    using (var sha1 = SHA1.Create()) hash1 = BitConverter.ToString(sha1.ComputeHash(bytes)).Replace("-", "");
                    string cache1 = Path.Combine(CacheDir, hash1 + ".t1");
                    if (!File.Exists(cache1)) File.WriteAllBytes(cache1, bytes);
                    r.CachePaths.Add(cache1);
                    return r;
                }
                var faces = FontContainer.ExtractFaces(bytes);
                Directory.CreateDirectory(CacheDir);
                foreach (var face in faces)
                {
                    string hash;
                    using (var sha = SHA1.Create())
                        hash = BitConverter.ToString(sha.ComputeHash(face)).Replace("-", "");
                    bool cff = face.Length > 4 && face[0] == 0x4F && face[1] == 0x54;
                    string cache = Path.Combine(CacheDir, hash + (cff ? ".otf" : ".ttf"));
                    if (!File.Exists(cache)) File.WriteAllBytes(cache, face);
                    r.CachePaths.Add(cache);
                }
            }
            catch (Exception ex)
            {
                r.Error = ex.Message;
            }
            return r;
        }

        /// <summary>Reads a cached sfnt entirely with GlyphCraft's own readers (no Windows font loading).</summary>
        public static FontFace MakeLoadedFace(string cachePath, string sourcePath, out string error)
        {
            error = null;
            try
            {
                byte[] raw = File.ReadAllBytes(cachePath);
                SfntData data;
                IOutlineSource outlines;
                if (cachePath.EndsWith(".t1", StringComparison.OrdinalIgnoreCase))
                {
                    var t1 = new Type1Font(raw);
                    data = t1.ToSfntData();
                    outlines = t1;
                }
                else
                {
                    data = SfntInfo.Read(raw);
                    outlines = new OutlineReader(raw);
                }
                if (data.Map.Count == 0)
                    throw new InvalidOperationException("this font has no character map GlyphCraft can read");

                string family = data.Family;
                string style = data.Style;
                if (string.IsNullOrEmpty(family)) family = Path.GetFileNameWithoutExtension(sourcePath);
                if (string.IsNullOrEmpty(style)) style = "Regular";
                return FontFace.CreateFromFile(family, style, Path.GetFileName(sourcePath), data, outlines);
            }
            catch (Exception ex)
            {
                error = "GlyphCraft couldn't read this font (" + ex.Message + ").";
                return null;
            }
        }

        static string Safe(Func<string> f)
        {
            try { return f() ?? ""; } catch { return ""; }
        }

        static string FirstFrame(Exception ex)
        {
            string t = ex.StackTrace;
            if (string.IsNullOrEmpty(t)) return "?";
            int nl = t.IndexOf('\n');
            string line = (nl > 0 ? t.Substring(0, nl) : t).Trim();
            return line.Length > 140 ? line.Substring(0, 140) : line;
        }

    }
}
