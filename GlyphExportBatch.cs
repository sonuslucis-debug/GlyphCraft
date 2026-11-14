using System;
using System.IO;
using System.Text;
using System.Windows.Media;

namespace GlyphCraft.Services
{
    public enum GlyphExportFormat
    {
        Eps,
        Svg,
        Png,
        Jpg
    }

    public sealed class GlyphExportItemResult
    {
        public int Code;
        public string Path;
        public bool Written;
        public bool Skipped;
        public string Message;
    }

    /// <summary>Writes one or more glyphs as separate EPS, SVG, PNG or JPG files.</summary>
    public static class GlyphExporter
    {
        public static string Extension(GlyphExportFormat format)
        {
            switch (format)
            {
                case GlyphExportFormat.Eps: return ".eps";
                case GlyphExportFormat.Svg: return ".svg";
                case GlyphExportFormat.Jpg: return ".jpg";
                default: return ".png";
            }
        }

        public static string FormatLabel(GlyphExportFormat format)
        {
            switch (format)
            {
                case GlyphExportFormat.Eps: return "EPS (vector)";
                case GlyphExportFormat.Svg: return "SVG (vector)";
                case GlyphExportFormat.Jpg: return "JPG (image)";
                default: return "PNG (image)";
            }
        }

        public static string SuggestFileName(FontFace face, int code, GlyphExportFormat format)
        {
            string family = string.IsNullOrEmpty(face.Family) ? "Font" : face.Family;
            string style = string.IsNullOrEmpty(face.Style) ? "Regular" : face.Style;
            string s = family.Replace(' ', '-') + "-" + style.Replace(' ', '-');
            if (code >= 0 && code < 0x10000 && char.IsLetterOrDigit((char)code)) s += "_" + (char)code;
            s += "_U+" + code.ToString("X4");
            foreach (char bad in Path.GetInvalidFileNameChars()) s = s.Replace(bad, '_');
            return s + Extension(format);
        }

        public static GlyphExportItemResult ExportOne(FontFace face, int code, string folder, GlyphExportFormat format,
                                                      int pngSize, bool overwrite, bool transparent)
        {
            var result = new GlyphExportItemResult { Code = code };
            try
            {
                ushort gi;
                if (face == null || !face.Map.TryGetValue(code, out gi))
                {
                    result.Skipped = true;
                    result.Message = "not in this font";
                    return result;
                }

                PathGeometry pg = GlyphExport.Outline(face, gi);
                if (pg == null || pg.Figures.Count == 0 || pg.Bounds.IsEmpty ||
                    pg.Bounds.Width <= 0 || pg.Bounds.Height <= 0)
                {
                    result.Skipped = true;
                    result.Message = "no outline (probably a space)";
                    return result;
                }

                Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, SuggestFileName(face, code, format));
                result.Path = path;

                if (File.Exists(path) && !overwrite)
                {
                    result.Skipped = true;
                    result.Message = "already exists";
                    return result;
                }

                if (format == GlyphExportFormat.Eps)
                {
                    string title = "U+" + code.ToString("X4") + " " + face.Title;
                    File.WriteAllText(path, GlyphExport.BuildEps(pg, title), Encoding.ASCII);
                }
                else if (format == GlyphExportFormat.Svg)
                {
                    string title = "U+" + code.ToString("X4") + " " + face.Title;
                    File.WriteAllText(path, GlyphExport.BuildSvg(pg, title), new UTF8Encoding(false));
                }
                else if (format == GlyphExportFormat.Jpg)
                {
                    int size = Math.Max(64, Math.Min(4096, pngSize));
                    var bmp = GlyphExport.RenderBitmap(pg, size, 32, Brushes.White);
                    using (var jpg = GlyphExport.ToJpeg(bmp, 94))
                    using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
                        jpg.CopyTo(fs);
                }
                else
                {
                    int size = Math.Max(64, Math.Min(4096, pngSize));
                    var bmp = GlyphExport.RenderBitmap(pg, size, 32, transparent ? null : Brushes.White);
                    using (var png = GlyphExport.ToPng(bmp))
                    using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
                        png.CopyTo(fs);
                }

                result.Written = true;
                return result;
            }
            catch (Exception ex)
            {
                result.Message = ex.Message;
                return result;
            }
        }
    }
}
