using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using GlyphCraft.Services;

namespace GlyphCraft
{
    public partial class SpecimenWindow : Window
    {
        readonly FontFace _face;
        readonly IReadOnlyList<int> _selected;
        readonly FixedDocument _preview;

        public SpecimenWindow(FontFace face, IReadOnlyList<int> selected)
        {
            InitializeComponent();
            _face = face;
            _selected = selected ?? new List<int>();

            Subtitle.Text = face.Title + "  ·  " + face.Codes.Count.ToString("N0") + " characters" +
                            (_selected.Count >= 2 ? "  ·  " + _selected.Count + " selected" : "");

            _preview = SpecimenSheet.Build(face, _selected, SpecimenSheet.A4);
            Viewer.Document = _preview;
        }

        void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Wait for the viewer's template to finish laying out, then show the top of page one.
            Dispatcher.BeginInvoke(new Action(delegate
            {
                try
                {
                    Viewer.FitToWidth();
                    Viewer.VerticalOffset = 0;
                    Viewer.HorizontalOffset = 0;
                }
                catch { }
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        void Print_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new PrintDialog();
            if (dlg.ShowDialog() != true) return;

            try
            {
                var size = new Size(dlg.PrintableAreaWidth, dlg.PrintableAreaHeight);
                FixedDocument doc = SpecimenSheet.Build(_face, _selected, size);
                dlg.PrintDocument(doc.DocumentPaginator, "GlyphCraft specimen — " + _face.Title);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Couldn't print the specimen sheet:\n\n" + ex.Message,
                    "GlyphCraft", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
