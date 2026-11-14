using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using GlyphCraft.Controls;

namespace GlyphCraft.Services
{
    /// <summary>
    /// Builds a printable one-page type specimen. Everything is drawn as vector
    /// outlines, so fonts opened from files print just as well as installed ones.
    /// </summary>
    public static class SpecimenSheet
    {
        public static readonly Size A4 = new Size(793.7, 1122.5);

        // The sheet is laid out at this design size, then scaled to fit the paper.
        public const double DesignWidth = 793.7;
        public const double DesignHeight = 1122.5;

        public static FixedDocument Build(FontFace face, IReadOnlyList<int> selectedCodes, Size pageSize)
        {
            if (pageSize.Width < 100 || pageSize.Height < 100) pageSize = A4;
            var doc = new FixedDocument();
            doc.DocumentPaginator.PageSize = pageSize;

            var page = new FixedPage { Width = pageSize.Width, Height = pageSize.Height, Background = Brushes.White };
            var content = new SpecimenPage(face, selectedCodes) { Width = pageSize.Width, Height = pageSize.Height };
            FixedPage.SetLeft(content, 0);
            FixedPage.SetTop(content, 0);
            page.Children.Add(content);

            var pageContent = new PageContent();
            ((IAddChild)pageContent).AddChild(page);
            doc.Pages.Add(pageContent);
            return doc;
        }
    }

    sealed class SpecimenPage : FrameworkElement
    {
        const double PageMargin = 56;
        const double DesignWidth = SpecimenSheet.DesignWidth;
        const double DesignHeight = SpecimenSheet.DesignHeight;

        static readonly Brush Navy = GlyphDraw.Navy;
        static readonly Brush Accent = GlyphDraw.Accent;
        static readonly Brush Ink = GlyphDraw.Frozen(Color.FromRgb(0x1A, 0x2A, 0x38));
        static readonly Brush Muted = GlyphDraw.Frozen(Color.FromRgb(0x6B, 0x84, 0x9A));
        static readonly Brush Pale = GlyphDraw.Frozen(Color.FromRgb(0x9A, 0xAF, 0xC2));
        static readonly Brush Tint = GlyphDraw.Tint;
        static readonly Pen Rule = GlyphDraw.MakePen(GlyphDraw.LineBrush, 1);
        static readonly Pen Hair = GlyphDraw.MakePen(GlyphDraw.Frozen(Color.FromRgb(0xE2, 0xE9, 0xF0)), 1);
        static readonly Pen Missing = GlyphDraw.MakePen(GlyphDraw.Frozen(Color.FromRgb(0xC3, 0xD0, 0xDC)), 1);

        readonly FontFace _face;
        readonly IReadOnlyList<int> _selected;

        public SpecimenPage(FontFace face, IReadOnlyList<int> selected)
        {
            _face = face;
            _selected = selected ?? new List<int>();
            ClipToBounds = true;
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, w, h));
            if (_face == null) return;

            double ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            double scale = Math.Min(w / DesignWidth, h / DesignHeight);
            double ox = (w - DesignWidth * scale) / 2;
            double oy = (h - DesignHeight * scale) / 2;
            dc.PushTransform(new TranslateTransform(ox, oy));
            dc.PushTransform(new ScaleTransform(scale, scale));
            Draw(dc, ppd);
            dc.Pop();
            dc.Pop();
        }

        void Draw(DrawingContext dc, double ppd)
        {
            double x = PageMargin;
            double contentW = DesignWidth - PageMargin * 2;
            double y = PageMargin;

            // ---- header ----------------------------------------------------
            var kicker = Brand("GLYPHCRAFT  ·  TYPE SPECIMEN", 9, Accent, FontWeights.Bold, ppd);
            dc.DrawText(kicker, new Point(x, y));
            var date = Brand(DateTime.Now.ToString("d MMMM yyyy", CultureInfo.CurrentCulture), 9, Muted, FontWeights.Normal, ppd);
            dc.DrawText(date, new Point(x + contentW - date.Width, y));
            y += 15;
            dc.DrawLine(Rule, new Point(x, y), new Point(x + contentW, y));
            y += 18;

            var title = Brand(_face.Title, 31, Navy, FontWeights.Bold, ppd);
            title.MaxTextWidth = contentW;
            title.MaxLineCount = 2;
            dc.DrawText(title, new Point(x, y));
            y += title.Height + 3;

            string source = _face.Source == "Installed" ? "Installed font" : "From file: " + _face.Source;
            var meta = Brand(_face.Style + "  ·  " + _face.Codes.Count.ToString("N0") + " characters  ·  " + source,
                             9.5, Muted, FontWeights.Normal, ppd);
            meta.MaxTextWidth = contentW;
            meta.MaxLineCount = 1;
            meta.Trimming = TextTrimming.CharacterEllipsis;
            dc.DrawText(meta, new Point(x, y));
            y += meta.Height + 18;

            // ---- hero panel ------------------------------------------------
            double heroH = 186;
            dc.DrawRoundedRectangle(Tint, null, new Rect(x, y, contentW, heroH), 6, 6);

            string hero = HeroText();
            double heroSize = Math.Min(150, heroH * 0.8);
            double heroW = Measure(hero, heroSize);
            double heroMax = contentW * 0.42;
            if (heroW > heroMax) heroSize *= heroMax / heroW;
            DrawText(dc, hero, heroSize, new Point(x + 26, y + heroH - 26), Navy, ppd);

            double splitX = x + contentW * 0.46;
            dc.DrawLine(Hair, new Point(splitX, y + 20), new Point(splitX, y + heroH - 20));
            double rx = splitX + 22;
            double rw = x + contentW - 22 - rx;

            var glance = Brand("AT A GLANCE", 8, Accent, FontWeights.Bold, ppd);
            dc.DrawText(glance, new Point(rx, y + 17));

            string display = HeroDisplay();
            double displaySize = Fit(display, 32, rw);
            DrawText(dc, display, displaySize, new Point(rx, y + 64), Navy, ppd);

            string numerals = AvailableText("0123456789");
            if (numerals.Length > 0)
            {
                double numberSize = Fit(numerals, 20, rw);
                DrawText(dc, numerals, numberSize, new Point(rx, y + 98), Ink, ppd);
            }

            double sampleY = y + 126;
            double sampleSize = 14.5;
            var lines = Wrap("The quick brown fox jumps over the lazy dog.", sampleSize, rw);
            foreach (string line in lines)
            {
                DrawText(dc, line, sampleSize, new Point(rx, sampleY), Ink, ppd);
                sampleY += sampleSize * 1.5;
                if (sampleY > y + heroH - 12) break;
            }

            y += heroH + 22;

            // ---- character sections ---------------------------------------
            y = Section(dc, "UPPERCASE", AvailableText("ABCDEFGHIJKLMNOPQRSTUVWXYZ"), 25, x, y, contentW, ppd, 11);
            y = Section(dc, "LOWERCASE", AvailableText("abcdefghijklmnopqrstuvwxyz"), 25, x, y, contentW, ppd, 11);
            y = Section(dc, "NUMERALS", AvailableText("0123456789"), 24, x, y, contentW, ppd, 11);
            y = Section(dc, "PUNCTUATION", AvailableText("! \" # $ % & ' ( ) * + , - . / : ; < = > ? @ [ \\ ] ^ _ ` { | } ~"),
                         19, x, y, contentW, ppd, 11);
            string symbols = AvailableText("© ® ™ § ¶ † ‡ • · ° ± × ÷ µ ¢ £ ¤ ¥ € ‰");
            if (symbols.Length > 0)
                y = Section(dc, "SYMBOLS", symbols, 19, x, y, contentW, ppd, 11);

            y = Sample(dc, "SAMPLE TEXT", "The quick brown fox jumps over the lazy dog. " +
                                           "Five quacking zephyrs jolt my wax bed.", 15.5, x, y, contentW, ppd);

            // ---- selected glyphs ------------------------------------------
            if (_selected.Count >= 2)
                DrawSelected(dc, x, y, contentW, ppd);

            // ---- footer ----------------------------------------------------
            double fy = DesignHeight - PageMargin - 7;
            dc.DrawLine(Rule, new Point(x, fy - 12), new Point(x + contentW, fy - 12));
            var footer = Brand("Generated with GlyphCraft  ·  sonuslucis.com", 8, Muted, FontWeights.Normal, ppd);
            dc.DrawText(footer, new Point(x, fy));
            var check = Brand("Check the font licence before reusing its outlines.", 8, Pale, FontWeights.Normal, ppd);
            dc.DrawText(check, new Point(x + contentW - check.Width, fy));
        }

        double Section(DrawingContext dc, string label, string text, double size,
                       double x, double y, double width, double ppd, double gap)
        {
            var lab = Brand(label, 8, Accent, FontWeights.Bold, ppd);
            dc.DrawText(lab, new Point(x, y));
            y += lab.Height + 5;

            if (text.Length == 0)
            {
                var none = Brand("Not present in this font", 10, Muted, FontWeights.Normal, ppd);
                dc.DrawText(none, new Point(x, y));
                return y + none.Height + gap;
            }

            double s = Fit(text, size, width);
            DrawText(dc, text, s, new Point(x, y + s * 0.78), Navy, ppd);
            return y + s + gap;
        }

        double Sample(DrawingContext dc, string label, string text, double size,
                      double x, double y, double width, double ppd)
        {
            var lab = Brand(label, 8, Accent, FontWeights.Bold, ppd);
            dc.DrawText(lab, new Point(x, y));
            y += lab.Height + 5;

            foreach (string line in Wrap(text, size, width))
            {
                DrawText(dc, line, size, new Point(x, y + size * 0.78), Ink, ppd);
                y += size * 1.55;
            }
            return y + 10;
        }

        void DrawSelected(DrawingContext dc, double x, double y, double width, double ppd)
        {
            int shown = Math.Min(_selected.Count, 24);
            string label = "SELECTED CHARACTERS";
            if (_selected.Count > shown) label += "  ·  SHOWING " + shown + " OF " + _selected.Count;
            var lab = Brand(label, 8, Accent, FontWeights.Bold, ppd);
            dc.DrawText(lab, new Point(x, y));
            y += lab.Height + 8;

            const int cols = 8;
            double cellW = width / cols;
            double cellH = 50;
            for (int i = 0; i < shown; i++)
            {
                int code = _selected[i];
                int row = i / cols, col = i % cols;
                double cx = x + col * cellW + cellW / 2;
                double top = y + row * cellH;
                dc.DrawLine(Hair, new Point(cx - cellW / 2, top + cellH), new Point(cx + cellW / 2, top + cellH));

                string glyph = CharOf(code);
                double gw = Measure(glyph, 24);
                DrawText(dc, glyph, 24, new Point(cx - gw / 2, top + 34), Navy, ppd);

                var codeText = Brand("U+" + code.ToString("X4"), 7, Muted, FontWeights.Normal, ppd);
                dc.DrawText(codeText, new Point(cx - codeText.Width / 2, top + 37));
            }
        }

        // ------------------------------------------------------------ text helpers

        string HeroText()
        {
            var sb = new StringBuilder();
            foreach (int cp in new[] { 0x41, 0x61 })
            {
                if (_face.Map.ContainsKey(cp)) sb.Append(char.ConvertFromUtf32(cp));
                if (sb.Length >= 2) break;
            }
            if (sb.Length >= 2) return sb.ToString();
            sb.Clear();
            foreach (int cp in _face.Codes)
            {
                sb.Append(CharOf(cp));
                if (sb.Length >= 2) break;
            }
            return sb.Length > 0 ? sb.ToString() : "A";
        }

        string HeroDisplay()
        {
            foreach (string candidate in new[] { "Aa Bb Cc", "Aa", "ABC", "123" })
            {
                if (AvailableText(candidate).Length == candidate.Replace(" ", "").Length) return candidate;
            }
            var sb = new StringBuilder();
            for (int i = 0; i < Math.Min(7, _face.Codes.Count); i++) sb.Append(CharOf(_face.Codes[i]));
            return sb.ToString();
        }

        string AvailableText(string text)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < text.Length; i++)
            {
                int cp = text[i];
                if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    cp = char.ConvertToUtf32(text[i], text[i + 1]);
                    i++;
                }
                if (cp != ' ' && !_face.Map.ContainsKey(cp)) continue;
                sb.Append(char.ConvertFromUtf32(cp));
            }
            return sb.ToString();
        }

        static string CharOf(int cp)
        {
            try { return char.ConvertFromUtf32(cp); }
            catch { return "?"; }
        }

        double Measure(string text, double size)
        {
            double x = 0;
            for (int i = 0; i < text.Length; i++)
            {
                int cp = text[i];
                if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    cp = char.ConvertToUtf32(text[i], text[i + 1]);
                    i++;
                }
                ushort gi;
                x += _face.Map.TryGetValue(cp, out gi) ? _face.Advance(gi) * size : size * 0.5;
            }
            return x;
        }

        double Fit(string text, double wanted, double maxWidth)
        {
            double w = Measure(text, wanted);
            if (w <= maxWidth || w <= 0) return wanted;
            return Math.Max(5, wanted * maxWidth / w);
        }

        void DrawText(DrawingContext dc, string text, double size, Point origin, Brush brush, double ppd)
        {
            double x = origin.X;
            for (int i = 0; i < text.Length; i++)
            {
                int cp = text[i];
                if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    cp = char.ConvertToUtf32(text[i], text[i + 1]);
                    i++;
                }

                ushort gi;
                if (_face.Map.TryGetValue(cp, out gi))
                {
                    double adv = _face.Advance(gi) * size;
                    GlyphDraw.DrawGlyph(dc, brush, _face, gi, size, new Point(x, origin.Y), ppd);
                    x += adv;
                }
                else
                {
                    double adv = size * 0.5;
                    dc.DrawRectangle(null, Missing, new Rect(x + 1, origin.Y - size * 0.72, adv - 3, size * 0.72));
                    x += adv;
                }
            }
        }

        List<string> Wrap(string text, double size, double maxWidth)
        {
            var lines = new List<string>();
            string current = "";
            foreach (string word in text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string candidate = current.Length == 0 ? word : current + " " + word;
                if (current.Length == 0 || Measure(candidate, size) <= maxWidth)
                {
                    current = candidate;
                }
                else
                {
                    lines.Add(current);
                    current = word;
                }
            }
            if (current.Length > 0) lines.Add(current);
            return lines;
        }

        static FormattedText Brand(string text, double size, Brush brush, FontWeight weight, double ppd)
        {
            FontFamily family;
            try { family = (FontFamily)Application.Current.FindResource("Brand"); }
            catch { family = new FontFamily("Segoe UI"); }
            var typeface = new Typeface(family, FontStyles.Normal, weight, FontStretches.Normal);
            return new FormattedText(text ?? "", CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                                     typeface, size, brush, ppd);
        }
    }
}
