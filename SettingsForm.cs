using System.Diagnostics;
using System.Net.NetworkInformation;
using LibreHardwareMonitor.Hardware;

namespace TempOverlay;

public class SettingsForm : Form
{
    // Palette
    private static readonly Color CBg      = Color.FromArgb(18, 18, 18);
    private static readonly Color CSurface = Color.FromArgb(26, 26, 26);
    private static readonly Color CBorder  = Color.FromArgb(50, 50, 50);
    private static readonly Color CAccent  = Color.FromArgb(0, 120, 255);
    private static readonly Color CText    = Color.FromArgb(228, 228, 228);
    private static readonly Color CMuted   = Color.FromArgb(110, 110, 110);
    private static readonly Color CSection = Color.FromArgb(99, 179, 255);

    // Fonts
    private readonly Font _fTitle  = new("Segoe UI", 11f, FontStyle.Bold);
    private readonly Font _fHeader = new("Segoe UI", 8.5f, FontStyle.Bold);
    private readonly Font _fBody   = new("Segoe UI", 8.5f);
    private readonly Font _fMono   = new("Consolas", 8.5f);

    // State
    private readonly AppSettings _settings;
    private readonly Computer _computer;
    private Color _cpuColor;
    private Color _gpuColor;

    // Settings tab
    private readonly Panel _cpuSwatch;
    private readonly Panel _gpuSwatch;
    private readonly CheckBox _startupCheck;

    // Nav
    private readonly Button _navSettings;
    private readonly Button _navInfo;
    private readonly Panel _settingsPanel;
    private readonly Panel _infoPanel;

    // Info labels
    private Label _lblUser = null!, _lblUptime = null!, _lblProcesses = null!, _lblScreen = null!;
    private Label _lblCpuName = null!, _lblCpuTemp = null!, _lblCpuLoad = null!, _lblCpuClock = null!, _lblCpuPower = null!;
    private Label _lblGpuName = null!, _lblGpuTemp = null!, _lblGpuLoad = null!;
    private Label _lblRam = null!;
    private ProgressBar _ramBar = null!;
    private Label _lblNetUp = null!, _lblNetDown = null!;
    private Label _lblDiskRead = null!, _lblDiskWrite = null!;
    private Label _lblBattery = null!;

    // Perf
    private readonly System.Windows.Forms.Timer _statsTimer;
    private long _lastBytesSent, _lastBytesRecv;
    private PerformanceCounter? _diskRead, _diskWrite;

    public SettingsForm(AppSettings settings, Computer computer)
    {
        _settings = settings;
        _computer = computer;
        _cpuColor = settings.GetCpuColor();
        _gpuColor = settings.GetGpuColor();

        Text = "TempOverlay";
        Icon = AppIcon.Create();
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(400, 520);
        BackColor = CBg;
        DoubleBuffered = true;

        // Title
        var titleBar = new Panel { Bounds = new Rectangle(0, 0, 400, 46), BackColor = CSurface };
        var titleLbl = Lbl("TempOverlay", _fTitle, CText);
        titleLbl.AutoSize = true;
        titleBar.Controls.Add(titleLbl);
        titleBar.Layout += (_, _) =>
            titleLbl.Location = new Point((titleBar.Width - titleLbl.PreferredWidth) / 2,
                                          (titleBar.Height - titleLbl.PreferredHeight) / 2);

        // Nav
        var navBar = new Panel { Bounds = new Rectangle(0, 46, 400, 36), BackColor = CBg };
        _navSettings = NavBtn("Settings",    new Rectangle(0,   0, 200, 36));
        _navInfo     = NavBtn("System Info", new Rectangle(200, 0, 200, 36));
        navBar.Controls.AddRange([_navSettings, _navInfo]);

        var navLine = new Panel { Bounds = new Rectangle(0, 82, 400, 1), BackColor = CBorder };

        // Panels
        _settingsPanel = new Panel { Bounds = new Rectangle(0, 83, 400, 437), BackColor = CBg };
        _infoPanel     = new Panel { Bounds = new Rectangle(0, 83, 400, 437), BackColor = CBg, Visible = false };

        BuildSettings(out _cpuSwatch, out _gpuSwatch, out _startupCheck);
        BuildInfo();

        Controls.AddRange([titleBar, navBar, navLine, _settingsPanel, _infoPanel]);

        _navSettings.Click += (_, _) => Activate(settings: true);
        _navInfo.Click     += (_, _) => Activate(settings: false);
        Activate(settings: true);

        InitPerfCounters();
        InitNetwork();

        _statsTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _statsTimer.Tick += (_, _) => RefreshStats();
        _statsTimer.Start();
        RefreshStats();
    }

    // ── Nav ─────────────────────────────────────────────────────────────────

    private Button NavBtn(string text, Rectangle bounds)
    {
        var b = new Button
        {
            Text = text, Bounds = bounds,
            FlatStyle = FlatStyle.Flat,
            Font = _fBody, ForeColor = CMuted, BackColor = CBg,
            Cursor = Cursors.Hand,
        };
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = Color.FromArgb(30, 30, 30);
        b.Paint += (_, e) =>
        {
            if (b.ForeColor != CText) return;
            using var p = new Pen(CAccent, 2);
            e.Graphics.DrawLine(p, 12, b.Height - 2, b.Width - 12, b.Height - 2);
        };
        return b;
    }

    private void Activate(bool settings)
    {
        _settingsPanel.Visible = settings;
        _infoPanel.Visible     = !settings;
        _navSettings.ForeColor = settings  ? CText : CMuted;
        _navInfo.ForeColor     = !settings ? CText : CMuted;
        _navSettings.Invalidate();
        _navInfo.Invalidate();
    }

    // ── Settings panel ───────────────────────────────────────────────────────

    private void BuildSettings(out Panel cpuSwatch, out Panel gpuSwatch, out CheckBox startupCheck)
    {
        int y = 28;

        Panel Swatch(Color c, int top)
        {
            var sw = new Panel { Location = new Point(140, top), Size = new Size(80, 28), BackColor = c, Cursor = Cursors.Hand };
            sw.Paint += (_, e) =>
            {
                using var p = new Pen(CBorder);
                e.Graphics.DrawRectangle(p, 0, 0, sw.Width - 1, sw.Height - 1);
            };
            return sw;
        }

        // CPU row
        _settingsPanel.Controls.Add(Lbl("CPU color", _fBody, CMuted, new Point(24, y + 5)));
        cpuSwatch = Swatch(_cpuColor, y);
        var cpuSwatchRef = cpuSwatch;
        var cpuPick = Btn("Pick", new Rectangle(232, y, 64, 28));
        cpuPick.Click += (_, _) => PickColor(ref _cpuColor, cpuSwatchRef);
        _settingsPanel.Controls.AddRange([cpuSwatch, cpuPick]);

        y += 48;

        // GPU row
        _settingsPanel.Controls.Add(Lbl("GPU color", _fBody, CMuted, new Point(24, y + 5)));
        gpuSwatch = Swatch(_gpuColor, y);
        var gpuSwatchRef = gpuSwatch;
        var gpuPick = Btn("Pick", new Rectangle(232, y, 64, 28));
        gpuPick.Click += (_, _) => PickColor(ref _gpuColor, gpuSwatchRef);
        _settingsPanel.Controls.AddRange([gpuSwatch, gpuPick]);

        y += 48;

        _settingsPanel.Controls.Add(Divider(y)); y += 16;

        startupCheck = new CheckBox
        {
            Text = "Start with Windows",
            Location = new Point(24, y),
            AutoSize = true,
            ForeColor = CText,
            Font = _fBody,
            Checked = IsStartupEnabled(),
        };
        _settingsPanel.Controls.Add(startupCheck);

        var saveBtn = new Button
        {
            Text = "Save",
            Bounds = new Rectangle(24, 390, 352, 36),
            FlatStyle = FlatStyle.Flat,
            Font = _fBody,
            ForeColor = Color.White,
            BackColor = CAccent,
            Cursor = Cursors.Hand,
        };
        saveBtn.FlatAppearance.BorderSize = 0;
        saveBtn.FlatAppearance.MouseOverBackColor = Color.FromArgb(0, 100, 220);
        saveBtn.Click += Save;
        _settingsPanel.Controls.Add(saveBtn);
    }

    // ── Info panel ───────────────────────────────────────────────────────────

    private void BuildInfo()
    {
        var scroll = new DoubleBufferedPanel { AutoScroll = true, Dock = DockStyle.Fill, BackColor = CBg };
        int y = 10;

        void Section(string title, Action rows)
        {
            scroll.Controls.Add(Lbl(title, _fHeader, CSection, new Point(16, y)));
            y += 22;
            rows();
            scroll.Controls.Add(Divider(y, 368));
            y += 14;
        }

        Label Row(string key)
        {
            scroll.Controls.Add(Lbl(key, _fMono, CMuted, new Point(24, y)));
            var val = Lbl("", _fMono, CText, new Point(130, y));
            val.Size = new Size(240, 16);
            scroll.Controls.Add(val);
            y += 18;
            return val;
        }

        Section("SYSTEM", () =>
        {
            _lblUser      = Row("User");
            _lblUptime    = Row("Uptime");
            _lblProcesses = Row("Processes");
            _lblScreen    = Row("Screen");
        });

        Section("CPU", () =>
        {
            _lblCpuName  = Row("Name");
            _lblCpuTemp  = Row("Temp");
            _lblCpuLoad  = Row("Load");
            _lblCpuClock = Row("Clock");
            _lblCpuPower = Row("Power");
        });

        Section("GPU", () =>
        {
            _lblGpuName = Row("Name");
            _lblGpuTemp = Row("Temp");
            _lblGpuLoad = Row("Load");
        });

        Section("MEMORY", () =>
        {
            _lblRam = Row("Used");
            _ramBar = new ProgressBar
            {
                Bounds = new Rectangle(24, y, 352, 6),
                Style = ProgressBarStyle.Continuous,
                Minimum = 0, Maximum = 100,
            };
            scroll.Controls.Add(_ramBar);
            y += 14;
        });

        Section("NETWORK", () =>
        {
            _lblNetUp   = Row("Upload");
            _lblNetDown = Row("Download");
        });

        Section("DISK", () =>
        {
            _lblDiskRead  = Row("Read");
            _lblDiskWrite = Row("Write");
        });

        Section("BATTERY", () => { _lblBattery = Row("Status"); });

        _infoPanel.Controls.Add(scroll);
    }

    // ── Stats refresh ────────────────────────────────────────────────────────

    private void RefreshStats()
    {
        var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
        _lblUser.Text      = $"{Environment.UserName} @ {Environment.MachineName}";
        _lblUptime.Text    = $"{(int)uptime.TotalDays}d  {uptime.Hours}h  {uptime.Minutes}m";
        _lblProcesses.Text = $"{Process.GetProcesses().Length}";
        var scr = Screen.PrimaryScreen;
        _lblScreen.Text    = scr != null ? $"{scr.Bounds.Width} x {scr.Bounds.Height}  @  {(int)CreateGraphics().DpiX} DPI" : "--";

        float? cpuTemp = null, cpuLoad = null, cpuClock = null, cpuPower = null, cpuMin = null, cpuMax = null;
        float? gpuTemp = null, gpuLoad = null;
        float? ramUsed = null, ramAvail = null;
        string cpuName = "--", gpuName = "--";

        foreach (var hw in _computer.Hardware)
        {
            hw.Update();
            foreach (var sub in hw.SubHardware) sub.Update();

            switch (hw.HardwareType)
            {
                case HardwareType.Cpu:
                    cpuName = hw.Name;
                    foreach (var s in hw.Sensors)
                    {
                        if (!s.Value.HasValue) continue;
                        if (s.SensorType == SensorType.Temperature && s.Name.Contains("Package", StringComparison.OrdinalIgnoreCase))
                        { cpuTemp = s.Value; cpuMin = s.Min; cpuMax = s.Max; }
                        if (s.SensorType == SensorType.Load && s.Name.Contains("Total", StringComparison.OrdinalIgnoreCase)) cpuLoad = s.Value;
                        if (s.SensorType == SensorType.Clock && s.Name.Contains("#1", StringComparison.OrdinalIgnoreCase)) cpuClock = s.Value;
                        if (s.SensorType == SensorType.Power && s.Name.Contains("Package", StringComparison.OrdinalIgnoreCase)) cpuPower = s.Value;
                    }
                    break;
                case HardwareType.GpuNvidia:
                case HardwareType.GpuAmd:
                case HardwareType.GpuIntel:
                    gpuName = hw.Name;
                    foreach (var s in hw.Sensors)
                    {
                        if (!s.Value.HasValue) continue;
                        if (s.SensorType == SensorType.Temperature && gpuTemp == null) gpuTemp = s.Value;
                        if (s.SensorType == SensorType.Load && s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase)) gpuLoad = s.Value;
                    }
                    break;
                case HardwareType.Memory:
                    foreach (var s in hw.Sensors)
                    {
                        if (!s.Value.HasValue) continue;
                        if (s.SensorType == SensorType.Data && s.Name.Contains("Used", StringComparison.OrdinalIgnoreCase) && !s.Name.Contains("Virtual")) ramUsed = s.Value;
                        if (s.SensorType == SensorType.Data && s.Name.Contains("Available", StringComparison.OrdinalIgnoreCase)) ramAvail = s.Value;
                    }
                    break;
            }
        }

        _lblCpuName.Text  = cpuName;
        _lblCpuTemp.Text  = cpuTemp.HasValue ? $"{cpuTemp:F0}°C   ↓{cpuMin:F0}   ↑{cpuMax:F0}" : "--";
        _lblCpuLoad.Text  = cpuLoad.HasValue  ? $"{cpuLoad:F0}%" : "--";
        _lblCpuClock.Text = cpuClock.HasValue ? $"{cpuClock / 1000f:F2} GHz" : "--";
        _lblCpuPower.Text = cpuPower.HasValue ? $"{cpuPower:F1} W" : "--";

        _lblGpuName.Text = gpuName;
        _lblGpuTemp.Text = gpuTemp.HasValue ? $"{gpuTemp:F0}°C" : "--";
        _lblGpuLoad.Text = gpuLoad.HasValue  ? $"{gpuLoad:F0}%" : "--";

        float? ramTotal = ramUsed.HasValue && ramAvail.HasValue ? ramUsed + ramAvail : null;
        int ramPct = ramTotal.HasValue && ramTotal > 0 ? (int)(ramUsed!.Value / ramTotal.Value * 100) : 0;
        _lblRam.Text = ramTotal.HasValue ? $"{ramUsed:F1} / {ramTotal:F1} GB  ({ramPct}%)" : "--";
        _ramBar.Value = Math.Clamp(ramPct, 0, 100);

        long sent = 0, recv = 0;
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            var s = ni.GetIPStatistics();
            sent += s.BytesSent;
            recv += s.BytesReceived;
        }
        _lblNetUp.Text   = FmtBytes(Math.Max(0, sent - _lastBytesSent)) + "/s";
        _lblNetDown.Text = FmtBytes(Math.Max(0, recv - _lastBytesRecv)) + "/s";
        _lastBytesSent = sent;
        _lastBytesRecv = recv;

        try
        {
            _lblDiskRead.Text  = FmtBytes((long)(_diskRead?.NextValue()  ?? 0)) + "/s";
            _lblDiskWrite.Text = FmtBytes((long)(_diskWrite?.NextValue() ?? 0)) + "/s";
        }
        catch { _lblDiskRead.Text = _lblDiskWrite.Text = "--"; }

        var pwr = System.Windows.Forms.SystemInformation.PowerStatus;
        _lblBattery.Text = pwr.BatteryChargeStatus == BatteryChargeStatus.NoSystemBattery || pwr.BatteryLifePercent > 1f
            ? "No battery"
            : $"{pwr.BatteryLifePercent * 100:F0}%  ({(pwr.BatteryChargeStatus.HasFlag(BatteryChargeStatus.Charging) ? "Charging" : "Discharging")})";
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private void InitPerfCounters()
    {
        try
        {
            _diskRead  = new PerformanceCounter("PhysicalDisk", "Disk Read Bytes/sec",  "_Total");
            _diskWrite = new PerformanceCounter("PhysicalDisk", "Disk Write Bytes/sec", "_Total");
            _diskRead.NextValue(); _diskWrite.NextValue();
        }
        catch { }
    }

    private void InitNetwork()
    {
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            var s = ni.GetIPStatistics();
            _lastBytesSent += s.BytesSent;
            _lastBytesRecv += s.BytesReceived;
        }
    }

    private static Label Lbl(string text, Font font, Color color, Point? loc = null)
    {
        var l = new Label { Text = text, Font = font, ForeColor = color, BackColor = Color.Transparent, AutoSize = true };
        if (loc.HasValue) l.Location = loc.Value;
        return l;
    }

    private Button Btn(string text, Rectangle bounds)
    {
        var b = new Button
        {
            Text = text, Bounds = bounds,
            FlatStyle = FlatStyle.Flat,
            Font = _fBody, ForeColor = CText, BackColor = Color.FromArgb(40, 40, 40),
            Cursor = Cursors.Hand,
        };
        b.FlatAppearance.BorderColor = CBorder;
        b.FlatAppearance.BorderSize = 1;
        b.FlatAppearance.MouseOverBackColor = Color.FromArgb(55, 55, 55);
        return b;
    }

    private static Panel Divider(int y, int width = 352) =>
        new() { Location = new Point(16, y), Size = new Size(width, 1), BackColor = CBorder };

    private static string FmtBytes(long b)
    {
        if (b >= 1_048_576) return $"{b / 1_048_576f:F1} MB";
        if (b >= 1_024)     return $"{b / 1_024f:F1} KB";
        return $"{b} B";
    }

    private void PickColor(ref Color target, Panel swatch)
    {
        using var dlg = new ColorDialog { Color = target, FullOpen = true };
        if (dlg.ShowDialog() != DialogResult.OK) return;
        target = dlg.Color;
        swatch.BackColor = target;
    }

    private void Save(object? sender, EventArgs e)
    {
        _settings.CpuColor = ColorTranslator.ToHtml(_cpuColor);
        _settings.GpuColor = ColorTranslator.ToHtml(_gpuColor);
        _settings.Save();
        SetStartup(_startupCheck.Checked);
        Close();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _statsTimer.Stop();
        _diskRead?.Dispose();
        _diskWrite?.Dispose();
        base.OnFormClosed(e);
    }

    private static bool IsStartupEnabled()
    {
        try
        {
            var p = Process.Start(new ProcessStartInfo("schtasks", "/query /tn \"TempOverlay\"") { CreateNoWindow = true, UseShellExecute = false });
            p!.WaitForExit();
            return p.ExitCode == 0;
        }
        catch { return false; }
    }

    private static void SetStartup(bool enable)
    {
        try
        {
            var args = enable
                ? $"/create /tn \"TempOverlay\" /tr \"\\\"{Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule!.FileName}\\\"\" /sc onlogon /rl highest /f"
                : "/delete /tn \"TempOverlay\" /f";
            var p = Process.Start(new ProcessStartInfo("schtasks", args) { CreateNoWindow = true, UseShellExecute = false });
            p!.WaitForExit();
        }
        catch { }
    }

    private sealed class DoubleBufferedPanel : Panel
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ShowScrollBar(IntPtr hWnd, int wBar, bool bShow);

        public DoubleBufferedPanel() => DoubleBuffered = true;

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            // Hide the vertical scrollbar after Windows shows it (WM_NCCALCSIZE / WM_SIZE)
            if (m.Msg is 0x0083 or 0x0005)
                ShowScrollBar(Handle, 1, false);
        }
    }
}
