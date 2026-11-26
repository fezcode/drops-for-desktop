# Drops for desktop

![Drops](Assets/banner.png)

`Drops` sends a file to your other device, from a window and from the tray. It is the desktop
client for [Drops](https://drops.fezcode.studio): put a file in on one device, take it out on
another, and everything deletes itself after an hour.

Built with **Avalonia UI** and **C#**. It is a native app, not a web view: the interface is a
one-to-one rebuild of the Drops page in the Fezcode Studio "Daylight" kit, and it talks to the
Drops server's own API. It runs on Windows, macOS and Linux.

## What it does

- **Put files in**: drag them onto the window, paste them, or choose them. They are sent one at
  a time, with honest progress.
- **Send a piece of text** instead: a link, a note, a code.
- **Take files out**: Save, View (pictures and video open in your own viewer), Copy (text), Remove.
- **Lives in the tray**: closing the window keeps Drops running. The tray icon shows how many
  files are in the drop and offers *Show Drops*, *Send files…*, *Send clipboard*, *Sign out*
  and *Quit*.
- **Stays signed in**: the session is kept between runs, until you sign out from the tray.
- **Its own title bar**, in the same look as the rest of the window.
- Change your password from the bottom of the window.

Accounts are by invitation. There is no sign-up.

## Releases

Download the latest build from the [Releases](https://github.com/fezcode/drops-for-desktop/releases) page:

| System | File |
|---|---|
| Windows | `Drops-Setup-<version>.exe` |
| macOS (Apple Silicon / Intel) | `Drops-<version>-osx-arm64.dmg` / `Drops-<version>-osx-x64.dmg` |
| Linux | `Drops-<version>-linux-x64.tar.gz` |

The macOS app is signed ad-hoc, not notarized: the first time, right-click it and choose **Open**.

## Settings

Drops keeps its settings and session in the fezcode folder:

| System | Folder |
|---|---|
| Windows | `%APPDATA%\fezcode\Drops` |
| macOS | `~/Library/Application Support/fezcode/Drops` |
| Linux | `~/.config/fezcode/Drops` (follows `XDG_CONFIG_HOME`) |

Set `DROPS_HOME` to use another folder.

`settings.json`:

```json
{
  "server": "https://drops.fezcode.studio",
  "closeToTray": true,
  "width": 820,
  "height": 880
}
```

- `server`: the Drops server to use. Change it to point at your own.
- `closeToTray`: `false` makes closing the window quit Drops.

`session` holds the sign-in session. On Windows it is encrypted for your user account; on macOS
and Linux it is a file only you can read. Signing out (from the tray menu) removes it.

## Command line

```
Drops              open the window (or bring the running copy forward)
Drops --hidden     start in the tray without opening the window
Drops --version
Drops --help
```

## Build requirements

- **.NET 8.0 SDK** (or newer)

```powershell
dotnet run
```

## Packaging and releasing

The version lives in `<Version>` in `Drops.csproj`; keep `[app] version` in `forge.toml` in step with it.

**Windows.** The installer is built with [Forge](https://github.com/fezcode/Forge) from `forge.toml`:

```powershell
.\build-installer.ps1     # publish + bundle -> dist\Drops-Setup-<version>.exe
```

**Release.** Bump the version in both files, build the installer, commit, then:

```powershell
git tag vX.Y.Z
git push origin main vX.Y.Z
gh release create vX.Y.Z dist\Drops-Setup-X.Y.Z.exe --title "Drops vX.Y.Z" --notes "..."
```

**macOS and Linux.** Publishing that release starts `.github/workflows/unix-release.yml`, which
builds the same tag on GitHub's runners and attaches the `.tar.gz` and the two `.dmg` files.
To try a Unix build locally: `./build-unix.ps1 -Rid linux-x64`.

## Assets

`Assets/` is generated from the Drops web kit by `tools/make-assets.py`: the fonts (Inter and
Bricolage Grotesque, pinned to the weights the page uses), the sage droplet icon and the banner.
Both fonts are under the SIL Open Font License.

## License

MIT. See `LICENSE.txt`.
