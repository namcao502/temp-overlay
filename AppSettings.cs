using System.Text.Json;

namespace TempOverlay;

public class AppSettings
{
    public string CpuColor { get; set; } = "#0071C5";
    public string GpuColor { get; set; } = "#76B900";
    public bool? StartWithWindows { get; set; } = null;

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TempOverlay", "settings.json");

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

    public void Save()
    {
        var dir = Path.GetDirectoryName(SettingsPath)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }

    public Color GetCpuColor() => ParseColor(CpuColor, Color.FromArgb(0, 113, 197));
    public Color GetGpuColor() => ParseColor(GpuColor, Color.FromArgb(118, 185, 0));

    private static Color ParseColor(string hex, Color fallback)
    {
        try { return ColorTranslator.FromHtml(hex); }
        catch { return fallback; }
    }
}
