# AURA 0.0.1

AURA is a compact Windows desktop dashboard inspired by Dynamic Island. The
companion character is temporarily removed while a future 3D version is being
designed. The current interface uses a strict black-or-white background,
high-contrast text, and three configurable accent colors.

## Requirements

- Windows 10 or 11
- .NET 10 SDK to build from source
- Microsoft Edge WebView2 Runtime for CDN weather animations; the built-in
  animated scene remains available without it
- WinApp CLI for MSIX packaging (install with `winget install Microsoft.winappcli`)

## Run from source

```powershell
dotnet run --project DynamicIsland/DynamicIsland.csproj -c Release
```

## Build a portable EXE

Run `DynamicIsland/build.bat`. The self-contained Windows x64 executable is
written to `DynamicIsland/publish/AURA.exe`.

## Build an MSIX package

Run `build-msix.bat` from the repository root. The script:

1. Creates a local development certificate when one does not exist.
2. Publishes the WPF app as a self-contained Windows x64 folder.
3. Packages and signs the output as MSIX using `Package.appxmanifest`.

Install the development certificate once on the target PC:

```powershell
winapp cert install .\devcert.pfx
```

Then install the generated `.msix` package. Windows will register AURA with
package identity and the declared User Notification Listener capability,
which is required for the app's explicit notification-access request.

The self-signed certificate is for local testing only. For public distribution,
sign the package with a trusted code-signing certificate. Keep `devcert.pfx`
private and do not commit it.

The ordinary portable EXE build remains available and does not gain package
identity; Windows notification-listener access is available only in the
packaged MSIX installation.

## Features

- Compact top-edge island or notch with a 40 px baseline height, staged
  expansion, hover, drag, and global hotkey controls; track width is capped at
  three times the standard island width
- Clock, battery, CPU/RAM/GPU status, and fullscreen game mode
- Home tools below a reserved future 3D-companion stage: notes, alarm,
  calculator, countdown, screenshots, clipboard, and Explorer shortcuts
- Player synced through Windows Media Session with selectable active sources;
  supports Apple Music, Spotify, and browser players (including YouTube and
  Yandex Music) when they publish a Windows media session. Seeking and transport
  controls depend on the capabilities each player exposes. Includes album art,
  position-aware seeking, playback controls, and system volume; select among
  active media sources and choose from three collapsed layouts with title/artist,
  cover art, and a live system-audio spectrum. Audio is analyzed in memory and
  is not recorded or stored.
- Optional compact weather icon and temperature in the collapsed island/notch
- Live weather conditions and forecast with animated Meteocons SVGs from the
  CDN (local animated fallback when offline), plus a categorized launcher with
  drag-and-drop shortcuts and OpenAI-compatible chat
- Windows quick controls grouped into three 6-item sections, plus brightness,
  volume, and power controls
- Deep-black and pure-white themes with three editable accent colors, plus appearance, behavior,
  status, hotkey, AI, module, and update settings

## App data

Settings, notes, crash logs, and tray-icon cache are stored in
`%APPDATA%\AURA`.

## Third-party credits

Weather animations are provided at runtime by Meteocons, created by Bas Milius,
under the MIT License. Copyright (c) 2020-present Bas Milius. The icons are
loaded from `bmcdn.nl`; AURA does not bundle their SVG files. See the
[Meteocons license](https://github.com/basmilius/weather-icons/blob/main/LICENSE).
If the WebView2 Runtime or network is unavailable, AURA uses its built-in
animated weather scene instead.
