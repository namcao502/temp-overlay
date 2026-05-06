# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```powershell
# Run (debug)
dotnet run

# Build
dotnet build

# Publish single-file exe
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
# Output: bin\Release\net8.0-windows\win-x64\publish\TempOverlay.exe

# Register auto-start with Windows (run once as admin)
schtasks /create /tn "TempOverlay" /tr "`"<path-to-exe>`"" /sc onlogon /rl highest /f

# Remove auto-start
schtasks /delete /tn "TempOverlay" /f
```

Always kill the running process before building:
```powershell
taskkill /IM TempOverlay.exe /F
```

The app has a UAC manifest (`app.manifest`) requiring admin elevation - always run as Administrator.

## Architecture

**Entry point** - `Program.cs`: checks if the hardware driver (Pawnio/WinRing0) is installed by probing CPU sensors. If no sensor returns a value, shows `ShowDriverMissingDialog()` which opens https://pawnio.eu/ and exits. Otherwise starts `TrayApp`.

**TrayApp** (`Form1.cs`) extends `ApplicationContext` (no main window). Creates two `NotifyIcon` instances - one for CPU (blue), one for GPU (green). A 1-second timer calls `UpdateTray()` which reads sensors via LibreHardwareMonitor and redraws each icon using `DrawIcon()`. Sensor reading tries: package temp first, then any sensor on the hardware, then sub-hardware recursively. Double-click or right-click > Settings opens `SettingsForm`. Right-click > Exit calls `ExitApp()`.

**SettingsForm** (`SettingsForm.cs`): dark-themed WinForms dialog for picking CPU/GPU colors (via `ColorDialog`) and toggling "Start with Windows" (via `schtasks`). Closing the form does NOT exit the app. Save persists to `AppSettings` and reloads colors on next `UpdateTray()` tick.

**AppSettings** (`AppSettings.cs`): serializes/deserializes CPU and GPU hex color strings to `%AppData%\TempOverlay\settings.json`. Provides `GetCpuColor()` / `GetGpuColor()` helpers that parse hex to `Color` with fallback defaults (Intel blue `#0071C5`, NVIDIA green `#76B900`).

## Key constraints

- **Driver required**: LibreHardwareMonitor reads CPU/GPU temps via the Pawnio/WinRing0 kernel driver. Without it, all sensor values are null. The driver is installed separately from https://pawnio.eu/ - it is not bundled.
- **Admin required**: The UAC manifest requests `requireAdministrator`. The Task Scheduler startup task must also use `/rl highest`.
- **Tray icon size**: Icons are drawn as 32x32 bitmaps with dynamic font sizing (max 28pt, steps down until text fits). Windows renders tray icons at 16x16px at 100% DPI - this is a hard OS limit.
- **No discrete GPU**: The test machine has only Intel HD Graphics 630 (integrated). GPU sensor support is included for Nvidia/AMD/Intel GPU hardware types but may show `--` if no temp sensor is readable.
