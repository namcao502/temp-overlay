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
    private string _lastCpuStr = "";
    private string _lastGpuStr = "";
    private Color _lastCpuRenderColor = Color.Empty;
    private Color _lastGpuRenderColor = Color.Empty;
    private string _lastTooltip = "";
    private Color _cpuBaseColor;
    private Color _gpuBaseColor;

    public TrayApp(Computer computer)
    {
        _settings = AppSettings.Load();
        _cpuBaseColor = _settings.GetCpuColor();
        _gpuBaseColor = _settings.GetGpuColor();

        _computer = computer;  // use the pre-opened instance from Program.cs

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
            _settingsForm.WindowState = FormWindowState.Normal;
            _settingsForm.Activate();
            return;
        }

        _settingsForm = new SettingsForm(_settings);
        _settingsForm.FormClosed += (_, _) =>
        {
            _settings = AppSettings.Load();
            _cpuBaseColor = _settings.GetCpuColor();
            _gpuBaseColor = _settings.GetGpuColor();
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

        Color cpuRender = InterpolateColor(_cpuBaseColor, cpu);
        Color gpuRender = InterpolateColor(_gpuBaseColor, gpu);

        string tooltip = $"CPU {cpuStr}°C  |  GPU {gpuStr}°C";
        if (tooltip != _lastTooltip)
        {
            _cpuIcon.Text = tooltip;
            _gpuIcon.Text = tooltip;
            _lastTooltip = tooltip;
        }

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

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    private static readonly Dictionary<int, float> _fontSizeCache = new();
    private static readonly Dictionary<float, Font> _fontCache = new();

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

        if (!_fontCache.TryGetValue(fontSize, out var font))
            _fontCache[fontSize] = font = new Font("Arial", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);

        using var brush = new SolidBrush(valueColor);
        g.DrawString(value, font, brush, new RectangleF(0, 0, w, h), sf);

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

    private void ExitApp()
    {
        _timer.Stop();
        _timer.Dispose();
        _computer.Close();
        foreach (var f in _fontCache.Values) f.Dispose();
        _fontCache.Clear();
        _cpuIcon.Visible = false;
        _gpuIcon.Visible = false;
        Application.Exit();
    }
}
