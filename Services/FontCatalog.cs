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

        public static FontFace Create(string family, string style, string source, GlyphTypeface gt)
        {
            var f = new FontFace { Family = family, Style = style, Source = source, Glyphs = gt };
            var map = gt.CharacterToGlyphMap;
            f.IsSymbol = map.ContainsKey(0xF041) && !map.ContainsKey(0x41);
            var codes = new List<int>(map.Count);
            foreach (int cp in map.Keys)
            {
                if (cp < 0x20 || (cp >= 0x7F && cp <= 0x9F)) continue;
                if (cp >= 0xD800 && cp <= 0xDFFF) continue;
                if (cp == 0xFFFE || cp == 0xFFFF || cp > 0x10FFFF) continue;
                codes.Add(cp);
            }
            codes.Sort();
            f.Codes = codes;
            return f;
        }

        /// <summary>Finds the glyph for a code point (symbol fonts keep A-Z etc. up at U+F0xx).</summary>
        public bool Resolve(int cp, out ushort glyph, out int actualCp)
        {
            var map = Glyphs.CharacterToGlyphMap;
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
        { ".ttf", ".otf", ".ttc", ".otc", ".woff", ".woff2", ".eot" };

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
                try { faces.Add(FontFace.Create(name, style, "Installed", gt)); } catch { }
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
                    else if (File.Exists(p)) files.Add(p);
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

        /// <summary>UI thread: opens a cached sfnt as a GlyphTypeface.</summary>
        public static FontFace MakeLoadedFace(string cachePath, string sourcePath, out string error)
        {
            error = null;
            Exception last = null;
            foreach (string path in CandidatePaths(cachePath))
            {
                try
                {
                    var gt = new GlyphTypeface(new Uri(path));
                    string family = PickName(gt.Win32FamilyNames);
                    string style = PickName(gt.Win32FaceNames);
                    if (string.IsNullOrEmpty(family)) family = PickName(gt.FamilyNames);
                    if (string.IsNullOrEmpty(family)) family = Path.GetFileNameWithoutExtension(sourcePath);
                    if (string.IsNullOrEmpty(style)) style = PickName(gt.FaceNames);
                    if (string.IsNullOrEmpty(style)) style = "Regular";
                    return FontFace.Create(family, style, Path.GetFileName(sourcePath), gt);
                }
                catch (Exception ex)
                {
                    last = ex;
                }
            }
            error = "Windows couldn't open this font" +
                    (last != null ? " (" + last.GetType().Name + ": " + last.Message + ")" : "") + ".";
            return null;
        }

        // Second attempt for OpenType/CFF files: same bytes under a .ttf name.
        static IEnumerable<string> CandidatePaths(string cachePath)
        {
            yield return cachePath;
            if (cachePath.EndsWith(".otf", StringComparison.OrdinalIgnoreCase))
            {
                string alt = Path.ChangeExtension(cachePath, ".ttf");
                bool ok = true;
                try { if (!File.Exists(alt)) File.Copy(cachePath, alt); }
                catch { ok = false; }
                if (ok) yield return alt;
            }
        }
    }
}
