using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using GlyphCraft.Services;
using Microsoft.Win32;

namespace GlyphCraft
{
    public partial class ExportWindow : Window
    {
        readonly FontFace _face;
        readonly IReadOnlyList<int> _codes;

        public GlyphExportFormat Format { get; private set; }
        public string Folder { get; private set; }
        public int PngSizePixels { get; private set; }
        public bool Overwrite { get; private set; }
        public bool TransparentBackground { get; private set; }

        public ExportWindow(FontFace face, IReadOnlyList<int> codes, string initialFolder)
        {
            InitializeComponent();
            _face = face;
            _codes = codes ?? new List<int>();

            Intro.Text = _codes.Count + (_codes.Count == 1 ? " selected glyph" : " selected glyphs") +
                         " from " + face.Title + ". Each glyph is written as its own file.";

            PngSize.ItemsSource = new[] { 512, 1024, 2048, 4096 };
            PngSize.SelectedItem = 1024;

            string folder = initialFolder;
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                folder = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            if (string.IsNullOrEmpty(folder))
                folder = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            FolderBox.Text = folder;
        }

        void Format_Changed(object sender, RoutedEventArgs e)
        {
            if (PngOptions == null) return;
            bool png = PngRadio != null && PngRadio.IsChecked == true;
            bool jpg = JpgRadio != null && JpgRadio.IsChecked == true;
            PngOptions.Visibility = png || jpg ? Visibility.Visible : Visibility.Collapsed;
            if (TransparentCheck != null)
                TransparentCheck.Visibility = png ? Visibility.Visible : Visibility.Collapsed;
        }

        void Browse_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFolderDialog { Title = "Choose a folder for the exported glyphs" };
            string current = FolderBox.Text.Trim();
            if (Directory.Exists(current)) dlg.InitialDirectory = current;
            if (dlg.ShowDialog(this) == true) FolderBox.Text = dlg.FolderName;
        }

        void Export_Click(object sender, RoutedEventArgs e)
        {
            string folder = FolderBox.Text.Trim();
            if (folder.Length == 0)
            {
                Warn("Choose a destination folder first.");
                return;
            }

            try
            {
                folder = Path.GetFullPath(folder);
                Directory.CreateDirectory(folder);
            }
            catch (Exception ex)
            {
                Warn("That folder can't be used:\n\n" + ex.Message);
                return;
            }

            GlyphExportFormat format = SvgRadio.IsChecked == true ? GlyphExportFormat.Svg
                                     : PngRadio.IsChecked == true ? GlyphExportFormat.Png
                                     : JpgRadio.IsChecked == true ? GlyphExportFormat.Jpg
                                     : GlyphExportFormat.Eps;
            int pngSize = 1024;
            if (PngSize.SelectedItem is int)
            {
                pngSize = (int)PngSize.SelectedItem;
            }
            else if (PngSize.SelectedItem != null)
            {
                int.TryParse(PngSize.SelectedItem.ToString(), out pngSize);
                if (pngSize < 64) pngSize = 1024;
            }

            bool overwrite = OverwriteCheck.IsChecked == true;
            if (!overwrite)
            {
                int existing = 0;
                foreach (int code in _codes)
                {
                    string path = Path.Combine(folder, GlyphExporter.SuggestFileName(_face, code, format));
                    if (File.Exists(path)) existing++;
                }
                if (existing > 0)
                {
                    var answer = MessageBox.Show(this,
                        existing + (existing == 1 ? " file already exists" : " files already exist") +
                        " in this folder.\n\nReplace " + (existing == 1 ? "it" : "them") + "?",
                        "Export glyphs", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (answer != MessageBoxResult.Yes) return;
                    overwrite = true;
                }
            }

            Format = format;
            Folder = folder;
            PngSizePixels = pngSize;
            Overwrite = overwrite;
            TransparentBackground = format == GlyphExportFormat.Png && TransparentCheck.IsChecked == true;
            DialogResult = true;
        }

        void Warn(string message)
        {
            MessageBox.Show(this, message, "Export glyphs", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
