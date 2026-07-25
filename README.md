# TenzoraX – Modern Controller Mapper
[![C#](https://img.shields.io/badge/C%23-12.0-blue)](https://learn.microsoft.com/dotnet/csharp) [![WPF](https://img.shields.io/badge/WPF-.NET%208.0-green)](https://learn.microsoft.com/dotnet/desktop/wpf) [![Windows](https://img.shields.io/badge/Windows-10%2B-lightgrey)](https://www.microsoft.com/windows) [![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

TenzoraX converts gamepad inputs into keyboard/mouse actions. Supports gaming, emulators, and custom hotkey combinations.

## Features
| Category | Features |
|----------|----------|
| Controller | XInput, DirectInput, auto-detection, hot-plugging |
| Mapping | Visual button mapping, multi-button combos, keyboard/mouse output |
| Keys | A–Z, 0–9, F1–F24, F13–F24, CTRL/ALT/SHIFT/WIN, mouse, numpad |
| Profiles | Save/load configurations |
| UI | Battery indicator, hotkey notifications, sound feedback, DE/EN |
| System | System tray, Windows autostart, update system, persisted window |

## Install
Download from [Releases](https://github.com/Arimtak/TenzoraX/releases), extract ZIP, run `TenzoraX.exe`. Config saved to `Documents\TenzoraX\`.

## Example
```
L1 + SELECT → F13
START       → SHIFT + TAB
A           → SPACE
```

## Tech Stack
**C# · .NET 8 · WPF · Windows**

```bash
dotnet build
```

## AI-Assisted Development
Hobby project developed with AI-assisted programming. All code reviewed, tested, and integrated by the developer.

## License
MIT – see [LICENSE](LICENSE).
