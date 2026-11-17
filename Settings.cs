using System.Text.Json;

namespace ScreenDimmer;

public sealed class AppSettings
{
    public int DimLevel { get; set; } = 0;  // 0-100
    public int ColorTemp { get; set; } = 0;  // 0=no filter, 100=warmest
    public bool OverlayEnabled { get; set; } = true;  // quick toggle for dim + color
    public bool UseHardwareBrightness { get; set; } = true;  // dim the real backlight (DDC/CI or laptop panel) before falling back to the overlay
    public bool StartOnStartup { get; set; } = false;

    private static string SettingsPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ScreenDimmer",
            "settings.json");

    public static AppSettings Load()
    {
        try
        {
            var path = SettingsPath;
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                if (loaded != null)
                    return loaded;
            }
        }
        catch
        {
            // ignore
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            var path = SettingsPath;
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch
        {
            // ignore
        }
    }
}
