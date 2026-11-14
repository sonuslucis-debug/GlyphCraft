using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using GlyphCraft.Core;
using GlyphCraft.Services;

namespace GlyphCraft
{
    public sealed class BlockItem
    {
        public string Name { get; set; }     // null = all characters
        public string Label { get; set; }
    }

    public partial class MainWindow : Window
    {
        readonly List<FontEntry> _installed = new List<FontEntry>();
        readonly List<FontEntry> _loaded = new List<FontEntry>();
        readonly HashSet<string> _loadedCache = new HashSet<string>();

        FontEntry _entry;
        FontFace _face;
        int _code = 0x41;
        bool _suppress;
        bool _full;
        string _baseStatus = "";
        string _lastDir;
        string _lastExportDir;

        MenuItem _miCopySelection, _miAddSelection, _miExport, _miSaveEps, _miSaveSvg;

        const string UpperText = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        const string LowerText = "abcdefghijklmnopqrstuvwxyz";
        const string NumberText = "1234567890";
        const string PunctText = "! \" # $ % & ' ( ) * + , - . / : ; < = > ? @ [ \\ ] ^ _ ` { | } ~";
        const string ExtraText = "¡ ¿ « » ‹ › “ ” ‘ ’ „ – — … • · † ‡ § ¶ © ® ™ ° ± × ÷ µ ¢ £ ¤ ¥ € ‰";

        public MainWindow()
        {
            InitializeComponent();

            // Fit smaller / scaled screens (e.g. 1080p laptops at 125-150% scaling)
            var wa = SystemParameters.WorkArea;
            Width = Math.Min(Width, wa.Width - 16);
            Height = Math.Min(Height, wa.Height - 16);

            GlyphGridView.Attach(GridBar);
            GlyphGridView.SelectionChanged += OnGlyphSelected;
            GlyphGridView.SelectionSetChanged += OnSelectionSetChanged;
            GlyphGridView.Activated += code => AddToCopy(code);
            GlyphGridView.Hover += OnHover;
            GlyphGridView.CopyRequested += CopySelectedText;
            var menu = BuildMenu();
            GlyphGridView.ContextMenu = menu;
            Preview.ContextMenu = menu;
        }

        // ------------------------------------------------------------ startup

        async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            FontCatalog.CleanCache();
            try { _installed.AddRange(FontCatalog.InstalledFonts()); } catch { }
            RefreshList();

            var start = _installed.FirstOrDefault(x => x.Name == "Segoe UI") ?? _installed.FirstOrDefault();
            if (start != null)
            {
                FontList.SelectedItem = start;
                FontList.ScrollIntoView(start);
            }

            if (App.StartupFiles.Length > 0) await LoadPathsAsync(App.StartupFiles);
        }

        void SetStatus(string s) { StatusText.Text = s; }

        // ------------------------------------------------------------ font list

        void RefreshList()
        {
            string t = FilterBox.Text.Trim();
            var view = _loaded.Concat(_installed)
                .Where(x => t.Length == 0 || x.Name.IndexOf(t, StringComparison.CurrentCultureIgnoreCase) >= 0)
                .ToList();
            _suppress = true;
            FontList.ItemsSource = view;
            if (_entry != null && view.Contains(_entry)) FontList.SelectedItem = _entry;
            _suppress = false;
        }

        void FilterBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (FontList != null) RefreshList();
        }

        void FontList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppress) return;
            var entry = FontList.SelectedItem as FontEntry;
            if (entry == null || entry == _entry) return;
            SelectEntry(entry);
        }

        void SelectEntry(FontEntry entry)
        {
            _entry = entry;
            var faces = entry.GetFaces();
            if (faces.Count == 0)
            {
                _face = null;
                StyleBox.ItemsSource = null;
                BlockBox.ItemsSource = null;
                GlyphGridView.SetData(null, new List<int>(), -1);
                Preview.Show(null, -1);
                if (ExportBtn != null) ExportBtn.IsEnabled = false;
                if (PrintBtn != null) PrintBtn.IsEnabled = false;
                if (SelectionText != null) SelectionText.Text = "";
                SetStatus(entry.Name + " — Windows couldn't read any glyphs from this font.");
                return;
            }
            StyleBox.ItemsSource = faces;
            StyleBox.SelectedItem = faces.FirstOrDefault(f => f.Style == "Regular") ?? faces[0];
        }

        void StyleBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var face = StyleBox.SelectedItem as FontFace;
            if (face != null) SetFace(face);
        }

        void SetFace(FontFace face)
        {
            _face = face;

            var counts = new Dictionary<string, int>();
            var order = new List<string>();
            foreach (int cp in face.Codes)
            {
                string n = UnicodeBlocks.NameOf(cp);
                if (!counts.ContainsKey(n)) { counts[n] = 0; order.Add(n); }
                counts[n]++;
            }
            var items = new List<BlockItem>();
            items.Add(new BlockItem { Name = null, Label = "All characters (" + face.Codes.Count.ToString("N0") + ")" });
            foreach (string n in order)
                items.Add(new BlockItem { Name = n, Label = n + " (" + counts[n].ToString("N0") + ")" });

            _suppress = true;
            BlockBox.ItemsSource = items;
            BlockBox.SelectedIndex = 0;
            _suppress = false;

            _baseStatus = face.Title + "  ·  " + face.Codes.Count.ToString("N0") + " characters  ·  " +
                          (face.Source == "Installed" ? "Installed font" : "From file: " + face.Source);
            SetStatus(_baseStatus);

            if (PrintBtn != null) PrintBtn.IsEnabled = true;
            FillGrid(PreferredCode());
            if (_full) RefreshFull();
        }

        int PreferredCode()
        {
            if (_face.Codes.Contains(_code)) return _code;
            ushort gi; int actual;
            if (_face.Resolve(0x41, out gi, out actual) && _face.Codes.Contains(actual)) return actual;
            return _face.Codes.Count > 0 ? _face.Codes[0] : -1;
        }

        void BlockBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppress || _face == null) return;
            FillGrid(_code);
        }

        void FillGrid(int prefer)
        {
            var item = BlockBox.SelectedItem as BlockItem;
            List<int> codes = (item == null || item.Name == null)
                ? _face.Codes
                : _face.Codes.Where(cp => UnicodeBlocks.NameOf(cp) == item.Name).ToList();
            GlyphGridView.SetData(_face, codes, prefer);
        }

        // ------------------------------------------------------------ selection + info

        void OnGlyphSelected(int code)
        {
            _code = code;
            UpdateInfo();
        }

        void OnSelectionSetChanged()
        {
            int n = GlyphGridView.SelectedCount;
            if (SelectionText != null) SelectionText.Text = n > 1 ? n + " selected" : "";
            if (ExportBtn != null) ExportBtn.IsEnabled = _face != null && n > 0;
            if (PrintBtn != null) PrintBtn.IsEnabled = _face != null;
        }

        /// <summary>Selected glyphs, falling back to the focused glyph so single-glyph actions still work.</summary>
        List<int> SelectionCodes()
        {
            var codes = new List<int>(GlyphGridView.SelectedCodes);
            if (codes.Count == 0 && _code >= 0) codes.Add(_code);
            return codes;
        }

        void UpdateInfo()
        {
            if (_face == null) return;
            ushort gi;
            if (!_face.Map.TryGetValue(_code, out gi)) return;

            Preview.Show(_face, _code);
            InfoCode.Text = "U+" + _code.ToString("X4");
            InfoBlock.Text = UnicodeBlocks.NameOf(_code);
            double adv = _face.Advance(gi);
            InfoDetail.Text = "Decimal " + _code + "    HTML &#" + _code + ";\nGlyph ID " + gi +
                              "    Advance " + Math.Round(adv * 1000) + "/1000 em";
            var k = Keystroke.For(_code, _face.IsSymbol);
            InfoKeys.Text = k.Keys;
            InfoKeyHint.Text = k.Hint;
        }

        void OnHover(int code)
        {
            if (code < 0) { SetStatus(_baseStatus); return; }
            SetStatus("U+" + code.ToString("X4") + "  " + UnicodeBlocks.NameOf(code) + "   —   " + _baseStatus);
        }

        void Guides_Changed(object sender, RoutedEventArgs e)
        {
            var cb = sender as CheckBox;
            if (Preview != null && cb != null) Preview.ShowGuides = cb.IsChecked == true;
        }

        // ------------------------------------------------------------ find

        void FindBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { DoFind(); e.Handled = true; }
        }

        void Find_Click(object sender, RoutedEventArgs e) { DoFind(); }

        void DoFind()
        {
            if (_face == null) return;
            string t = FindBox.Text.Trim();
            if (t.Length == 0) return;

            int cp = -1;
            if (t.StartsWith("U+", StringComparison.OrdinalIgnoreCase) || t.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                if (!int.TryParse(t.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out cp)) cp = -1;
            }
            else if (t.Length >= 2 && t.All(char.IsDigit))
            {
                if (!int.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out cp)) cp = -1;
            }
            else if (t.Length == 1) cp = t[0];
            else if (t.Length == 2 && char.IsSurrogatePair(t, 0)) cp = char.ConvertToUtf32(t, 0);

            ushort gi; int actual;
            if (cp < 0 || !_face.Resolve(cp, out gi, out actual) || !_face.Codes.Contains(actual))
            {
                SetStatus("“" + t + "” isn't in this font.");
                return;
            }
            if (!GlyphGridView.Select(actual))
            {
                _suppress = true;
                BlockBox.SelectedIndex = 0;
                _suppress = false;
                FillGrid(actual);
            }
            GlyphGridView.Focus();
        }

        // ------------------------------------------------------------ copy

        static string CharString(int cp) { return char.ConvertFromUtf32(cp); }

        void AddToCopy(int code)
        {
            CopyBox.Text += CharString(code);
            CopyBox.CaretIndex = CopyBox.Text.Length;
        }

        void CopyText_Click(object sender, RoutedEventArgs e)
        {
            if (CopyBox.Text.Length > 0) SafeSetText(CopyBox.Text);
            else CopyChar();
        }

        void ClearCopy_Click(object sender, RoutedEventArgs e) { CopyBox.Clear(); }

        void CopyChar()
        {
            if (_face == null) return;
            if (SafeSetText(CharString(_code))) SetStatus("Copied U+" + _code.ToString("X4") + " to the clipboard.");
        }

        void CopySelectedText()
        {
            if (_face == null) return;
            var codes = SelectionCodes();
            if (codes.Count == 0) return;
            if (codes.Count == 1)
            {
                if (SafeSetText(CharString(codes[0])))
                    SetStatus("Copied U+" + codes[0].ToString("X4") + " to the clipboard.");
                return;
            }

            var sb = new StringBuilder();
            foreach (int code in codes) sb.Append(CharString(code));
            if (SafeSetText(sb.ToString())) SetStatus("Copied " + codes.Count + " characters to the clipboard.");
        }

        void AddSelectionToCopy()
        {
            var codes = SelectionCodes();
            if (codes.Count == 0) return;
            var sb = new StringBuilder();
            foreach (int code in codes) sb.Append(CharString(code));
            CopyBox.Text += sb.ToString();
            CopyBox.CaretIndex = CopyBox.Text.Length;
        }

        bool SafeSetText(string s)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try { Clipboard.SetText(s); return true; }
                catch (Exception) { Thread.Sleep(60); }
            }
            MessageBox.Show(this, "Couldn't reach the clipboard — another program may be holding it. Try again.",
                "GlyphCraft", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        // ------------------------------------------------------------ right-click menu

        ContextMenu BuildMenu()
        {
            var m = new ContextMenu();
            m.FontFamily = (FontFamily)FindResource("Brand");

            m.Items.Add(MenuEntry("Copy character", (s, e) => CopyChar()));
            _miCopySelection = MenuEntry("Copy selected characters", (s, e) => CopySelectedText());
            m.Items.Add(_miCopySelection);
            m.Items.Add(MenuEntry("Add to “Characters to copy”", (s, e) => { if (_face != null) AddToCopy(_code); }));
            _miAddSelection = MenuEntry("Add selected characters to “Characters to copy”", (s, e) => AddSelectionToCopy());
            m.Items.Add(_miAddSelection);
            m.Items.Add(MenuEntry("Copy glyph as image (PNG)", (s, e) => CopyImage()));
            m.Items.Add(new Separator());

            _miExport = MenuEntry("Export glyph…", (s, e) => Export_Click(s, e));
            m.Items.Add(_miExport);
            _miSaveEps = MenuEntry("Save glyph as EPS…", (s, e) => SaveVector(true));
            m.Items.Add(_miSaveEps);
            _miSaveSvg = MenuEntry("Save glyph as SVG…", (s, e) => SaveVector(false));
            m.Items.Add(_miSaveSvg);
            m.Items.Add(new Separator());
            m.Items.Add(MenuEntry("Print font specimen…", (s, e) => PrintSpecimen_Click(s, e)));

            m.Opened += delegate { UpdateMenu(); };
            return m;
        }

        void UpdateMenu()
        {
            int n = GlyphGridView.SelectedCount;
            bool multi = n > 1;

            if (_miCopySelection != null)
            {
                _miCopySelection.Header = "Copy " + n + " selected characters";
                _miCopySelection.Visibility = multi ? Visibility.Visible : Visibility.Collapsed;
            }
            if (_miAddSelection != null)
            {
                _miAddSelection.Header = "Add " + n + " selected characters to “Characters to copy”";
                _miAddSelection.Visibility = multi ? Visibility.Visible : Visibility.Collapsed;
            }
            if (_miExport != null)
                _miExport.Header = multi ? "Export " + n + " selected glyphs…" : "Export glyph…";
            if (_miSaveEps != null) _miSaveEps.Visibility = multi ? Visibility.Collapsed : Visibility.Visible;
            if (_miSaveSvg != null) _miSaveSvg.Visibility = multi ? Visibility.Collapsed : Visibility.Visible;
        }

        static MenuItem MenuEntry(string header, RoutedEventHandler handler)
        {
            var mi = new MenuItem { Header = header };
            mi.Click += handler;
            return mi;
        }

        PathGeometry CurrentOutline()
        {
            if (_face == null) return null;
            ushort gi;
            if (!_face.Map.TryGetValue(_code, out gi)) return null;
            PathGeometry pg = GlyphExport.Outline(_face, gi);
            if (pg.Figures.Count == 0)
            {
                MessageBox.Show(this, "This glyph has no outline to export (it's probably a space).",
                    "GlyphCraft", MessageBoxButton.OK, MessageBoxImage.Information);
                return null;
            }
            return pg;
        }

        void CopyImage()
        {
            var pg = CurrentOutline();
            if (pg == null) return;
            try
            {
                var bmp = GlyphExport.RenderBitmap(pg, 1024, 32);
                var data = new DataObject();
                data.SetImage(bmp);
                data.SetData("PNG", GlyphExport.ToPng(bmp));
                Clipboard.SetDataObject(data, true);
                SetStatus("Copied U+" + _code.ToString("X4") + " as an image.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Couldn't copy the image: " + ex.Message, "GlyphCraft",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        string SuggestName()
        {
            string s = _face.Family.Replace(' ', '-') + "-" + _face.Style.Replace(' ', '-');
            if (_code < 0x10000 && char.IsLetterOrDigit((char)_code)) s += "_" + (char)_code;
            s += "_U+" + _code.ToString("X4");
            foreach (char bad in System.IO.Path.GetInvalidFileNameChars()) s = s.Replace(bad, '_');
            return s;
        }

        void SaveVector(bool eps)
        {
            var pg = CurrentOutline();
            if (pg == null) return;

            string title = "U+" + _code.ToString("X4") + " " + _face.Title;
            string text = eps ? GlyphExport.BuildEps(pg, title) : GlyphExport.BuildSvg(pg, title);

            var dlg = new SaveFileDialog
            {
                Title = eps ? "Save glyph as EPS" : "Save glyph as SVG",
                Filter = eps ? "Encapsulated PostScript (*.eps)|*.eps" : "SVG image (*.svg)|*.svg",
                DefaultExt = eps ? ".eps" : ".svg",
                AddExtension = true,
                FileName = SuggestName()
            };
            if (_lastDir != null && Directory.Exists(_lastDir)) dlg.InitialDirectory = _lastDir;
            if (dlg.ShowDialog(this) != true) return;

            try
            {
                Encoding enc = eps ? (Encoding)Encoding.ASCII : new UTF8Encoding(false);
                File.WriteAllText(dlg.FileName, text, enc);
                _lastDir = System.IO.Path.GetDirectoryName(dlg.FileName);
                SetStatus("Saved " + dlg.FileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Couldn't save the file: " + ex.Message, "GlyphCraft",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // ------------------------------------------------------------ batch export

        async void Export_Click(object sender, RoutedEventArgs e)
        {
            if (_face == null) return;
            var codes = SelectionCodes();
            if (codes.Count == 0) return;

            FontFace face = _face;
            var dlg = new ExportWindow(face, codes, _lastExportDir) { Owner = this };
            if (dlg.ShowDialog() != true) return;
            await RunExportAsync(face, codes, dlg);
        }

        async Task RunExportAsync(FontFace face, List<int> codes, ExportWindow options)
        {
            int written = 0, skipped = 0, failed = 0;
            var errors = new List<string>();

            Cursor = Cursors.Wait;
            if (ExportBtn != null) ExportBtn.IsEnabled = false;
            try
            {
                for (int i = 0; i < codes.Count; i++)
                {
                    var r = GlyphExporter.ExportOne(face, codes[i], options.Folder, options.Format,
                                                    options.PngSizePixels, options.Overwrite,
                                                    options.TransparentBackground);
                    if (r.Written) written++;
                    else if (r.Skipped) skipped++;
                    else
                    {
                        failed++;
                        if (errors.Count < 8)
                            errors.Add("U+" + codes[i].ToString("X4") + " — " + r.Message);
                    }

                    if ((i & 7) == 7)
                    {
                        SetStatus("Exporting " + (i + 1) + " of " + codes.Count + " glyphs…");
                        await Dispatcher.InvokeAsync(delegate { }, DispatcherPriority.Background);
                    }
                }

                _lastExportDir = options.Folder;
                string summary = "Exported " + written + (written == 1 ? " glyph" : " glyphs") + " from " + face.Title;
                if (skipped > 0) summary += ", skipped " + skipped;
                if (failed > 0) summary += ", failed " + failed;
                SetStatus(summary + "  ·  " + _baseStatus);

                if (failed > 0)
                {
                    string msg = string.Join("\n", errors);
                    if (failed > errors.Count) msg += "\n…and " + (failed - errors.Count) + " more.";
                    MessageBox.Show(this, "Some glyphs couldn't be exported:\n\n" + msg, "Export glyphs",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "The export stopped unexpectedly:\n\n" + ex.Message, "Export glyphs",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                Cursor = null;
                OnSelectionSetChanged();
            }
        }

        void PrintSpecimen_Click(object sender, RoutedEventArgs e)
        {
            if (_face == null) return;
            new SpecimenWindow(_face, GlyphGridView.SelectedCodes) { Owner = this }.ShowDialog();
        }

        // ------------------------------------------------------------ opening fonts

        async void OpenFont_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Open font files",
                Multiselect = true,
                Filter = "Font files|*.ttf;*.otf;*.ttc;*.otc;*.woff;*.woff2;*.eot;*.pfb;*.pfa;*.t1|All files|*.*"
            };
            if (dlg.ShowDialog(this) == true) await LoadPathsAsync(dlg.FileNames);
        }

        async void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFolderDialog { Title = "Choose a folder (or drive) containing fonts" };
            if (dlg.ShowDialog(this) == true) await LoadPathsAsync(new[] { dlg.FolderName });
        }

        async void Window_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files != null && files.Length > 0) await LoadPathsAsync(files);
        }

        async Task LoadPathsAsync(string[] paths)
        {
            SetStatus("Reading fonts…");
            Cursor = Cursors.Wait;
            List<PrepareResult> results;
            try { results = await Task.Run(() => FontCatalog.PrepareAll(paths)); }
            finally { Cursor = null; }

            var errors = new List<string>();
            FontEntry first = null;
            int count = 0;
            foreach (var r in results)
            {
                if (r.Error != null) { errors.Add(System.IO.Path.GetFileName(r.Source) + " — " + r.Error); continue; }
                foreach (string cache in r.CachePaths)
                {
                    if (!_loadedCache.Add(cache)) continue;
                    string err;
                    FontFace face = FontCatalog.MakeLoadedFace(cache, r.Source, out err);
                    if (face == null) { errors.Add(System.IO.Path.GetFileName(r.Source) + " — " + err); continue; }

                    string family = face.Family;
                    var entry = _loaded.FirstOrDefault(x => x.Name == family);
                    if (entry == null)
                    {
                        entry = new FontEntry { Name = family, Badge = "FILE", Installed = false, Faces = new List<FontFace>() };
                        _loaded.Insert(0, entry);
                    }
                    entry.Faces.Add(face);
                    if (first == null) first = entry;
                    count++;
                }
            }

            if (first != null)
            {
                _entry = null;
                FilterBox.Text = "";
                RefreshList();
                FontList.SelectedItem = first;
                FontList.ScrollIntoView(first);
                SetStatus("Loaded " + count + " font face" + (count == 1 ? "" : "s") + " from file.  " + _baseStatus);
            }
            else if (errors.Count == 0)
            {
                SetStatus("No font files found.");
            }

            if (errors.Count > 0)
            {
                string msg = string.Join("\n", errors.Take(10));
                if (errors.Count > 10) msg += "\n…and " + (errors.Count - 10) + " more.";
                MessageBox.Show(this, "Some files couldn't be opened:\n\n" + msg, "GlyphCraft",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        // ------------------------------------------------------------ full font preview

        void Full_Click(object sender, RoutedEventArgs e)
        {
            _full = !_full;
            FullPanel.Visibility = _full ? Visibility.Visible : Visibility.Collapsed;
            FullBtn.Content = _full ? "Back to grid" : "Full font preview";
            if (_full) RefreshFull();
        }

        void FullSize_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_full) RefreshFull();
        }

        void RefreshFull()
        {
            if (_face == null) return;
            double s = FullSize.Value;
            FullTitle.Text = _face.Title;
            FpUpper.Set(_face, UpperText, s);
            FpLower.Set(_face, LowerText, s);
            FpNumbers.Set(_face, NumberText, s);
            FpPunct.Set(_face, PunctText, s);
            FpExtra.Set(_face, ExtraText, s);
        }

        // ------------------------------------------------------------ about

        void About_Click(object sender, RoutedEventArgs e)
        {
            new AboutWindow { Owner = this }.ShowDialog();
        }
    }
}
