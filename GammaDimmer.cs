namespace ScreenDimmer;

/// <summary>
/// Crushes midtones through the GPU output gamma ramp: out = in^gamma, with the white point left
/// at full. Input 0 maps to output 0, so black stays black, and everything between falls far faster
/// than the overlay's flat veil (out = in * (1 - alpha)) which lightens midtones into gray.
/// The overlay still sets the white point; this only reshapes the curve under it.
///
/// Windows clamps gamma ramp writes and the limits were measured on this machine:
///   scale 1.00 + gamma up to 3.0  -> accepted
///   scale below 0.50 (any gamma)  -> rejected
///   scale 0.50 + gamma 3.0        -> rejected
/// So the ramp holds scale at 1.0 and spends its whole budget on the exponent; pulling the white
/// point down is the overlay's job, where Windows does not interfere.
/// </summary>
public sealed class GammaDimmer : IDisposable
{
    /// <summary>
    /// Extra gamma exponent at the darkest setting - this is what crushes the gray midtones.
    /// Kept under the measured rejection threshold (3.0 at full scale).
    /// </summary>
    private const double MaxGammaBoost = 1.9;

    /// <summary>
    /// Warm tint at the warmest setting, as per-channel white points. Kept above the measured 0.50
    /// rejection floor. Doing the tint here instead of colouring the overlay matters: a warm overlay
    /// adds its own colour to every pixel and lifts black off zero, while a per-channel ramp still
    /// maps 0 to 0 on all three channels.
    /// </summary>
    private const double WarmestGreen = 0.82;
    private const double WarmestBlue = 0.58;

    private readonly List<string> _devices = new();
    private readonly ushort[] _ramp = new ushort[768];
    private ushort[]? _original;
    private double _level;   // 0 = untouched, 1 = darkest
    private double _warmth;  // 0 = neutral, 1 = warmest
    private bool _available;
    private bool _disposed;

    /// <summary>True when the GPU accepted a ramp write (drivers may refuse or clamp them).</summary>
    public bool IsAvailable => _available;

    public GammaDimmer()
    {
        Enumerate();
        _available = Probe();
    }

    /// <summary>0 = linear ramp, 1 = hardest midtone crush. Black stays black at every level.</summary>
    public void SetLevel(double level)
    {
        if (_disposed) return;
        _level = Math.Clamp(level, 0.0, 1.0);
        Apply();
    }

    /// <summary>0 = neutral, 1 = warmest. Pulls green and blue white points down; black stays black.</summary>
    public void SetWarmth(double warmth)
    {
        if (_disposed) return;
        _warmth = Math.Clamp(warmth, 0.0, 1.0);
        Apply();
    }

    /// <summary>Re-send the current ramp. Windows resets ramps on mode changes, resume and session switches.</summary>
    public void Reapply()
    {
        if (_disposed) return;
        Apply();
    }

    /// <summary>Re-enumerate displays after a configuration change, then re-apply.</summary>
    public void Refresh()
    {
        if (_disposed) return;
        Enumerate();
        Apply();
    }

    /// <summary>Hand the display pipeline back to Windows.</summary>
    public void Restore()
    {
        if (_disposed) return;
        _level = 0;
        if (_original != null)
            WriteToAllDevices(_original);
        else
            Apply();   // _level is 0, so this writes the identity ramp
    }

    private void Apply()
    {
        if (!_available) return;

        // Red white point stays at 1.0 - Windows rejects ramps that dim it, and the overlay sets the
        // overall white point anyway. Green and blue come down only as far as the warm tint needs.
        var gamma = 1.0 + _level * MaxGammaBoost;
        var greenScale = 1.0 - _warmth * (1.0 - WarmestGreen);
        var blueScale = 1.0 - _warmth * (1.0 - WarmestBlue);

        for (int i = 0; i < 256; i++)
        {
            var curve = Math.Pow(i / 255.0, gamma) * 65535.0;
            _ramp[i] = Scale(curve, 1.0);
            _ramp[256 + i] = Scale(curve, greenScale);
            _ramp[512 + i] = Scale(curve, blueScale);
        }

        WriteToAllDevices(_ramp);
    }

    private static ushort Scale(double curve, double channelScale) =>
        (ushort)Math.Clamp(Math.Round(curve * channelScale), 0, 65535);

    private void WriteToAllDevices(ushort[] ramp)
    {
        if (_devices.Count == 0)
        {
            var dc = Win32Gamma.GetDC(IntPtr.Zero);
            if (dc == IntPtr.Zero) return;
            try { Win32Gamma.SetDeviceGammaRamp(dc, ramp); }
            finally { Win32Gamma.ReleaseDC(IntPtr.Zero, dc); }
            return;
        }

        foreach (var device in _devices)
        {
            var dc = Win32Gamma.CreateDC(device, device, null, IntPtr.Zero);
            if (dc == IntPtr.Zero) continue;
            try { Win32Gamma.SetDeviceGammaRamp(dc, ramp); }
            finally { Win32Gamma.DeleteDC(dc); }
        }
    }

    private void Enumerate()
    {
        _devices.Clear();
        try
        {
            for (uint i = 0; ; i++)
            {
                var dd = new DISPLAY_DEVICE();
                dd.cb = (uint)System.Runtime.InteropServices.Marshal.SizeOf<DISPLAY_DEVICE>();
                if (!Win32Gamma.EnumDisplayDevices(null, i, ref dd, 0))
                    break;
                if ((dd.StateFlags & Win32Gamma.DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) != 0)
                    _devices.Add(dd.DeviceName);
            }
        }
        catch
        {
            // fall back to the primary DC in WriteToAllDevices
        }
    }

    /// <summary>Snapshot the current ramp and confirm the driver actually honours a write.</summary>
    private bool Probe()
    {
        var dc = Win32Gamma.GetDC(IntPtr.Zero);
        if (dc == IntPtr.Zero) return false;
        try
        {
            var original = new ushort[768];
            if (Win32Gamma.GetDeviceGammaRamp(dc, original))
                _original = original;

            // Identity ramp: visually a no-op, but it proves the driver accepts ramp writes.
            var identity = new ushort[768];
            for (int i = 0; i < 256; i++)
            {
                var v = (ushort)(i * 257);
                identity[i] = v;
                identity[256 + i] = v;
                identity[512 + i] = v;
            }
            return Win32Gamma.SetDeviceGammaRamp(dc, identity);
        }
        catch
        {
            return false;
        }
        finally
        {
            Win32Gamma.ReleaseDC(IntPtr.Zero, dc);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        Restore();
        _disposed = true;
    }
}
