using Microsoft.Win32;

namespace ScreenDimmer;

public static class StartupHelper
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ScreenDimmer";

    public static bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(KeyPath, false);
                return !string.IsNullOrEmpty(key?.GetValue(ValueName) as string);
            }
            catch
            {
                return false;
            }
        }
    }

    public static void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath, true);
            if (key == null) return;

            if (enabled)
            {
                var exePath = Environment.ProcessPath
                    ?? Environment.GetCommandLineArgs().FirstOrDefault()
                    ?? Path.Combine(AppContext.BaseDirectory, "ScreenDimmer.exe");
                if (exePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    exePath = Path.ChangeExtension(exePath, ".exe");
                key.SetValue(ValueName, $"\"{exePath}\"");
            }
            else
            {
                key.DeleteValue(ValueName, false);
            }
        }
        catch
        {
            // ignore
        }
    }
}
