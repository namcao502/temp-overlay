using System.Diagnostics;
using LibreHardwareMonitor.Hardware;

namespace TempOverlay;

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();

        if (!IsDriverInstalled())
        {
            ShowDriverMissingDialog();
            return;
        }

        Application.Run(new TrayApp());
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
            Text = "⚠",
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

    private static bool IsDriverInstalled()
    {
        try
        {
            var computer = new Computer { IsCpuEnabled = true };
            computer.Open();

            foreach (var hw in computer.Hardware)
            {
                hw.Update();
                foreach (var s in hw.Sensors)
                    if (s.SensorType == SensorType.Temperature && s.Value.HasValue)
                    {
                        computer.Close();
                        return true;
                    }
            }

            computer.Close();
            return false;
        }
        catch
        {
            return false;
        }
    }
}
