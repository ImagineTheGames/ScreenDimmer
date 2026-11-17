using System.Drawing;
using Microsoft.Win32;

namespace ScreenDimmer;

/// <summary>
/// Drives dimming across three cooperating tiers:
/// 1. hardware backlight (DDC/CI monitors, laptop panels) - removes real light, used first where
///    the display still has brightness headroom;
/// 2. GPU gamma ramp - crushes midtones with an exponent and pins black to black;
/// 3. the translucent overlay - sets the white point and tints for color temperature.
/// Tiers 2 and 3 run together on purpose. The overlay alone can only do out = in * (1 - alpha),
/// a flat veil that lightens midtones into gray; the ramp alone cannot lower the white point because
/// Windows rejects such ramps. Combined, midtones drop far below the veil and black stays black.
/// One overlay form per physical screen; all tiers are re-applied when the display configuration
/// changes.
/// </summary>
public sealed class OverlayManager : IDisposable
{
    /// <summary>
    /// Share of the dim slider handled by the hardware backlight before the gamma ramp takes over.
    /// Only used when a display actually has brightness headroom left.
    /// </summary>
    private const int HardwareSpan = 40;

    private readonly List<OverlayForm> _overlays = new();
    private readonly HardwareBrightness _hardware = new();
    private readonly GammaDimmer _gamma = new();
    private readonly System.Windows.Forms.Timer _gammaWatchdog;
    private int _dimLevel;
    private int _colorTemp;
    private bool _overlayActive;
    private bool _useHardware;
    private bool _disposed;

    public int DimLevel
    {
        get => _dimLevel;
        set
        {
            _dimLevel = Math.Clamp(value, 0, 100);
            ApplyDim();
        }
    }

    public int ColorTemp
    {
        get => _colorTemp;
        set
        {
            _colorTemp = Math.Clamp(value, 0, 100);
            // Tint through the ramp where possible: a coloured overlay would lift black off zero.
            if (_gamma.IsAvailable)
            {
                _gamma.SetWarmth(_colorTemp / 100.0);
                foreach (var o in _overlays) o.ColorTemp = 0;
            }
            else
            {
                foreach (var o in _overlays) o.ColorTemp = _colorTemp;
            }
        }
    }

    public bool OverlayActive
    {
        get => _overlayActive;
        set
        {
            _overlayActive = value;
            foreach (var o in _overlays) o.OverlayActive = value;
            ApplyDim();
        }
    }

    /// <summary>Use the real backlight as the first dimming tier. Off = gamma/overlay only.</summary>
    public bool UseHardwareBrightness
    {
        get => _useHardware;
        set
        {
            _useHardware = value;
            if (!value) _hardware.Restore();
            ApplyDim();
        }
    }

    /// <summary>True when at least one display has brightness headroom (DDC/CI or laptop panel).</summary>
    public bool HardwareAvailable => _hardware.IsAvailable;

    /// <summary>True when the GPU accepts gamma ramps, i.e. dimming keeps blacks black.</summary>
    public bool GammaAvailable => _gamma.IsAvailable;

    public OverlayManager(int dimLevel, int colorTemp, bool overlayActive, bool useHardwareBrightness)
    {
        _dimLevel = Math.Clamp(dimLevel, 0, 100);
        _colorTemp = Math.Clamp(colorTemp, 0, 100);
        _overlayActive = overlayActive;
        _useHardware = useHardwareBrightness;

        CreateOverlays(OverlayDimFor(_dimLevel), _gamma.IsAvailable ? 0 : _colorTemp, _overlayActive);
        if (_gamma.IsAvailable) _gamma.SetWarmth(_colorTemp / 100.0);
        ApplyDim();

        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;

        // Windows drops gamma ramps on its own (resume, fast user switching, some fullscreen apps),
        // so re-assert the ramp periodically instead of silently losing the dimming.
        _gammaWatchdog = new System.Windows.Forms.Timer { Interval = 3000 };
        _gammaWatchdog.Tick += (_, _) =>
        {
            if (!_disposed && _overlayActive && _dimLevel > 0 && _gamma.IsAvailable)
                _gamma.Reapply();
        };
        _gammaWatchdog.Start();
    }

    private bool HardwareUsable => _useHardware && _hardware.IsAvailable;

    /// <summary>Slider range given to the backlight before the next tier starts.</summary>
    private int HardwarePortion => HardwareUsable ? HardwareSpan : 0;

    /// <summary>
    /// Overlay opacity - this sets the white point. Windows rejects gamma ramps that pull the white
    /// point down, so the overlay keeps that job while the ramp crushes everything below it.
    /// </summary>
    private int OverlayDimFor(int dimLevel)
    {
        var span = HardwarePortion;
        if (dimLevel <= span)
            return 0;
        return (int)Math.Round((dimLevel - span) * 100.0 / (100 - span));
    }

    private void ApplyDim()
    {
        if (_disposed) return;

        var span = HardwarePortion;

        if (_hardware.IsAvailable)
        {
            // Dimming off, or hardware tier disabled: give the backlight back to the user.
            if (!_overlayActive || !_useHardware)
                _hardware.SetScale(1.0);
            else
                _hardware.SetScale(1.0 - Math.Min(_dimLevel, span) / (double)span);
        }

        if (_gamma.IsAvailable)
        {
            _gamma.SetWarmth(_overlayActive ? _colorTemp / 100.0 : 0);
            if (!_overlayActive || _dimLevel <= span)
                _gamma.SetLevel(0);
            else
                _gamma.SetLevel((_dimLevel - span) / (double)(100 - span));
        }

        var overlayDim = OverlayDimFor(_dimLevel);
        foreach (var o in _overlays) o.DimLevel = overlayDim;
    }

    private void CreateOverlays(int overlayDim, int colorTemp, bool overlayActive)
    {
        foreach (var screen in Screen.AllScreens)
        {
            var overlay = new OverlayForm(screen.Bounds, overlayDim, colorTemp)
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

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (_disposed || e.Mode != PowerModes.Resume) return;
        ApplyDim();
    }

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        if (_disposed) return;
        if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.ConsoleConnect)
            ApplyDim();
    }

    private void RecreateOverlays()
    {
        foreach (var o in _overlays)
            o.Dispose();
        _overlays.Clear();
        // Monitors may have been added or removed - re-enumerate both hardware tiers.
        _hardware.Refresh();
        _gamma.Refresh();
        CreateOverlays(OverlayDimFor(_dimLevel), _gamma.IsAvailable ? 0 : _colorTemp, _overlayActive);
        if (_gamma.IsAvailable) _gamma.SetWarmth(_colorTemp / 100.0);
        foreach (var o in _overlays)
            o.Show();
        ApplyDim();
    }

    public void Show()
    {
        foreach (var o in _overlays)
            o.Show();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        _gammaWatchdog.Stop();
        _gammaWatchdog.Dispose();
        foreach (var o in _overlays)
            o.Dispose();
        _overlays.Clear();
        _gamma.Dispose();
        _hardware.Dispose();
    }
}
