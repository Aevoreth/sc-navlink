using System;
using System.Windows;

namespace NexusApp.Services;

/// <summary>
/// App-wide visual assets and the restart helper. SC-navLink ships a single theme:
/// the palette is merged statically in App.xaml (Themes/Palette.Luxury.xaml),
/// with no first-run picker and no runtime switching.
/// </summary>
public static class ThemeService
{
    /// <summary>Relaunch the app (used after destructive actions like clearing saved data).</summary>
    public static void RestartApp()
    {
        var path = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(path))
            System.Diagnostics.Process.Start(path);
        Application.Current.Shutdown();
    }

    public static string LogoUri => AppIdentity.LogoPackUri;
}
