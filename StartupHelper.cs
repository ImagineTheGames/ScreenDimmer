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
                // Must use path to ScreenDimmer.exe; ProcessPath can be dotnet.exe when run via dotnet run/IDE
                var exePath = GetExePath();
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

    /// <summary>
    /// Resolves the full path to ScreenDimmer.exe. When run via dotnet run/IDE, ProcessPath points to dotnet.exe,
    /// so we always resolve to the actual exe in the app's output directory for correct startup registration.
    /// </summary>
    internal static string GetExePath()
    {
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(processPath) &&
            processPath.EndsWith("ScreenDimmer.exe", StringComparison.OrdinalIgnoreCase) &&
            File.Exists(processPath))
            return Path.GetFullPath(processPath);

        var exePath = Path.Combine(AppContext.BaseDirectory, "ScreenDimmer.exe");
        return Path.GetFullPath(File.Exists(exePath) ? exePath : processPath ?? exePath);
    }
}
