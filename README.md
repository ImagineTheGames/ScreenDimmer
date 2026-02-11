# Screen Dimmer

A Windows system tray application that dims your screen beyond hardware limits and offers a warm color filter for eye care.

## Features

- **Dimmer than hardware** – Uses a transparent overlay to dim below your monitor's minimum brightness
- **Warm color filter** – Reduces blue light with an adjustable amber tint (None → Warmest)
- **System tray** – Runs in the background; right-click the tray icon for sliders
- **Quick toggle** – Enable/disable overlay while keeping your dim and color settings
- **Click-through overlay** – Interact with apps normally while the dimmer is active
- **Start on system startup** – Optional Windows startup entry

## Usage

1. Run `ScreenDimmer.exe`
2. Find the icon in the system tray (bottom-right of the taskbar)
3. Right-click for the context menu:
   - **Dimming** – Slider (0–100%)
   - **Color** – Slider from None to Warmest
   - **Overlay enabled** – Toggle both dim and color on/off
   - **Start on system startup** – Launch with Windows
   - **Exit**

## Build

```bash
dotnet build
```

Run from `bin/Debug/net8.0-windows/ScreenDimmer.exe` or publish for release:

```bash
dotnet publish -c Release -r win-x64 --self-contained
```

## Requirements

- Windows 10/11
- .NET 8 (or use `--self-contained` when publishing)
