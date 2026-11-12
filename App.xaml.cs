using System;
using System.Windows;
using System.Windows.Threading;

namespace GlyphCraft
{
    public partial class App : Application
    {
        public const string VersionText = "1.0.3";
        public const string BuildDate = "12 November 2026";
        public const string SiteUrl = "https://sonuslucis.com";

        public static string[] StartupFiles = new string[0];

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            StartupFiles = e.Args ?? new string[0];
            DispatcherUnhandledException += OnUnhandled;
        }

        static void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show("Something unexpected happened:\n\n" + e.Exception.Message,
                "GlyphCraft", MessageBoxButton.OK, MessageBoxImage.Warning);
            e.Handled = true;
        }
    }
}
