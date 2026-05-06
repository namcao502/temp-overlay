using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace TempOverlay;

static class AppIcon
{
    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    public static Icon Create()
    {
        const int S = 64;
        using var bmp = new Bitmap(S, S);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        var accent  = Color.FromArgb(0, 140, 255);
        var tubeBg  = Color.FromArgb(45, 45, 60);
        var outline = Color.FromArgb(90, 90, 120);

        int cx     = S / 2;
        int tubeW  = 14;
        int tubeX  = cx - tubeW / 2;
        int tubeTop = 5;
        int tubeBot = 43;
        int tubeH  = tubeBot - tubeTop;
        int bulbR  = 11;
        int bulbTop = tubeBot - 4;

        // Tube (rounded top)
        using var tubePath = RoundedRect(tubeX, tubeTop, tubeW, tubeH, tubeW / 2);

        using (var b = new SolidBrush(tubeBg))
            g.FillPath(b, tubePath);

        // Mercury fill (65%)
        float fill = 0.65f;
        int fillH = (int)(tubeH * fill);
        g.SetClip(tubePath);
        using (var b = new SolidBrush(accent))
            g.FillRectangle(b, tubeX, tubeBot - fillH, tubeW, fillH);
        g.ResetClip();

        using (var p = new Pen(outline, 1.5f))
            g.DrawPath(p, tubePath);

        // Tick marks
        using (var p = new Pen(Color.FromArgb(110, 110, 140), 1f))
        {
            for (int i = 1; i <= 3; i++)
            {
                int ty = tubeBot - (int)(tubeH * i / 4f);
                g.DrawLine(p, tubeX + 2, ty, tubeX + 6, ty);
            }
        }

        // Tube shine
        using (var b = new SolidBrush(Color.FromArgb(28, 255, 255, 255)))
            g.FillRectangle(b, tubeX + 3, tubeTop + 6, 3, tubeH - 14);

        // Bulb
        using (var b = new SolidBrush(accent))
            g.FillEllipse(b, cx - bulbR, bulbTop, bulbR * 2, bulbR * 2);
        using (var p = new Pen(Color.FromArgb(0, 100, 200), 1.5f))
            g.DrawEllipse(p, cx - bulbR, bulbTop, bulbR * 2, bulbR * 2);

        // Bulb shine
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
