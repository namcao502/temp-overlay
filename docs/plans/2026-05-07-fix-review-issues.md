# TempOverlay Fix All Review Issues

> **For agentic workers:** Use s3-implement to execute this plan task-by-task.

**Goal:** Fix all correctness, performance, security, and resource-leak issues found in the code review.

**Architecture:** Four targeted edits across Program.cs, Form1.cs, SettingsForm.cs, and AppIcon.cs. No new files -- error logging is added as private static helpers directly in AppSettings.cs. Each task is self-contained and buildable.

**Tech Stack:** C# 12, .NET 8, WinForms, LibreHardwareMonitor 0.9.6

---

## Task 1: Single-instance mutex + Computer lifetime (Program.cs, Form1.cs)

**Files:**
- Modify: `Program.cs`
- Modify: `Form1.cs`

**Problems fixed:**
- Multiple app instances each add two tray icons (no mutex)
- `IsDriverInstalled` opens a separate `Computer`; then `TrayApp` opens a second one (double work, ~300ms extra startup)

### Step 1: Build baseline (expect success)
```powershell
taskkill /IM TempOverlay.exe /F 2>$null; dotnet build
```
Expected: Build succeeded, 0 error(s)

### Step 2: Refactor Program.cs

Replace entire `Program.cs` with:

```csharp
using System.Diagnostics;
using LibreHardwareMonitor.Hardware;

namespace TempOverlay;

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();

        using var mutex = new Mutex(true, "Global\\TempOverlay_SingleInstance", out bool createdNew);
        if (!createdNew) return;

        var computer = new Computer { IsCpuEnabled = true, IsGpuEnabled = true };
        computer.Open();

        if (!IsDriverInstalled(computer))
        {
            computer.Close();
            ShowDriverMissingDialog();
            return;
        }

        Application.Run(new TrayApp(computer));
    }

    private static void ShowDriverMissingDialog()
    {
        using var form = new Form
        {
            Text = "TempOverlay - Driver Required",
            Icon = AppIcon.Create(),
            FormBorderStyle = FormBorderStyle.FixedSingle,
            MaximizeBox = false,
            MinimizeBox = false,
            StartPosition = FormStartPosition.CenterScreen,
            ClientSize = new Size(320, 150),
            BackColor = Color.FromArgb(30, 30, 30),
        };

        var icon = new Label
        {
            Text = "!",
            Font = new Font("Segoe UI", 24f),
            ForeColor = Color.FromArgb(255, 200, 0),
            Location = new Point(16, 20),
            AutoSize = true,
        };

        var msg = new Label
        {
            Text = "Pawnio driver is not installed.\nPlease download and install it to\nenable hardware temperature reading.",
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9f),
            Location = new Point(64, 20),
            AutoSize = true,
        };

        var downloadBtn = new Button
        {
            Text = "Download Pawnio",
            Location = new Point(16, 106),
            Size = new Size(140, 28),
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.White,
            BackColor = Color.FromArgb(0, 113, 197),
        };
        downloadBtn.FlatAppearance.BorderSize = 0;
        downloadBtn.Click += (_, _) => Process.Start(new ProcessStartInfo("https://pawnio.eu/") { UseShellExecute = true });

        var closeBtn = new Button
        {
            Text = "Close",
            Location = new Point(164, 106),
            Size = new Size(140, 28),
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.White,
            BackColor = Color.FromArgb(60, 60, 60),
        };
        closeBtn.FlatAppearance.BorderSize = 0;
        closeBtn.Click += (_, _) => form.Close();

        form.Controls.AddRange([icon, msg, downloadBtn, closeBtn]);
        form.ShowDialog();
    }

    private static bool IsDriverInstalled(Computer computer)
    {
        try
        {
            foreach (var hw in computer.Hardware)
            {
                hw.Update();
                foreach (var s in hw.Sensors)
                    if (s.SensorType == SensorType.Temperature && s.Value.HasValue)
                        return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }
}
```

**Key changes vs original:**
- `Mutex` guard at top of `Main` -- returns immediately if second instance
- `computer` opened once, passed to `TrayApp` -- no second open
- `IsDriverInstalled` now accepts the already-open `Computer` instead of creating its own
- Unicode `⚠` replaced with ASCII `!` (global coding rule: ASCII only in code/strings)

### Step 3: Update Form1.cs constructor to accept Computer

Change `TrayApp` constructor signature and remove the internal `Computer` construction:

```csharp
public TrayApp(Computer computer)
{
    _settings = AppSettings.Load();

    _computer = computer;  // use the pre-opened instance from Program.cs

    var menu = new ContextMenuStrip();
    // ... rest unchanged
```

Remove these two lines that were previously in the constructor:
```csharp
// DELETE:
_computer = new Computer { IsCpuEnabled = true, IsGpuEnabled = true };
_computer.Open();
```

### Step 4: Fix ExitApp to dispose the timer

```csharp
private void ExitApp()
{
    _timer.Stop();
    _timer.Dispose();
    _computer.Close();
    _cpuIcon.Visible = false;
    _gpuIcon.Visible = false;
    Application.Exit();
}
```

### Step 5: Build -- expect success
```powershell
dotnet build
```
Expected: Build succeeded, 0 error(s)

### Step 6: Manual verification
- Run the exe
- Run it a second time -- second instance must exit immediately (no second pair of tray icons appears)

---

## Task 2: Fix CreateGraphics leak + dispose fonts and timer (SettingsForm.cs)

**Files:**
- Modify: `SettingsForm.cs`

**Problems fixed:**
- `CreateGraphics()` called every second without disposal (GDI handle leak)
- `_fTitle`, `_fHeader`, `_fBody`, `_fMono` Font objects never disposed
- `_statsTimer` stopped but never disposed
- `_diskRead` and `_diskWrite` disposed in `OnFormClosed` -- move to `Dispose(bool)` for correctness

### Step 1: Add `_dpiX` field and cache DPI once at construction

Add field at the top of the class (after existing fields):
```csharp
private readonly int _dpiX;
```

At the end of the constructor, before `InitPerfCounters()`:
```csharp
using (var g = CreateGraphics())
    _dpiX = (int)g.DpiX;
```

### Step 2: Replace the leaking DPI read in RefreshStats

Find and replace in `RefreshStats()`:
```csharp
// OLD (leaks a Graphics handle every second):
_lblScreen.Text = scr != null ? $"{scr.Bounds.Width} x {scr.Bounds.Height}  @  {(int)CreateGraphics().DpiX} DPI" : "--";

// NEW:
_lblScreen.Text = scr != null ? $"{scr.Bounds.Width} x {scr.Bounds.Height}  @  {_dpiX} DPI" : "--";
```

### Step 3: Override Dispose(bool) to clean up fonts and timer

Add this method to `SettingsForm`:
```csharp
protected override void Dispose(bool disposing)
{
    if (disposing)
    {
        _statsTimer.Stop();
        _statsTimer.Dispose();
        _fTitle.Dispose();
        _fHeader.Dispose();
        _fBody.Dispose();
        _fMono.Dispose();
        _diskRead?.Dispose();
        _diskWrite?.Dispose();
    }
    base.Dispose(disposing);
}
```

### Step 4: Update OnFormClosed to stop timer immediately; move Dispose calls to Dispose(bool)

`OnFormClosed` fires before `Dispose`. Keeping `Stop()` here prevents timer ticks between form close and disposal. The three `Dispose()` calls move to `Dispose(bool)` above.

```csharp
// OLD:
protected override void OnFormClosed(FormClosedEventArgs e)
{
    _statsTimer.Stop();
    _diskRead?.Dispose();
    _diskWrite?.Dispose();
    base.OnFormClosed(e);
}

// NEW:
protected override void OnFormClosed(FormClosedEventArgs e)
{
    _statsTimer.Stop();  // stop immediately; Dispose(bool) will call Dispose() on it
    base.OnFormClosed(e);
}
```

### Step 5: Build -- expect success
```powershell
dotnet build
```
Expected: Build succeeded, 0 error(s)

---

## Task 3: Performance + UX fixes (SettingsForm.cs, Form1.cs)

**Files:**
- Modify: `SettingsForm.cs`
- Modify: `Form1.cs`

**Problems fixed:**
- `Process.GetProcesses()` called every second (expensive -- allocates a handle per process)
- `schtasks` args built by hand string interpolation -- use `ArgumentList` instead
- `OpenSettings` calls `BringToFront` but does not restore a minimized window
- Double `hw.Update()` per second when settings form is open (both timers tick simultaneously)

### Step 1: Throttle Process.GetProcesses to every 5 seconds

Add field in `SettingsForm`:
```csharp
private int _tickCount;
private int _cachedProcessCount;
```

In `RefreshStats()`, replace:
```csharp
// OLD:
_lblProcesses.Text = $"{Process.GetProcesses().Length}";

// NEW:
if (_tickCount % 5 == 0)
    _cachedProcessCount = Process.GetProcesses().Length;
_tickCount++;
_lblProcesses.Text = $"{_cachedProcessCount}";
```

### Step 2: Fix schtasks to use ArgumentList

Replace the entire `SetStartup` method:
```csharp
private static void SetStartup(bool enable)
{
    try
    {
        var psi = new ProcessStartInfo("schtasks")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
        };

        if (enable)
        {
            var exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule!.FileName;
            psi.ArgumentList.Add("/create");
            psi.ArgumentList.Add("/tn"); psi.ArgumentList.Add("TempOverlay");
            psi.ArgumentList.Add("/tr"); psi.ArgumentList.Add($"\"{exePath}\"");
            psi.ArgumentList.Add("/sc"); psi.ArgumentList.Add("onlogon");
            psi.ArgumentList.Add("/rl"); psi.ArgumentList.Add("highest");
            psi.ArgumentList.Add("/f");
        }
        else
        {
            psi.ArgumentList.Add("/delete");
            psi.ArgumentList.Add("/tn"); psi.ArgumentList.Add("TempOverlay");
            psi.ArgumentList.Add("/f");
        }

        Process.Start(psi)!.WaitForExit();
    }
    catch { }
}
```

Note: `/tr` still wraps `exePath` in inner quotes because that is what `schtasks` requires to store the command. The outer shell quoting is now handled correctly by `ArgumentList`.

### Step 3: Fix OpenSettings to restore minimized window (Form1.cs)

```csharp
// OLD:
private void OpenSettings()
{
    if (_settingsForm != null)
    {
        _settingsForm.BringToFront();
        return;
    }
    // ...

// NEW:
private void OpenSettings()
{
    if (_settingsForm != null)
    {
        _settingsForm.WindowState = FormWindowState.Normal;
        _settingsForm.Activate();
        return;
    }
    // ...
```

### Step 4: Prevent double hw.Update() -- skip TrayApp update when settings form is open

In `TrayApp.UpdateTray()`, wrap the sensor read loop with a guard. The settings form already calls `hw.Update()` on its own timer, so skip it in the tray timer when the form is visible.

Change the update guard logic:
```csharp
private void UpdateTray()
{
    float? cpu = null;
    float? gpu = null;

    foreach (var hw in _computer.Hardware)
    {
        // Only update if the settings form is not doing it concurrently on this tick.
        // Both timers run on the UI thread so there is no race, but calling Update
        // twice per second on the same hardware is redundant when the form is open.
        if (_settingsForm == null)
        {
            hw.Update();
            foreach (var sub in hw.SubHardware)
                sub.Update();
        }

        if (hw.HardwareType == HardwareType.Cpu && cpu == null)
            cpu = ReadPackageTemp(hw) ?? ReadFirstTemp(hw) ?? ReadFirstTempDeep(hw);

        if (hw.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel && gpu == null)
            gpu = ReadFirstTemp(hw) ?? ReadFirstTempDeep(hw);
    }

    string cpuStr = cpu.HasValue ? $"{cpu.Value:F0}" : "--";
    string gpuStr = gpu.HasValue ? $"{gpu.Value:F0}" : "--";

    _gpuIcon.Icon?.Dispose();
    _gpuIcon.Icon = DrawIcon(gpuStr, _settings.GetGpuColor());
    _gpuIcon.Text = $"GPU {gpuStr}C";

    _cpuIcon.Icon?.Dispose();
    _cpuIcon.Icon = DrawIcon(cpuStr, _settings.GetCpuColor());
    _cpuIcon.Text = $"CPU {cpuStr}C";
}
```

### Step 5: Build -- expect success
```powershell
dotnet build
```
Expected: Build succeeded, 0 error(s)

### Step 6: Manual verification
- Open Settings, minimize it, double-click tray icon -- window should restore (not just flash in taskbar)
- Open Settings and watch Task Manager GDI objects for TempOverlay.exe -- count must be stable, not climbing

---

## Task 4: Cache AppIcon + add error logging (AppIcon.cs, AppSettings.cs)

**Files:**
- Modify: `AppIcon.cs`
- Modify: `AppSettings.cs`

**Problems fixed:**
- `AppIcon.Create()` redraws the thermometer bitmap every time `SettingsForm` is constructed (no cache)
- Silent `catch {}` in `AppSettings.Load()` hides deserialization failures -- write to a log file instead

### Step 1: Add static cache to AppIcon

Replace the entire content of `AppIcon.cs` with:

```csharp
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace TempOverlay;

static class AppIcon
{
    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    private static Icon? _cache;

    public static Icon Create() => _cache ??= BuildIcon();

    private static Icon BuildIcon()
    {
        const int S = 64;
        using var bmp = new Bitmap(S, S);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        var accent  = Color.FromArgb(0, 140, 255);
        var tubeBg  = Color.FromArgb(45, 45, 60);
        var outline = Color.FromArgb(90, 90, 120);

        int cx      = S / 2;
        int tubeW   = 14;
        int tubeX   = cx - tubeW / 2;
        int tubeTop = 5;
        int tubeBot = 43;
        int tubeH   = tubeBot - tubeTop;
        int bulbR   = 11;
        int bulbTop = tubeBot - 4;

        using var tubePath = RoundedRect(tubeX, tubeTop, tubeW, tubeH, tubeW / 2);

        using (var b = new SolidBrush(tubeBg))
            g.FillPath(b, tubePath);

        float fill  = 0.65f;
        int   fillH = (int)(tubeH * fill);
        g.SetClip(tubePath);
        using (var b = new SolidBrush(accent))
            g.FillRectangle(b, tubeX, tubeBot - fillH, tubeW, fillH);
        g.ResetClip();

        using (var p = new Pen(outline, 1.5f))
            g.DrawPath(p, tubePath);

        using (var p = new Pen(Color.FromArgb(110, 110, 140), 1f))
        {
            for (int i = 1; i <= 3; i++)
            {
                int ty = tubeBot - (int)(tubeH * i / 4f);
                g.DrawLine(p, tubeX + 2, ty, tubeX + 6, ty);
            }
        }

        using (var b = new SolidBrush(Color.FromArgb(28, 255, 255, 255)))
            g.FillRectangle(b, tubeX + 3, tubeTop + 6, 3, tubeH - 14);

        using (var b = new SolidBrush(accent))
            g.FillEllipse(b, cx - bulbR, bulbTop, bulbR * 2, bulbR * 2);
        using (var p = new Pen(Color.FromArgb(0, 100, 200), 1.5f))
            g.DrawEllipse(p, cx - bulbR, bulbTop, bulbR * 2, bulbR * 2);

        using (var b = new SolidBrush(Color.FromArgb(50, 255, 255, 255)))
            g.FillEllipse(b, cx - bulbR + 3, bulbTop + 3, 6, 5);

        var hIcon = bmp.GetHicon();
        try { return (Icon)Icon.FromHandle(hIcon).Clone(); }
        finally { DestroyIcon(hIcon); }
    }

    private static GraphicsPath RoundedRect(int x, int y, int w, int h, int r)
    {
        var path = new GraphicsPath();
        path.AddArc(x, y, r * 2, r * 2, 180, 90);
        path.AddArc(x + w - r * 2, y, r * 2, r * 2, 270, 90);
        path.AddLine(x + w, y + r, x + w, y + h);
        path.AddLine(x + w, y + h, x, y + h);
        path.AddLine(x, y + h, x, y + r);
        path.CloseFigure();
        return path;
    }
}
```

The only structural change vs the original: `Create()` is now a one-liner that caches; the drawing logic moves verbatim into `BuildIcon()`.

### Step 2: Add error logging to AppSettings.Load()

```csharp
public static AppSettings Load()
{
    try
    {
        if (File.Exists(SettingsPath))
        {
            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
    }
    catch (Exception ex)
    {
        LogError($"AppSettings.Load failed: {ex.Message}");
    }
    return new AppSettings();
}

private static string LogPath => Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    "TempOverlay", "error.log");

private static void LogError(string message)
{
    try
    {
        var dir = Path.GetDirectoryName(LogPath)!;
        Directory.CreateDirectory(dir);
        File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
    }
    catch { }
}
```

### Step 3: Build -- expect success
```powershell
dotnet build
```
Expected: Build succeeded, 0 error(s)

### Step 4: Final run and smoke test
```powershell
taskkill /IM TempOverlay.exe /F 2>$null; dotnet run
```
Expected:
- Single tray icon pair appears
- Hovering icons shows CPU/GPU temp tooltips
- Double-clicking opens Settings
- Minimizing Settings and clicking tray reopens (not just flashes)
- Opening a second instance of the exe does nothing
- `%AppData%\TempOverlay\error.log` does not exist (no errors on clean run)
