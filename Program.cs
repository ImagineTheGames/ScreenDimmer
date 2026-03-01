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

        // When started with Windows, display may not be ready. Delay to let DWM/explorer initialize.
        if (Environment.TickCount64 < 120_000) // System up < 2 min = likely startup
        {
            Thread.Sleep(4000);
        }

        var settings = AppSettings.Load();
        var overlayManager = new OverlayManager(settings.DimLevel, settings.ColorTemp, settings.OverlayEnabled);
        var tray = new TrayManager(overlayManager, settings);

        overlayManager.Show();

        Application.Run();

        tray.Dispose();
        overlayManager.Dispose();
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
    }
}
