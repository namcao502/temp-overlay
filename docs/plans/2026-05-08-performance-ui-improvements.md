# TempOverlay: Performance & UI Improvements

> **For agentic workers:** Use s3-implement to execute this plan task-by-task.

**Goal:** Remove the System Info tab, eliminate redundant GDI allocation and double hardware updates, and add temperature color interpolation, C/F toggle, and live icon preview in Settings.

**Architecture:** All changes are in-process WinForms. SettingsForm is trimmed to a single settings-only panel with no timer or hardware reference. `DrawIcon` becomes `internal static` so SettingsForm can render preview bitmaps. Sensor reading stays exclusively in TrayApp's 1-second timer.

**Tech Stack:** .NET 8 WinForms, LibreHardwareMonitor, GDI+

> **No test framework:** This is a WinForms app with no test project. Each task's verification step is `dotnet build` (0 errors) followed by running the app to confirm behavior.

---

### Task 1: Remove System Info tab -- simplify SettingsForm

**Files:**
- Modify: `SettingsForm.cs`
- Modify: `Form1.cs` (remove `_computer` from SettingsForm ctor call)

- [ ] **Step 1: Remove info-panel fields**

Delete these fields from SettingsForm (they become unused once BuildInfo/RefreshStats are gone):

```csharp
// DELETE all of these:
private readonly Font _fHeader;   // only used in BuildInfo section headers
private readonly Font _fMono;     // only used in BuildInfo row values
private static readonly Color CSection; // only used in BuildInfo
private readonly Button _navSettings;
private readonly Button _navInfo;
private readonly Panel _infoPanel;
private Label _lblUser, _lblUptime, _lblProcesses, _lblScreen;
private Label _lblCpuName, _lblCpuTemp, _lblCpuLoad, _lblCpuClock, _lblCpuPower;
private Label _lblGpuName, _lblGpuTemp, _lblGpuLoad;
private Label _lblRam;
private ProgressBar _ramBar;
private Label _lblNetUp, _lblNetDown;
private Label _lblDiskRead, _lblDiskWrite;
private Label _lblBattery;
private readonly System.Windows.Forms.Timer _statsTimer;
private long _lastBytesSent, _lastBytesRecv;
private PerformanceCounter? _diskRead, _diskWrite;
private readonly int _dpiX;
private int _tickCount;
private int _cachedProcessCount;
private readonly Computer _computer;
```

Remove these imports:
- `using System.Net.NetworkInformation;`
- `using LibreHardwareMonitor.Hardware;` (no longer used after `_computer` and `RefreshStats` are removed)

- [ ] **Step 2: Delete info-only methods**

Delete these methods entirely:
- `NavBtn(string text, Rectangle bounds)`
- `Activate(bool settings)`
- `BuildInfo()`
- `RefreshStats()`
- `InitPerfCounters()`
- `InitNetwork()`
- `FmtBytes(long b)`
- The `DoubleBufferedPanel` inner class

- [ ] **Step 3: Rewrite constructor**

Replace the constructor with:

```csharp
public SettingsForm(AppSettings settings)
{
    _settings = settings;
    _cpuColor = settings.GetCpuColor();
    _gpuColor = settings.GetGpuColor();

    Text = "TempOverlay";
    Icon = AppIcon.Create();
    FormBorderStyle = FormBorderStyle.FixedSingle;
    MaximizeBox = false;
    MinimizeBox = false;
    StartPosition = FormStartPosition.CenterScreen;
    ClientSize = new Size(400, 483);
    BackColor = CBg;
    DoubleBuffered = true;

    var titleBar = new Panel { Bounds = new Rectangle(0, 0, 400, 46), BackColor = CSurface };
    var titleLbl = Lbl("TempOverlay", _fTitle, CText);
    titleLbl.AutoSize = true;
    titleBar.Controls.Add(titleLbl);
    titleBar.Layout += (_, _) =>
        titleLbl.Location = new Point((titleBar.Width - titleLbl.PreferredWidth) / 2,
                                      (titleBar.Height - titleLbl.PreferredHeight) / 2);

    _settingsPanel = new Panel { Bounds = new Rectangle(0, 46, 400, 437), BackColor = CBg };
    BuildSettings(out _cpuSwatch, out _gpuSwatch, out _startupCheck);

    Controls.AddRange([titleBar, _settingsPanel]);
}
```

- [ ] **Step 4: Simplify Dispose and OnFormClosed**

```csharp
protected override void Dispose(bool disposing)
{
    if (disposing)
    {
        _fTitle.Dispose();
        _fBody.Dispose();
    }
    base.Dispose(disposing);
}

protected override void OnFormClosed(FormClosedEventArgs e)
{
    base.OnFormClosed(e);
}
```

- [ ] **Step 5: Update Form1.cs OpenSettings**

In `Form1.cs:50`, change:
```csharp
// Before:
_settingsForm = new SettingsForm(_settings, _computer);
// After:
_settingsForm = new SettingsForm(_settings);
```

- [ ] **Step 6: Verify**

Run: `dotnet build`
Expected: 0 errors, 0 unused-field warnings.

---

### Task 2: Optimize tray icon rendering

**Files:**
- Modify: `Form1.cs`

Changes: fix sensor update loop (SettingsForm no longer calls `hw.Update()` so the `_settingsForm == null` guard is dead code), cache last rendered state to skip redundant GDI calls, and show a combined tooltip on both icons.

- [ ] **Step 1: Add cache fields to TrayApp**

After the existing `private SettingsForm? _settingsForm;` line add:

```csharp
private string _lastCpuStr = "";
private string _lastGpuStr = "";
private Color _lastCpuRenderColor = Color.Empty;
private Color _lastGpuRenderColor = Color.Empty;
```

- [ ] **Step 2: Rewrite UpdateTray**

Replace the entire `UpdateTray` method:

```csharp
private void UpdateTray()
{
    float? cpu = null;
    float? gpu = null;

    foreach (var hw in _computer.Hardware)
    {
        hw.Update();
        foreach (var sub in hw.SubHardware)
            sub.Update();

        if (hw.HardwareType == HardwareType.Cpu && cpu == null)
            cpu = ReadPackageTemp(hw) ?? ReadFirstTemp(hw) ?? ReadFirstTempDeep(hw);

        if (hw.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel && gpu == null)
            gpu = ReadFirstTemp(hw) ?? ReadFirstTempDeep(hw);
    }

    string cpuStr = cpu.HasValue ? $"{cpu.Value:F0}" : "--";
    string gpuStr = gpu.HasValue ? $"{gpu.Value:F0}" : "--";
    string unit = "C";

    Color cpuRender = _settings.GetCpuColor();
    Color gpuRender = _settings.GetGpuColor();

    string tooltip = $"CPU {cpuStr}°{unit}  |  GPU {gpuStr}°{unit}";
    _cpuIcon.Text = tooltip;
    _gpuIcon.Text = tooltip;

    if (cpuStr != _lastCpuStr || cpuRender != _lastCpuRenderColor)
    {
        _cpuIcon.Icon?.Dispose();
        _cpuIcon.Icon = DrawIcon(cpuStr, cpuRender);
        _lastCpuStr = cpuStr;
        _lastCpuRenderColor = cpuRender;
    }

    if (gpuStr != _lastGpuStr || gpuRender != _lastGpuRenderColor)
    {
        _gpuIcon.Icon?.Dispose();
        _gpuIcon.Icon = DrawIcon(gpuStr, gpuRender);
        _lastGpuStr = gpuStr;
        _lastGpuRenderColor = gpuRender;
    }
}
```

(The `unit` variable and `cpuRender`/`gpuRender` lines will be extended in Tasks 4 and 5.)

- [ ] **Step 3: Verify**

Run: `dotnet build`
Run app. Tray icon tooltip on hover shows "CPU 72 deg C  |  GPU 55 deg C". Icons only redraw when the value string changes (i.e., once per degree change).

---

### Task 3: Optimize DrawIcon -- font size cache and make internal

**Files:**
- Modify: `Form1.cs`

The existing font-size loop creates and disposes a `Font` on every iteration, every second. Caching by string length reduces this to one `Font` allocation per unique string length (at most 3 in practice: 1-char, 2-char, 3-char temps).

- [ ] **Step 1: Add static cache and change visibility**

Replace the existing `DrawIcon` signature and add the cache:

```csharp
private static readonly Dictionary<int, float> _fontSizeCache = new();

internal static Icon DrawIcon(string value, Color valueColor)
{
    const int w = 32, h = 32;

    using var bmp = new Bitmap(w, h);
    using var g = Graphics.FromImage(bmp);
    using var sf = new StringFormat(StringFormat.GenericTypographic)
    {
        Alignment = StringAlignment.Center,
        LineAlignment = StringAlignment.Center,
    };

    g.SmoothingMode = SmoothingMode.AntiAlias;
    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

    int len = value.Length;
    if (!_fontSizeCache.TryGetValue(len, out float fontSize))
    {
        fontSize = 28f;
        while (fontSize > 4f)
        {
            using var testFont = new Font("Arial", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
            var measured = g.MeasureString(value, testFont, PointF.Empty, sf);
            if (measured.Width <= w && measured.Height <= h) break;
            fontSize -= 1f;
        }
        _fontSizeCache[len] = fontSize;
    }

    using var brush = new SolidBrush(valueColor);
    using var font = new Font("Arial", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
    g.DrawString(value, font, brush, new RectangleF(0, 0, w, h), sf);

    var hIcon = bmp.GetHicon();
    try { return (Icon)Icon.FromHandle(hIcon).Clone(); }
    finally { DestroyIcon(hIcon); }
}
```

- [ ] **Step 2: Verify**

Run: `dotnet build`
Expected: 0 errors. `DrawIcon` is now `internal static` and callable from SettingsForm via `TrayApp.DrawIcon(...)`.

---

### Task 4: Temperature color interpolation

**Files:**
- Modify: `Form1.cs`

Below 60 deg C: user color. 60-80 deg C: lerp toward orange. Above 80 deg C: lerp toward red.

- [ ] **Step 1: Add color helpers after ReadFirstTempDeep**

```csharp
private static Color InterpolateColor(Color baseColor, float? temp)
{
    if (!temp.HasValue || temp.Value < 60f) return baseColor;

    var warm = Color.FromArgb(255, 140, 0);
    var hot  = Color.FromArgb(255, 40,  40);

    if (temp.Value >= 80f)
    {
        float t = Math.Clamp((temp.Value - 80f) / 20f, 0f, 1f);
        return Lerp(warm, hot, t);
    }

    float u = (temp.Value - 60f) / 20f;
    return Lerp(baseColor, warm, u);
}

private static Color Lerp(Color a, Color b, float t) =>
    Color.FromArgb(
        (int)Math.Round(a.R + (b.R - a.R) * t),
        (int)Math.Round(a.G + (b.G - a.G) * t),
        (int)Math.Round(a.B + (b.B - a.B) * t));
```

- [ ] **Step 2: Wire into UpdateTray**

In `UpdateTray`, replace:
```csharp
Color cpuRender = _settings.GetCpuColor();
Color gpuRender = _settings.GetGpuColor();
```
With:
```csharp
Color cpuRender = InterpolateColor(_settings.GetCpuColor(), cpu);
Color gpuRender = InterpolateColor(_settings.GetGpuColor(), gpu);
```

- [ ] **Step 3: Verify**

Run: `dotnet build`
Run app. At CPU temp above 60 deg C the tray icon color shifts toward orange; above 80 deg C it shifts toward red. Below 60 deg C the user-configured color shows unchanged.

---

### Task 5: Temperature unit toggle (C/F)

**Files:**
- Modify: `AppSettings.cs`
- Modify: `SettingsForm.cs`
- Modify: `Form1.cs`

- [ ] **Step 1: Add UseFahrenheit to AppSettings**

In `AppSettings.cs`, add after `GpuColor`:
```csharp
public bool UseFahrenheit { get; set; } = false;
```

- [ ] **Step 2: Add checkbox field to SettingsForm**

Add a field (not readonly -- assigned inside a helper method, not directly in the constructor):
```csharp
private CheckBox _unitCheck = null!;
```

- [ ] **Step 3: Add checkbox in BuildSettings**

In `BuildSettings`, after the `_settingsPanel.Controls.Add(startupCheck)` line, add:

```csharp
y += 36;

_unitCheck = new CheckBox
{
    Text = "Show temperatures in Fahrenheit",
    Location = new Point(24, y),
    AutoSize = true,
    ForeColor = CText,
    Font = _fBody,
    Checked = _settings.UseFahrenheit,
};
_settingsPanel.Controls.Add(_unitCheck);
```

- [ ] **Step 4: Save in Save()**

In the `Save` method, before `Close()`, add:
```csharp
_settings.UseFahrenheit = _unitCheck.Checked;
```

- [ ] **Step 5: Apply conversion in Form1.cs UpdateTray**

In `UpdateTray`, replace:
```csharp
string cpuStr = cpu.HasValue ? $"{cpu.Value:F0}" : "--";
string gpuStr = gpu.HasValue ? $"{gpu.Value:F0}" : "--";
string unit = "C";
```
With:
```csharp
bool useFahr = _settings.UseFahrenheit;
string cpuStr = cpu.HasValue  ? $"{(useFahr ? cpu.Value * 9f / 5f + 32f : cpu.Value):F0}"  : "--";
string gpuStr = gpu.HasValue  ? $"{(useFahr ? gpu.Value * 9f / 5f + 32f : gpu.Value):F0}"  : "--";
string unit = useFahr ? "F" : "C";
```

- [ ] **Step 6: Verify**

Run: `dotnet build`
Run app, open Settings, check Fahrenheit, save. On the next 1-second tick the tray icons show F values and the tooltip reads `CPU 167 degF  |  GPU 131 degF`. Unchecking and saving reverts to C.

---

### Task 6: Live icon preview in SettingsForm

**Files:**
- Modify: `SettingsForm.cs`

Shows a 16x16 rendering of what the tray icon will look like at the chosen color, updated immediately when a color is picked.

- [ ] **Step 1: Add preview PictureBox fields**

(Not readonly -- both are assigned inside `BuildSettings`, not directly in the constructor.)
```csharp
private PictureBox _cpuPreview = null!;
private PictureBox _gpuPreview = null!;
```

- [ ] **Step 2: Add RefreshPreview helper**

Add as a private static method:
```csharp
private static void RefreshPreview(PictureBox box, Color color)
{
    var old = box.Image;
    using var icon = TrayApp.DrawIcon("75", color);
    box.Image = icon.ToBitmap();
    old?.Dispose();
}
```

- [ ] **Step 3: Create preview boxes in BuildSettings and rewire Pick clicks**

In BuildSettings, the CPU block currently reads:
```csharp
var cpuPick = Btn("Pick", new Rectangle(232, y, 64, 28));
cpuPick.Click += (_, _) => PickColor(ref _cpuColor, cpuSwatchRef);
_settingsPanel.Controls.AddRange([cpuSwatch, cpuPick]);
```

Replace with:
```csharp
var cpuPick = Btn("Pick", new Rectangle(232, y, 64, 28));
_cpuPreview = new PictureBox { Location = new Point(308, y + 6), Size = new Size(16, 16), SizeMode = PictureBoxSizeMode.StretchImage };
RefreshPreview(_cpuPreview, _cpuColor);
var cpuPreviewRef = _cpuPreview;
cpuPick.Click += (_, _) =>
{
    PickColor(ref _cpuColor, cpuSwatchRef);
    RefreshPreview(cpuPreviewRef, _cpuColor);
};
_settingsPanel.Controls.AddRange([cpuSwatch, cpuPick, _cpuPreview]);
```

Apply the same pattern to the GPU block:
```csharp
var gpuPick = Btn("Pick", new Rectangle(232, y, 64, 28));
_gpuPreview = new PictureBox { Location = new Point(308, y + 6), Size = new Size(16, 16), SizeMode = PictureBoxSizeMode.StretchImage };
RefreshPreview(_gpuPreview, _gpuColor);
var gpuPreviewRef = _gpuPreview;
gpuPick.Click += (_, _) =>
{
    PickColor(ref _gpuColor, gpuSwatchRef);
    RefreshPreview(gpuPreviewRef, _gpuColor);
};
_settingsPanel.Controls.AddRange([gpuSwatch, gpuPick, _gpuPreview]);
```

- [ ] **Step 4: Dispose preview bitmaps**

In `Dispose(bool disposing)`:
```csharp
_cpuPreview.Image?.Dispose();
_gpuPreview.Image?.Dispose();
```

- [ ] **Step 5: Verify**

Run: `dotnet build`
Run app, open Settings. A 16x16 mini icon showing "75" appears to the right of each Pick button in the selected color. Clicking Pick and choosing a new color updates the preview immediately without needing to save.

---

### Task 7: Fast startup state check

**Files:**
- Modify: `AppSettings.cs`
- Modify: `SettingsForm.cs`

Replace the schtasks process-spawn on every settings-form open with a cached flag in settings.json. The first time the form opens after update, schtasks is queried once and the result is persisted. Subsequent opens read the cached value instantly.

- [ ] **Step 1: Add nullable StartWithWindows to AppSettings**

In `AppSettings.cs`, add after `UseFahrenheit`:
```csharp
public bool? StartWithWindows { get; set; } = null;
```

- [ ] **Step 2: Update BuildSettings to read from settings**

In BuildSettings, change the startupCheck initialization from:
```csharp
Checked = IsStartupEnabled(),
```
To:
```csharp
Checked = _settings.StartWithWindows ?? IsStartupEnabled(),
```

- [ ] **Step 3: Persist on first open in constructor**

In the SettingsForm constructor, after `BuildSettings(out _cpuSwatch, out _gpuSwatch, out _startupCheck)`, add:

```csharp
if (!_settings.StartWithWindows.HasValue)
{
    _settings.StartWithWindows = _startupCheck.Checked;
    _settings.Save();
}
```

- [ ] **Step 4: Replace Save() with the final composed body**

After Tasks 5 and 7 both modify `Save()`, the complete method must be:

```csharp
private void Save(object? sender, EventArgs e)
{
    _settings.CpuColor = ColorTranslator.ToHtml(_cpuColor);
    _settings.GpuColor = ColorTranslator.ToHtml(_gpuColor);
    _settings.UseFahrenheit = _unitCheck.Checked;
    _settings.StartWithWindows = _startupCheck.Checked;
    _settings.Save();
    SetStartup(_startupCheck.Checked);
    Close();
}
```

- [ ] **Step 5: Verify**

Run: `dotnet build`
First run: open Settings -- schtasks is queried once, result saved to settings.json. Close and reopen Settings: no process spawn; checkbox appears immediately. Toggling the checkbox and saving updates both settings.json and the Task Scheduler entry.
