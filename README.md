# Radio

A tiny Windows app for listening to live internet radio without opening a browser.
Pick a station, play or pause, set the volume — that's all it does.

![Radio window](docs/screenshot.png)

- **Always live.** Pressing play reopens the stream, so you hear the current broadcast,
  never the point where you paused.
- **Reconnects by itself** if the stream stalls or the network drops.
- **Remembers** the volume, the last station and where the window was.
- **Media keys work**: play/pause, and next/previous to change station.
- No browser or extra player involved — it uses the media engine built into Windows.

Stations it ships with: M-1, LRT Opus, LRT Radijas.

## Install on a PC

Requires Windows 10 (version 2004) or Windows 11, 64-bit.

1. Open the [Releases](https://github.com/JuozapasN/Radio/releases) page and download
   `Radio-win-x64.zip` from the latest release.
2. Extract it to a folder you'll keep, e.g. `%LOCALAPPDATA%\Radio`.
3. Run `RadioPlayer.exe`. Nothing else needs to be installed.

The exe is not code-signed, so the first run on a new PC may show a Windows SmartScreen
prompt — choose **More info → Run anyway**.

To get Desktop and Start menu shortcuts, either right-click the exe and pin it, or run
[install-shortcuts.ps1](install-shortcuts.ps1) from this repo:

```powershell
.\install-shortcuts.ps1 -Exe "$env:LOCALAPPDATA\Radio\RadioPlayer.exe"
```

## Using it

| Action | Mouse | Keyboard |
| --- | --- | --- |
| Play a station | Click it | `1` – `9` |
| Play / pause | Round button, or click the playing station | `Space`, media play/pause key |
| Volume | Slider or mouse wheel | Arrow keys |
| Mute | Speaker icon | `M` |
| Next / previous station | | Media next / previous keys |

Start a station straight away from a shortcut or script:

```powershell
RadioPlayer.exe --play 2            # second station in the list
RadioPlayer.exe --play "LRT Opus"   # by name
```

Only one copy runs at a time; launching it again brings the open window to the front.

## Changing the stations

Edit `stations.json` next to `RadioPlayer.exe` and restart the app:

```json
[
  { "name": "LRT Opus", "note": "lrt.lt", "url": "https://stream-live.lrt.lt/radio_opus/lrt_opus.m3u8" }
]
```

`url` must be the audio stream itself, not the station's web page. HLS (`.m3u8`) streams are
what the app is tested with; direct MP3/AAC streams that Windows can play should work too. If the file is missing or invalid, the three built-in stations are used.

Volume, last station and window position are stored per user in
`%APPDATA%\RadioPlayer\settings.json`.

## Building from source

Needs the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```powershell
dotnet run --project src        # run it
.\build.ps1                     # portable build -> dist\Radio-win-x64.zip
```

`build.ps1` produces a single self-contained `RadioPlayer.exe`, the same thing the release
contains. For a smaller build that relies on the .NET 10 Desktop Runtime being installed:

```powershell
dotnet publish src -c Release -o app
```

### Releasing

Push a version tag and GitHub Actions builds the zip and publishes the release
([release.yml](.github/workflows/release.yml)):

```powershell
git tag v1.1.0
git push origin v1.1.0
```

## Layout

| Path | What |
| --- | --- |
| `src/` | The app: WPF window (`MainWindow.xaml`), playback logic (`MainWindow.xaml.cs`), `stations.json` |
| `build.ps1` | Portable build script |
| `install-shortcuts.ps1` | Creates Desktop / Start menu shortcuts |
| `tools/make-icon.ps1` | Regenerates `src/app.ico` |
| `.github/workflows/release.yml` | Builds and publishes a release when a `v*` tag is pushed |
