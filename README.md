# TempOverlay

A lightweight Windows system tray app that displays CPU and GPU temperatures as live tray icons.

![Platform](https://img.shields.io/badge/platform-Windows-blue)
![.NET](https://img.shields.io/badge/.NET-8.0-purple)

## Preview

Two tray icons sit in the notification area — CPU temperature in Intel blue, GPU in NVIDIA green — updating every second.

Right-click either icon to open **Settings** or **Exit**.  
Double-click to open **Settings**.

## Requirements

- Windows 10 or later
- [Pawnio driver](https://pawnio.eu/) — required for hardware sensor access (install once, run as Administrator)
- Run as Administrator (the app requests elevation via UAC manifest)

## Getting Started

1. Install the [Pawnio driver](https://pawnio.eu/)
2. Build and publish:
   ```powershell
   cd C:\path\to\overlay
   dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
   ```
3. Run `bin\Release\net8.0-windows\win-x64\publish\TempOverlay.exe` as Administrator

## Auto-start with Windows

Open Settings from the tray icon and check **Start with Windows** — this registers a Task Scheduler task that runs the app at login with admin rights.

To register manually:
```powershell
schtasks /create /tn "TempOverlay" /tr "`"<path-to-exe>`"" /sc onlogon /rl highest /f
```

To remove:
```powershell
schtasks /delete /tn "TempOverlay" /f
```

## Settings

| Option | Description |
|--------|-------------|
| CPU color | Color of the CPU temperature tray icon |
| GPU color | Color of the GPU temperature tray icon |
| Start with Windows | Register/remove the Task Scheduler startup task |

Settings are saved to `%AppData%\TempOverlay\settings.json`.

## System Info

The **System Info** tab in Settings shows live stats:

- **CPU** — temperature (with session min/max), load, clock speed, power draw
- **GPU** — temperature, load
- **Memory** — used / total GB with progress bar
- **Network** — upload and download speed
- **Disk** — read and write speed
- **Battery** — charge level and status
- **System** — user, uptime, process count, screen resolution

## Architecture

| File | Purpose |
|------|---------|
| `Program.cs` | Entry point — checks Pawnio driver, shows warning dialog if missing |
| `Form1.cs` | `TrayApp` — two `NotifyIcon` instances, 1s refresh timer, sensor reading |
| `SettingsForm.cs` | Settings GUI — color pickers, startup toggle, live system stats |
| `AppSettings.cs` | JSON settings persistence to `%AppData%\TempOverlay\` |
| `AppIcon.cs` | Generates the thermometer app icon at runtime via GDI+ |
| `app.ico` | Embedded application icon (Task Manager, Alt+Tab) |
| `app.manifest` | UAC manifest requesting `requireAdministrator` |

## Build

```powershell
# Debug
dotnet run

# Release single-file exe
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

Always kill the running process before building:
```powershell
taskkill /IM TempOverlay.exe /F
```
