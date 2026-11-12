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

## Always run the updated feature build

From the repository root, run `rebuild-feature.bat`. It fetches and checks out
`feature/companion-capsule-eyes`, fast-forwards without discarding local edits,
closes the old AURA process, publishes the new binary, and launches it. This
prevents accidentally running the old `main` build or an old `publish/AURA.exe`.

The feature branch also runs a Windows CI build which compiles the WPF app,
publishes a Windows x64 executable, and tests MSIX package creation. It also
produces a **development** MSIX signed with a disposable self-signed certificate
plus its public `.cer` file. Download both from GitHub Actions to test the
MSIX installation. Import the public certificate in Trusted People before
installing. CI never uploads its private `.pfx` signing key. Development MSIX
packages are not production installers.

## Build a portable EXE

Run `DynamicIsland/build.bat`. The self-contained Windows x64 executable is
written to `DynamicIsland/publish/AURA.exe`.

## Build an MSIX package

Run `build-msix.bat` from the repository root. The script:

1. Creates a local development certificate when one does not exist.
2. Publishes the WPF app as a self-contained Windows x64 folder.
3. Copies MSIX logo assets into the published folder.
4. Packages and signs the output as MSIX using `Package.appxmanifest`.

On the build PC, install the development certificate once from an elevated
PowerShell terminal:

```powershell
winapp cert install .\devcert.pfx
```

Then install the generated `.msix` package. Windows will register AURA with
package identity and the declared User Notification Listener capability,
which is required for the app's explicit notification-access request.

For another test PC, transfer only the generated public `.cer` file and import
it into the Local Machine Trusted People store from an elevated PowerShell
terminal:

```powershell
Import-Certificate -FilePath .\devcert.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
```

Never distribute `devcert.pfx`: it contains the private signing key. The
self-signed certificate is for local testing only. For public distribution,
sign the package with a trusted code-signing certificate.

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

## Figma compact Island states

The compact Body uses the exact device-independent dimensions specified for
the six visual states (before the operating system's display DPI scaling):

| State | Outer Body width × height |
| --- | --- |
| Clock, companion eyes, battery | 215 × 46 |
| Clock + optional seconds, companion eyes, battery | 231 × 46 |
| Interactive system volume | 325 × 24 |
| Playing track + optional headphones/vertical volume meter | 325 × 24 |
| Track change card | 325 × 80 |
| Windows notification | 325 × 80 |

Compact dimensions describe the **black Island itself**, not an inner overlay.
The 325 × 80 cards have 24 DIP rounded corners rather than a full pill radius.
Changes of the compact Body width and height animate together. The track card
returns to the mini-player after four seconds; volume and notification panels
are temporary, and the notification panel has priority over volume changes.

The headphone icon is shown only when the active Windows playback endpoint is
identified as headphones/headset by its Core Audio form factor or device name.
An unsupported driver or ambiguous device name may be classified conservatively;
AURA does not claim that every paired Bluetooth device is an active headphone.

## Compact visual refinements

The base 215x46 DIP island has no vertical divider. Its normal visual layout uses
20 DIP left/right inset and 20 DIP gaps between clock, companion, and battery.
Clock text is 15 DIP Inter SemiBold; unboxed top-aligned seconds and battery
percentage are 8 DIP. The eyes are two 14x24 DIP capsules with 12 DIP separation
and 7 DIP corner radius. Inter is selected when installed on Windows, otherwise
the UI falls back to Segoe UI without bundling a font.

The 325x24 DIP volume and brightness overlays have no interactive slider. Each
uses a 12 DIP icon, a 4 DIP horizontal fill bar, and a percentage. Windows Core
Audio events update the volume line in place; repeated volume keystrokes must
not restart the transient opacity or Body-size animations. A brightness bar
appears for supported built-in screens when Windows WMI reports a change.
Unsupported displays do not show a brightness overlay.

## Compact morph, battery and brightness monitoring

- The clock/eyes/battery compact body is now 235×46 DIP, or 251×46 DIP with
  seconds, providing 10 extra DIP on each side of the companion eye group.
  Each eye remains 14×24 DIP with a 12 DIP gap and 7 DIP radius.
- Battery icon **and** percentage use fixed charge colors, independent of the
  accent theme: 0–20 red; 21–80 yellow; 81–100 green.
- Compact UI content is hidden during the full 225 ms Body geometry morph,
  and revealed only after it has finished. Consecutive volume steps update
  bar/percent in place without restarting the morph.
- Brightness is read from Windows WMI for built-in screens. External primary
  displays additionally use **read-only** DDC/CI GetMonitorBrightness, but
  only after GetMonitorCapabilities explicitly reports brightness support.
  If the display does not support either interface, no brightness card is
  displayed. Monitor DDC/CI support and device drivers may vary.


## Player display modes and brightness diagnostics

In Settings -> Общее -> Информация о треке в островке, choose:
- Мини-плеер — 325×24 (large card on track change for four seconds);
- Развернутый плеер — 325×80 (persistent card while music is playing).

The separate SVG-reference switch changes visual styling, not the display mode.
The saved `ExpandedTrackIsland` setting controls the layout in normal
(non-notification, non-volume) compact playback.

Settings -> Общее -> Диагностика яркости now contains a **Проверить яркость**
button. It reports the actual brightness and detection source where Windows
exposes it. WMI is checked before external-monitor DDC/CI to avoid slow DDC
queries delaying built-in laptop brightness monitoring. If no source is
available, the app reports that limitation rather than inventing a value.

## Unified track card and Windows brightness event fallback

The same `FullTrackCardView` is used for the automatic four-second track-change
preview and the pinned 325×80 playback Island. The duplicate classic card and
its extra style switch were removed; the size switch remains.

For display brightness, AURA also registers a Windows power-setting listener
for `GUID_VIDEO_CURRENT_MONITOR_BRIGHTNESS` and reads the real level from
`WM_POWERBROADCAST/PBT_POWERSETTINGCHANGE`. WMI/DDC/CI remain as fallbacks.
The settings diagnostic displays registration state and the most recent event
level. A system can support brightness keys without publishing this event;
AURA never guesses a brightness percentage.
