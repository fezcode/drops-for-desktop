using System;
using System.IO;

namespace Drops.Services;

/// <summary>
/// Where Drops keeps its settings and session: the fezcode tree shared with the rest of the suite.
///   Windows  %APPDATA%\fezcode\Drops
///   macOS    ~/Library/Application Support/fezcode/Drops
///   Linux    $XDG_CONFIG_HOME/fezcode/Drops (normally ~/.config/fezcode/Drops)
/// DROPS_HOME points it somewhere else.
/// </summary>
public static class AppPaths
{
    public static string Root
    {
        get
        {
            var over = Environment.GetEnvironmentVariable("DROPS_HOME");
            return !string.IsNullOrWhiteSpace(over)
                ? over
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "fezcode", "Drops");
        }
    }

    public static string SettingsFile => Path.Combine(Root, "settings.json");
    public static string SessionFile => Path.Combine(Root, "session");

    /// <summary>Files fetched for "View" land here; the folder is emptied on every start.</summary>
    public static string ViewCache => Path.Combine(Path.GetTempPath(), "fezcode-drops-view");

    public static void EnsureCreated() => Directory.CreateDirectory(Root);
}
