<p align="center"><img src="iris.png" width="96" height="96" alt="Iris logo"></p>
<h1 align="center">Iris</h1>
<p align="center"><strong>A customizable notch for Windows.</strong></p>
<p align="center"><a href="https://github.com/DrakenHelix/Iris/releases/tag/v1.0.0"><strong>Download Iris 1.0</strong></a> · Windows 10/11 x64 · English and German</p>

Iris lives at the top of your screen. It keeps your clock in view and opens to reveal media controls, a timer, a file shelf, a volume mixer, and quick actions.

## Download

| Package | How to start |
| --- | --- |
| [Windows installer](https://github.com/DrakenHelix/Iris/releases/download/v1.0.0/Iris-1.0-Setup.exe) | Choose your installation folder, then open Iris from the Start menu. |
| [Portable ZIP](https://github.com/DrakenHelix/Iris/releases/download/v1.0.0/Iris-1.0-Portable.zip) | Extract the entire archive to a writable folder and open Iris.exe. |

## Features

- **Design** — Choose Black, Liquid Glass, or Transparent and set a separate fullscreen design.
- **Media** — Control playback, view artwork, and follow synchronized lyrics when available.
- **Tools** — Set a countdown, save up to four file or folder shortcuts, and control app volume individually.
- **Gaming and downloads** — See measured FPS in supported games and progress for supported downloads. Iris guides you through the one-time Chrome connection.
- **Add-ons** — Assign supported app actions to eight configurable tiles.

Open **Settings** from the Iris icon in the Windows notification area or the rightmost button in the quick bar. You can change the interface language under **Settings → General**.

## Updates

Open **Settings → About Iris → Check for updates**. When a newer stable release is available, Iris shows the version and lets you download and install it from the app. The refreshed 1.0 packages include this feature. If you installed the original 1.0 build, download the refreshed package once to enable in-app updates.

## Build from source

Clone this repository on 64-bit Windows 10 or 11 with .NET Framework 4.8 and 64-bit Python 3 installed. Open PowerShell in the repository and run:

```powershell
./build.ps1
```

The installer and portable ZIP are written to `build/`. The first build downloads the pinned open-source PresentMon release and checks its SHA-256 digest. See [Contributing](CONTRIBUTING.md) for the branch workflow and [Releasing](RELEASING.md) for publishing.

Iris source code is available under the [MIT License](LICENSE). The [third-party notices](app/ICON-SOURCES.md) describe the icon artwork and its license. PresentMon includes its [own license](app/Tools/PresentMon/LICENSE.txt).
