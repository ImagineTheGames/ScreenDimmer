using System.Drawing;
using Microsoft.Win32;

namespace ScreenDimmer;

/// <summary>
/// Manages one overlay form per physical screen so every monitor is dimmed.
/// Recreates overlays when display configuration changes.
/// </summary>
public sealed class OverlayManager : IDisposable
{
    private readonly List<OverlayForm> _overlays = new();
    private readonly int _dimLevel;
    private readonly int _colorTemp;
    private bool _overlayActive;
    private bool _disposed;

    public int DimLevel
    {
        get => _overlays.Count > 0 ? _overlays[0].DimLevel : _dimLevel;
        set { foreach (var o in _overlays) o.DimLevel = value; }
    }

    public int ColorTemp
    {
        get => _overlays.Count > 0 ? _overlays[0].ColorTemp : _colorTemp;
        set { foreach (var o in _overlays) o.ColorTemp = value; }
    }

    public bool OverlayActive
    {
        get => _overlays.Count > 0 ? _overlays[0].OverlayActive : _overlayActive;
        set
        {
            _overlayActive = value;
            foreach (var o in _overlays) o.OverlayActive = value;
        }
    }

    public OverlayManager(int dimLevel, int colorTemp, bool overlayActive)
    {
        _dimLevel = dimLevel;
        _colorTemp = colorTemp;
        _overlayActive = overlayActive;
        CreateOverlays(_dimLevel, _colorTemp, _overlayActive);
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    private void CreateOverlays(int dimLevel, int colorTemp, bool overlayActive)
    {
        foreach (var screen in Screen.AllScreens)
        {
            var overlay = new OverlayForm(screen.Bounds, dimLevel, colorTemp)
            {
                OverlayActive = overlayActive
            };
            _overlays.Add(overlay);
        }
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        if (_disposed || _overlays.Count == 0) return;
        var first = _overlays[0];
        if (first.IsHandleCreated && !first.IsDisposed)
            first.BeginInvoke(RecreateOverlays);
    }

    private void RecreateOverlays()
    {
        var dim = _overlays.Count > 0 ? _overlays[0].DimLevel : _dimLevel;
        var color = _overlays.Count > 0 ? _overlays[0].ColorTemp : _colorTemp;
        var active = _overlays.Count > 0 ? _overlays[0].OverlayActive : _overlayActive;
        foreach (var o in _overlays)
            o.Dispose();
        _overlays.Clear();
        CreateOverlays(dim, color, active);
        foreach (var o in _overlays)
            o.Show();
    }

    public void Show()
    {
        foreach (var o in _overlays)
            o.Show();
    }

    public void Dispose()
    {
        if (_disposed) return;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        foreach (var o in _overlays)
            o.Dispose();
        _overlays.Clear();
        _disposed = true;
    }
}
