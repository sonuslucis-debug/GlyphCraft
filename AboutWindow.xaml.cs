using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;

namespace GlyphCraft
{
    public partial class AboutWindow : Window
    {
        public AboutWindow()
        {
            InitializeComponent();
            VersionText.Text = "Version " + App.VersionText + "  ·  " + App.BuildDate;
        }

        void Link_Navigate(object sender, RequestNavigateEventArgs e)
        {
            try { Process.Start(new ProcessStartInfo(App.SiteUrl) { UseShellExecute = true }); } catch { }
            e.Handled = true;
        }

        void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
