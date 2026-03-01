using System.Drawing;

namespace ScreenDimmer;

public sealed class OverlayForm : Form
{
    private int _dimLevel;   // 0-100
    private int _colorTemp;  // 0=no filter, 100=warmest
    private bool _overlayActive = true;
    private readonly System.Windows.Forms.Timer _refreshTimer;

    public int DimLevel
    {
        get => _dimLevel;
        set
        {
            _dimLevel = Math.Clamp(value, 0, 100);
            Invalidate();
            if (IsHandleCreated && !IsDisposed) ApplyTransparency();
        }
    }

    public int ColorTemp
    {
        get => _colorTemp;
        set
        {
            _colorTemp = Math.Clamp(value, 0, 100);
            Invalidate();
            if (IsHandleCreated && !IsDisposed) ApplyTransparency();
        }
    }

    public bool OverlayActive
    {
        get => _overlayActive;
        set
        {
            _overlayActive = value;
            Invalidate();
            if (IsHandleCreated && !IsDisposed) ApplyTransparency();
        }
    }

    /// <summary>Create overlay for a specific screen (one form per monitor).</summary>
    public OverlayForm(Rectangle bounds, int dimLevel, int colorTemp)
    {
        _dimLevel = dimLevel;
        _colorTemp = colorTemp;

        FormBorderStyle = FormBorderStyle.None;
        WindowState = FormWindowState.Normal;
        TopMost = true;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.Black;
        DoubleBuffered = true;

        Bounds = bounds;
        Location = bounds.Location;

        Load += (_, _) =>
        {
            var exStyle = Win32.GetWindowLong(Handle, Win32.GWL_EXSTYLE);
            Win32.SetWindowLong(Handle, Win32.GWL_EXSTYLE, exStyle | Win32.WS_EX_LAYERED);
        };

        _refreshTimer = new System.Windows.Forms.Timer { Interval = 100 };
        _refreshTimer.Tick += (_, _) =>
        {
            if (IsHandleCreated)
                Win32.SetWindowLong(Handle, Win32.GWL_EXSTYLE,
                    Win32.GetWindowLong(Handle, Win32.GWL_EXSTYLE) | Win32.WS_EX_TRANSPARENT);
        };

        Shown += OnShown;
        HandleCreated += OnHandleCreated;
    }

    private void OnHandleCreated(object? sender, EventArgs e)
    {
        // Defer setting click-through until window is fully shown
        BeginInvoke(() => _refreshTimer.Start());
    }

    private void OnShown(object? sender, EventArgs e)
    {
        // Apply initial transparency and click-through
        ApplyTransparency();
        Win32.SetWindowLong(Handle, Win32.GWL_EXSTYLE,
            Win32.GetWindowLong(Handle, Win32.GWL_EXSTYLE) | Win32.WS_EX_TRANSPARENT);
        // Re-apply after short delay when started with Windows (display/DWM may not be ready yet)
        if (Environment.TickCount64 < 120_000)
        {
            var t = new System.Windows.Forms.Timer { Interval = 800 };
            t.Tick += (_, _) => { t.Stop(); t.Dispose(); ApplyTransparency(); };
            t.Start();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        ApplyTransparency();
    }

    private void ApplyTransparency()
    {
        if (!IsHandleCreated || IsDisposed) return;

        // Must be layered for SetLayeredWindowAttributes to take effect (required at startup / before first paint).
        var exStyle = Win32.GetWindowLong(Handle, Win32.GWL_EXSTYLE);
        if ((exStyle & Win32.WS_EX_LAYERED) == 0)
            Win32.SetWindowLong(Handle, Win32.GWL_EXSTYLE, exStyle | Win32.WS_EX_LAYERED);

        if (!_overlayActive)
        {
            Win32.SetLayeredWindowAttributes(Handle, 0, 0, Win32.LWA_ALPHA);
            return;
        }

        byte alpha;
        if (_dimLevel > 0)
        {
            // Dimming on: alpha from dim level (0–220 for darkness)
            alpha = (byte)((_dimLevel / 100.0) * 220);
        }
        else
        {
            // Dimming off: color temp still works - 0=none, 100=warmest
            if (_colorTemp <= 0)
                alpha = 0;
            else
                alpha = (byte)(10 + (_colorTemp / 100.0) * 45);
        }

        Win32.SetLayeredWindowAttributes(Handle, 0, alpha, Win32.LWA_ALPHA);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // Solid overlay color - SetLayeredWindowAttributes controls opacity
        Color baseColor = GetOverlayColor();
        using var brush = new SolidBrush(baseColor);
        e.Graphics?.FillRectangle(brush, e.ClipRectangle);
    }

    private Color GetOverlayColor()
    {
        // Overlay must be DARK to dim (low RGB = blocks light). Color adds a subtle warm hue to the darkness.
        // 0=pure black, 100=dark warm (amber bias) - always dark so dimming actually reduces brightness.
        if (_colorTemp <= 0)
            return Color.FromArgb(255, 0, 0, 0);
        double t = _colorTemp / 100.0;
        int r = (int)(28 * t);   // dark overlay - dimming works; warm is subtle hue
        int g = (int)(18 * t);
        int b = (int)(6 * t);
        return Color.FromArgb(255, Math.Min(r, 255), Math.Min(g, 255), Math.Min(b, 255));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _refreshTimer?.Stop();
            _refreshTimer?.Dispose();
        }
        base.Dispose(disposing);
    }
}
