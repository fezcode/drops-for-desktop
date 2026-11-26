using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Drops.Services;

/// <summary>
/// Keeps the sign-in session between runs, so the tray app does not ask for the password after
/// every restart. On Windows the token is encrypted for the current user (DPAPI); on macOS and
/// Linux it is a file only the owner can read. The session is tied to the server it came from.
/// </summary>
public static class SessionStore
{
    public static string? Load(Uri server)
    {
        try
        {
            if (!File.Exists(AppPaths.SessionFile)) return null;
            var lines = File.ReadAllText(AppPaths.SessionFile).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (lines.Length != 2 || lines[0] != server.GetLeftPart(UriPartial.Authority)) return null;
            var raw = lines[1];
            if (!raw.StartsWith("enc:", StringComparison.Ordinal)) return raw;
            if (!OperatingSystem.IsWindows()) return null;
            var data = ProtectedData.Unprotect(Convert.FromBase64String(raw[4..]), null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(data);
        }
        catch (Exception e) when (e is IOException or CryptographicException or FormatException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static void Save(Uri server, string token)
    {
        try
        {
            AppPaths.EnsureCreated();
            var value = token;
            if (OperatingSystem.IsWindows())
                value = "enc:" + Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(token), null, DataProtectionScope.CurrentUser));
            var path = AppPaths.SessionFile;
            if (!OperatingSystem.IsWindows())
            {
                // Create it private before anything secret is written into it.
                using (new FileStream(path, new FileStreamOptions
                       {
                           Mode = FileMode.Create, Access = FileAccess.Write,
                           UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
                       })) { }
            }
            File.WriteAllText(path, server.GetLeftPart(UriPartial.Authority) + "\n" + value + "\n");
        }
        catch (Exception e) when (e is IOException or CryptographicException or UnauthorizedAccessException)
        {
        }
    }

    public static void Clear()
    {
        try { File.Delete(AppPaths.SessionFile); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
