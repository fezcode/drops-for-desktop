using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Drops.Services;

/// <summary>settings.json in <see cref="AppPaths.Root"/>. Edit it by hand to point Drops at another server.</summary>
public sealed class Settings
{
    public const string DefaultServer = "https://drops.fezcode.studio";

    [JsonPropertyName("server")] public string Server { get; set; } = DefaultServer;

    /// <summary>Closing the window keeps Drops running in the tray. False makes closing quit.</summary>
    [JsonPropertyName("closeToTray")] public bool CloseToTray { get; set; } = true;

    [JsonPropertyName("width")] public double Width { get; set; } = 820;
    [JsonPropertyName("height")] public double Height { get; set; } = 880;

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public Uri ServerUri =>
        Uri.TryCreate(Server.Trim().TrimEnd('/') + "/", UriKind.Absolute, out var u) && (u.Scheme == "https" || u.Scheme == "http")
            ? u
            : new Uri(DefaultServer + "/");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(AppPaths.SettingsFile)) ?? new Settings();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            // A broken file must not stop the app; defaults are used and the file is rewritten on the next save.
        }
        var fresh = new Settings();
        fresh.Save(); // so the file exists to be edited
        return fresh;
    }

    public void Save()
    {
        try
        {
            AppPaths.EnsureCreated();
            var tmp = AppPaths.SettingsFile + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
            File.Move(tmp, AppPaths.SettingsFile, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}
