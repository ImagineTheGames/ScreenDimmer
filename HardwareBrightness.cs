using System.Management;

namespace ScreenDimmer;

/// <summary>
/// Real backlight control: DDC/CI for external monitors, WMI for laptop panels.
/// An alpha overlay can only multiply pixel values - the backlight stays fully lit, so
/// dark content washes out to gray instead of going black. Lowering the actual backlight
/// is the only way to get real black, so dimming drives the hardware first and falls back
/// to the overlay only for whatever darkness the hardware cannot reach.
/// Brightness writes are slow (DDC/CI is an I2C round-trip to the monitor), so they run on
/// a worker thread that always collapses to the newest requested value.
/// </summary>
public sealed class HardwareBrightness : IDisposable
{
    private sealed class DdcMonitor
    {
        public IntPtr Handle;
        public uint Min;
        public uint Max;
        public uint Baseline;
    }

    private readonly List<DdcMonitor> _ddc = new();
    private readonly List<PHYSICAL_MONITOR> _owned = new();
    private byte? _wmiBaseline;

    private readonly object _lock = new();
    private readonly AutoResetEvent _signal = new(false);
    private readonly Thread _worker;
    private double _target = 1.0;
    private bool _dirty;
    private volatile bool _stop;
    private bool _disposed;

    /// <summary>True when at least one display accepts brightness commands.</summary>
    public bool IsAvailable
    {
        get { lock (_lock) return _ddc.Count > 0 || _wmiBaseline.HasValue; }
    }

    public HardwareBrightness()
    {
        Enumerate();
        _worker = new Thread(Worker) { IsBackground = true, Name = "ScreenDimmer.Brightness" };
        _worker.Start();
    }

    /// <summary>Scale brightness relative to the level the user had set when the app started. 1 = untouched, 0 = darkest the hardware allows.</summary>
    public void SetScale(double scale)
    {
        if (_disposed) return;
        scale = Math.Clamp(scale, 0.0, 1.0);
        lock (_lock)
        {
            _target = scale;
            _dirty = true;
        }
        _signal.Set();
    }

    /// <summary>Put every display back to the brightness it had when the app started.</summary>
    public void Restore()
    {
        lock (_lock)
        {
            foreach (var m in _ddc)
            {
                try { Win32Monitor.SetMonitorBrightness(m.Handle, m.Baseline); }
                catch { /* monitor unplugged or refused DDC */ }
            }
            if (_wmiBaseline.HasValue)
                TrySetWmiBrightness(_wmiBaseline.Value);
            _target = 1.0;
            _dirty = false;
        }
    }

    /// <summary>Re-enumerate after a display configuration change, then re-apply the current level.</summary>
    public void Refresh()
    {
        if (_disposed) return;
        double scale;
        lock (_lock)
        {
            ReleaseMonitors();
            Enumerate();
            scale = _target;
        }
        SetScale(scale);
    }

    private void Worker()
    {
        while (!_stop)
        {
            _signal.WaitOne();
            if (_stop) break;

            double scale;
            lock (_lock)
            {
                if (!_dirty) continue;
                scale = _target;
                _dirty = false;
            }

            Apply(scale);

            // DDC/CI writes take ~50ms each; pace them so dragging the slider cannot flood the bus.
            Thread.Sleep(80);
            lock (_lock)
            {
                if (_dirty) _signal.Set();
            }
        }
    }

    private void Apply(double scale)
    {
        lock (_lock)
        {
            foreach (var m in _ddc)
            {
                // Dim down from the user's own brightness setting, never above it.
                var span = m.Baseline > m.Min ? m.Baseline - m.Min : 0;
                var value = (uint)Math.Round(m.Min + span * scale);
                try { Win32Monitor.SetMonitorBrightness(m.Handle, Math.Clamp(value, m.Min, m.Max)); }
                catch { /* monitor unplugged or refused DDC */ }
            }
            if (_wmiBaseline.HasValue)
                TrySetWmiBrightness((byte)Math.Round(_wmiBaseline.Value * scale));
        }
    }

    private void Enumerate()
    {
        try
        {
            Win32Monitor.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, OnMonitor, IntPtr.Zero);
        }
        catch
        {
            // dxva2 missing or no DDC-capable display
        }

        // Same headroom rule for the laptop panel: brightness already at 0 means hardware dimming is a no-op.
        var wmi = TryGetWmiBrightness();
        _wmiBaseline = wmi is > 0 ? wmi : null;
    }

    private bool OnMonitor(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData)
    {
        if (!Win32Monitor.GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, out uint count) || count == 0)
            return true;

        var monitors = new PHYSICAL_MONITOR[count];
        if (!Win32Monitor.GetPhysicalMonitorsFromHMONITOR(hMonitor, count, monitors))
            return true;

        foreach (var pm in monitors)
        {
            // Usable only with real headroom: a display already sitting at its minimum has nothing left to give,
            // and claiming availability there would hand dimming to hardware that cannot dim.
            if (Win32Monitor.GetMonitorBrightness(pm.hPhysicalMonitor, out uint min, out uint cur, out uint max)
                && max > min && cur > min)
            {
                _ddc.Add(new DdcMonitor { Handle = pm.hPhysicalMonitor, Min = min, Max = max, Baseline = cur });
                _owned.Add(pm);
            }
            else
            {
                // No brightness support (or DDC/CI disabled in the monitor's own menu) - release the handle.
                try { Win32Monitor.DestroyPhysicalMonitors(1, new[] { pm }); } catch { }
            }
        }
        return true;
    }

    private void ReleaseMonitors()
    {
        if (_owned.Count > 0)
        {
            try { Win32Monitor.DestroyPhysicalMonitors((uint)_owned.Count, _owned.ToArray()); } catch { }
            _owned.Clear();
        }
        _ddc.Clear();
        _wmiBaseline = null;
    }

    private static byte? TryGetWmiBrightness()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness");
            foreach (ManagementObject obj in searcher.Get())
            {
                using (obj)
                    return Convert.ToByte(obj["CurrentBrightness"]);
            }
        }
        catch
        {
            // Not a laptop panel, or WMI brightness unsupported
        }
        return null;
    }

    private static void TrySetWmiBrightness(byte percent)
    {
        try
        {
            using var mclass = new ManagementClass(@"root\WMI", "WmiMonitorBrightnessMethods", null);
            foreach (ManagementObject instance in mclass.GetInstances())
            {
                using (instance)
                    instance.InvokeMethod("WmiSetBrightness", new object[] { (uint)1, percent });
            }
        }
        catch
        {
            // ignore - panel refused or driver missing
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Restore();

        _stop = true;
        _signal.Set();
        try { _worker.Join(1000); } catch { }

        lock (_lock) ReleaseMonitors();
        _signal.Dispose();
    }
}
