using System.Diagnostics;

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

    // Fonts
    private readonly Font _fTitle = new("Segoe UI", 11f, FontStyle.Bold);
    private readonly Font _fBody  = new("Segoe UI", 8.5f);

    // State
    private readonly AppSettings _settings;
    private Color _cpuColor;
    private Color _gpuColor;

    // Settings tab
    private readonly Panel _cpuSwatch;
    private readonly Panel _gpuSwatch;
    private readonly CheckBox _startupCheck;
    private PictureBox _cpuPreview = null!;
    private PictureBox _gpuPreview = null!;

    private readonly Panel _settingsPanel;

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

        if (!_settings.StartWithWindows.HasValue)
        {
            _settings.StartWithWindows = _startupCheck.Checked;
            _settings.Save();
        }

        Controls.AddRange([titleBar, _settingsPanel]);
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
        _cpuPreview = new PictureBox { Location = new Point(308, y + 6), Size = new Size(16, 16), SizeMode = PictureBoxSizeMode.StretchImage };
        RefreshPreview(_cpuPreview, _cpuColor);
        var cpuPreviewRef = _cpuPreview;
        cpuPick.Click += (_, _) =>
        {
            PickColor(ref _cpuColor, cpuSwatchRef);
            RefreshPreview(cpuPreviewRef, _cpuColor);
        };
        _settingsPanel.Controls.AddRange([cpuSwatch, cpuPick, _cpuPreview]);

        y += 48;

        // GPU row
        _settingsPanel.Controls.Add(Lbl("GPU color", _fBody, CMuted, new Point(24, y + 5)));
        gpuSwatch = Swatch(_gpuColor, y);
        var gpuSwatchRef = gpuSwatch;
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

        y += 48;

        _settingsPanel.Controls.Add(Divider(y)); y += 16;

        startupCheck = new CheckBox
        {
            Text = "Start with Windows",
            Location = new Point(24, y),
            AutoSize = true,
            ForeColor = CText,
            Font = _fBody,
            Checked = _settings.StartWithWindows ?? IsStartupEnabled(),
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

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static void RefreshPreview(PictureBox box, Color color)
    {
        var old = box.Image;
        using var icon = TrayApp.DrawIcon("75", color);
        box.Image = icon.ToBitmap();
        old?.Dispose();
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
        _settings.StartWithWindows = _startupCheck.Checked;
        _settings.Save();
        SetStartup(_startupCheck.Checked);
        Close();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _fTitle.Dispose();
            _fBody.Dispose();
            _cpuPreview?.Image?.Dispose();
            _gpuPreview?.Image?.Dispose();
        }
        base.Dispose(disposing);
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
}
