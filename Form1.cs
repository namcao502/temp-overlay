using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using LibreHardwareMonitor.Hardware;

namespace TempOverlay;

public class TrayApp : ApplicationContext
{
    private readonly NotifyIcon _cpuIcon;
    private readonly NotifyIcon _gpuIcon;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly Computer _computer;
    private AppSettings _settings;
    private SettingsForm? _settingsForm;

    public TrayApp()
    {
        _settings = AppSettings.Load();

        _computer = new Computer { IsCpuEnabled = true, IsGpuEnabled = true, IsMemoryEnabled = true };
        _computer.Open();

        var menu = new ContextMenuStrip();
        menu.Items.Add("Settings", null, (_, _) => OpenSettings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApp());

        _cpuIcon = new NotifyIcon { ContextMenuStrip = menu, Visible = true };
        _gpuIcon = new NotifyIcon { ContextMenuStrip = menu, Visible = true };

        _cpuIcon.DoubleClick += (_, _) => OpenSettings();
        _gpuIcon.DoubleClick += (_, _) => OpenSettings();

        _timer = new System.Windows.Forms.Timer { Interval = 1000 };
        _timer.Tick += (_, _) => UpdateTray();
        _timer.Start();

        UpdateTray();
    }

    private void OpenSettings()
    {
        if (_settingsForm != null)
        {
            _settingsForm.BringToFront();
            return;
        }

        _settingsForm = new SettingsForm(_settings, _computer);
        _settingsForm.FormClosed += (_, _) =>
        {
            _settings = AppSettings.Load();
            _settingsForm = null;
        };
        _settingsForm.Show();
    }

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

        _cpuIcon.Icon?.Dispose();
        _cpuIcon.Icon = DrawIcon(cpuStr, _settings.GetCpuColor());
        _cpuIcon.Text = $"CPU {cpuStr}C";

        _gpuIcon.Icon?.Dispose();
        _gpuIcon.Icon = DrawIcon(gpuStr, _settings.GetGpuColor());
        _gpuIcon.Text = $"GPU {gpuStr}C";
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    private static Icon DrawIcon(string value, Color valueColor)
    {
        const int w = 32;
        const int h = 32;

        using var bmp = new Bitmap(w, h);
        using var g = Graphics.FromImage(bmp);
        using var sf = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
        };

        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        using var brush = new SolidBrush(valueColor);
        float fontSize = 28f;
        while (fontSize > 6f)
        {
            using var font = new Font("Arial", fontSize, FontStyle.Bold);
            var size = g.MeasureString(value, font);
            if (size.Width <= w && size.Height <= h) { g.DrawString(value, font, brush, new RectangleF(0, 0, w, h), sf); break; }
            fontSize -= 1f;
        }

        var hIcon = bmp.GetHicon();
        try { return (Icon)Icon.FromHandle(hIcon).Clone(); }
        finally { DestroyIcon(hIcon); }
    }

    private static float? ReadPackageTemp(IHardware hw)
    {
        foreach (var s in hw.Sensors)
            if (s.SensorType == SensorType.Temperature &&
                s.Name.Contains("Package", StringComparison.OrdinalIgnoreCase) &&
                s.Value.HasValue)
                return s.Value;
        return null;
    }

    private static float? ReadFirstTemp(IHardware hw)
    {
        foreach (var s in hw.Sensors)
            if (s.SensorType == SensorType.Temperature && s.Value.HasValue)
                return s.Value;
        return null;
    }

    private static float? ReadFirstTempDeep(IHardware hw)
    {
        foreach (var sub in hw.SubHardware)
        {
            var t = ReadFirstTemp(sub);
            if (t.HasValue) return t;
        }
        return null;
    }

    private void ExitApp()
    {
        _timer.Stop();
        _computer.Close();
        _cpuIcon.Visible = false;
        _gpuIcon.Visible = false;
        Application.Exit();
    }
}
