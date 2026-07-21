# TenzoraX – Modern Controller Mapper

[![C#](https://img.shields.io/badge/C%23-12.0-blue)](https://learn.microsoft.com/dotnet/csharp)
[![WPF](https://img.shields.io/badge/WPF-.NET%208.0-green)](https://learn.microsoft.com/dotnet/desktop/wpf)
[![Windows](https://img.shields.io/badge/Windows-10%2B-lightgrey)](https://www.microsoft.com/windows)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

**TenzoraX** is a modern controller mapper for Windows that converts gamepad inputs into keyboard and mouse actions. Perfect for gaming, emulators, and accessible computing.

---

## Features

### Controller Support
- XInput & DirectInput support
- Hot-plug detection (plug/unplug controllers)
- Visual button mapping with real-time feedback
- Analog stick visualization
- Battery status display

### Keyboard Mapping
- All letters (A–Z) and number row (0–9)
- Function keys F1–F24 (F13–F24 for gaming shortcuts)
- Modifiers: CTRL, ALT, SHIFT, WIN
- Mouse buttons: Left, Right, Middle
- Numpad keys

### Advanced Features
- **Combinations**: Multiple controller buttons → multiple output keys
- **Profiles**: Save and load different configurations
- **Edit mode**: Visually position buttons on the controller image
- **Language**: English / German interface
- **Auto-start**: Launch with Windows
- **System tray**: Minimize to tray instead of closing
- **Pause**: Temporarily disable all mappings

### Visual Editor
- Drag-and-drop button positioning on a controller image
- Free image scaling with scroll wheel
- Relative button positions that adapt to image size
- Separate modes for image editing and button editing

---

## Installation

### Requirements
- Windows 10 or later
- .NET 8.0 Runtime (included in self-contained builds)

### Quick Start
1. Download the latest `TenzoraX.exe` from the [Releases](https://github.com/deinbenutzername/TenzoraX/releases) page
2. Extract the ZIP archive
3. Run `TenzoraX.exe`

> On first launch, a folder is created in `Documents\TenzoraX\` for configuration and profiles.

---

## Usage

### 1. Connect a controller
Plug in any compatible controller (Xbox, PlayStation, 8BitDo, etc.). It is detected automatically.

### 2. Create a mapping
1. Click a **controller button** on the virtual gamepad
2. Click a **keyboard key** in the lower panel
3. Click **Save combination**

### 3. Example mappings
```
Controller: L1 + SELECT → Keyboard: F13
Controller: START       → Keyboard: SHIFT + TAB
Controller: A           → Keyboard: SPACE
```

### 4. Settings
- **Edit mode** – Visually adjust button positions on the controller image
- **Pause mapping** – Disable all active mappings
- **Start with Windows** – Auto-start on boot
- **Start minimized** – Launch to system tray
- **Minimize (not close)** – Keep running when closing the window

---

## Project Structure

```
TenzoraX/
├── src/
│   └── TenzoraX/
│       ├── App.xaml / .cs          # Application definition
│       ├── MainWindow.xaml / .cs   # Main window UI and logic
│       ├── ControllerManager.cs    # Controller detection and polling
│       ├── InputSimulator.cs       # Keyboard/mouse simulation
│       ├── LanguageManager.cs      # English/German localization
│       ├── ProfileManager.cs       # Profile save/load
│       ├── AssemblyInfo.cs         # Assembly metadata
│       └── TenzoraX.csproj         # Project file
├── assets/
│   └── icons/
│       ├── xbox-360.png            # Controller background image
│       └── app.ico                 # Application icon
├── TenzoraX.sln                    # Solution file
├── README.md                       # This file
├── LICENSE                         # MIT License
└── .gitignore                      # Git ignore rules
```

---

## Development

### Prerequisites
- Visual Studio 2022 or Visual Studio Code
- .NET 8.0 SDK or later

### Build from source
```bash
# Clone the repository
git clone https://github.com/deinbenutzername/TenzoraX.git
cd TenzoraX

# Restore and build
dotnet restore
dotnet build

# Publish (self-contained release)
dotnet publish src/TenzoraX/TenzoraX.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

---

## Tech Stack

- **Language**: C# 12.0
- **Framework**: .NET 8.0 (Windows)
- **UI**: WPF (Windows Presentation Foundation)
- **Controller API**: XInput (via SharpDX / custom implementation)
- **Input Simulation**: Windows API (SendInput)

---

## AI-Assisted Development

This project was created as a learning project by a hobby developer. Parts of the code were written with the assistance of AI (Claude by Anthropic), including:

- Code suggestions and boilerplate generation
- Debugging assistance
- Refactoring guidance
- Documentation and README writing

All AI-generated code was reviewed, tested, adapted, and integrated manually. The developer maintains full responsibility for the codebase, understands every line of code, and made all architectural decisions.

---

## License

This project is open source under the **MIT License**. See the [LICENSE](LICENSE) file for details.

---

## Contributing

Contributions are welcome!

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/YourFeature`)
3. Commit your changes (`git commit -m 'Add YourFeature'`)
4. Push to the branch (`git push origin feature/YourFeature`)
5. Open a Pull Request

---

## Disclaimer

This is a hobby project developed for learning purposes. It is provided as-is, without any warranty or guarantee of fitness for a particular purpose. Bug reports and feature requests are appreciated but response times may vary.
