using System.Threading;

namespace ScreenDimmer;

static class Program
{
    private static Mutex? _mutex;

    [STAThread]
    static void Main()
    {
        const string mutexName = "ScreenDimmer_SingleInstance";
        _mutex = new Mutex(true, mutexName, out var createdNew);
        if (!createdNew)
        {
            return; // Another instance is already running
        }

        ApplicationConfiguration.Initialize();

        var settings = AppSettings.Load();
        var overlay = new OverlayForm(settings.DimLevel, settings.ColorTemp) { OverlayActive = settings.OverlayEnabled };
        var tray = new TrayManager(overlay, settings);

        overlay.Show();

        Application.Run();

        tray.Dispose();
        overlay.Dispose();
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
    }
}
