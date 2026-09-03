# TenzoraX

TenzoraX turns your game controller into keyboard and mouse actions – great for games, emulators, or your own custom shortcuts.

Free to use for everyone (MIT license).

![TenzoraX screenshot](screenshot.png)

---

## Installation

1. Download the latest version from [Releases](https://github.com/Arimtak/TenzoraX/releases).
2. Extract the ZIP to any folder.
3. Run `TenzoraX.exe` – no installation required.

> Note: The ZIP contains only the app. The required drivers are downloaded by the app itself (see below).

## Installing the drivers

On first launch you'll see a **status dot** in the top-right of the window:

- 🔴 **Red** = drivers are missing → click it once.
- 🟢 **Green** = drivers are installed – everything is ready.

The app then downloads the drivers (ViGEmBus + HidHide) automatically to `Documents\TenzoraX\drivers` and starts the installation.

## Usage

1. Connect your controller (USB, Bluetooth, or 2.4 GHz).
2. In the main window, click a button on the gamepad (e.g. A, B, LB, RB …).
3. Assign the desired key combination or mouse action on the right.
4. Click **Save** – done.

More features:

- 🕘 **History** – shows every controller ever detected.
- 🔍 **Diagnostics** – checks whether inputs are being received.
- 🎮 **Controller icon in the header** – opens the Windows "Game Controllers" window.
- **Tray icon** – shows your controller's battery level.

## Data

All settings, profiles, and logs are stored under `Documents\TenzoraX\`.

## System requirements

- Windows 10 or newer (64-bit)

## Built with AI assistance

This hobby project was developed with AI-assisted programming. All code was reviewed, tested, and integrated by the developer.

## License

MIT – see [LICENSE](LICENSE). You are free to use, modify, and share this software.