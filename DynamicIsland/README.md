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

## Run from source

```powershell
dotnet run --project DynamicIsland.csproj -c Release
```

For a self-contained Windows x64 executable, run `build.bat` from this folder.
The output is written to `publish\AURA.exe`.

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
  cover art, and a live system-audio
  spectrum. Audio is analyzed in memory and is not recorded or stored.
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
