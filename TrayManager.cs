using System.Drawing;
using System.Runtime.InteropServices;

namespace ScreenDimmer;

public sealed class TrayManager : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _contextMenu;
    private readonly OverlayManager _overlay;
    private readonly AppSettings _settings;
    private readonly MenuOutsideClickHook _outsideClickHook;

    public TrayManager(OverlayManager overlay, AppSettings settings)
    {
        _overlay = overlay;
        _settings = settings;

        _notifyIcon = new NotifyIcon
        {
            Text = "Screen Dimmer",
            Visible = true
        };

        try
        {
            Icon? icon = null;
            // Try embedded resource first (always available)
            var asm = typeof(TrayManager).Assembly;
            var resourceName = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("app.ico", StringComparison.OrdinalIgnoreCase));
            if (resourceName != null)
            {
                using var stream = asm.GetManifestResourceStream(resourceName);
                if (stream != null)
                    icon = new Icon(stream);
            }
            if (icon == null)
            {
                var icoPath = Path.Combine(AppContext.BaseDirectory, "app.ico");
                if (File.Exists(icoPath))
                    icon = new Icon(icoPath);
            }
            if (icon == null)
            {
                var exePath = Environment.ProcessPath ?? Application.ExecutablePath;
                if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
                    icon = Icon.ExtractAssociatedIcon(exePath);
            }
            _notifyIcon.Icon = icon ?? SystemIcons.Application;
        }
        catch
        {
            _notifyIcon.Icon = SystemIcons.Application;
        }

        _contextMenu = BuildContextMenu();
        _outsideClickHook = new MenuOutsideClickHook(_contextMenu);
        _contextMenu.Closing += OnContextMenuClosing;
        _contextMenu.Closed += OnContextMenuClosed;
        _contextMenu.Opening += (_, _) =>
        {
            _overlayCheckboxJustClicked = false;
            _startupCheckboxJustClicked = false;
            if (_dimTrack != null) _dimTrack.Value = _overlay.DimLevel;
            if (_colorTrack != null) _colorTrack.Value = _overlay.ColorTemp;
            UpdateOverlayCheckBoxState();
            UpdateStartupCheckBoxState();
            _outsideClickHook.Install();
        };
        _notifyIcon.ContextMenuStrip = _contextMenu;

    }

    private TrackBar? _dimTrack;
    private TrackBar? _colorTrack;
    private CheckBox? _overlayCheckBox;
    private CheckBox? _startupCheckBox;
    private bool _overlayCheckboxJustClicked;
    private bool _startupCheckboxJustClicked;
    private bool _reopenMenuAfterCheckboxClick;

    private void OnContextMenuClosing(object? sender, ToolStripDropDownClosingEventArgs e)
    {
        // Don't cancel: closing then reopening avoids breaking "click outside to close" (cancel leaves the dropdown stuck).
        if (e.CloseReason == ToolStripDropDownCloseReason.ItemClicked &&
            (_overlayCheckboxJustClicked || _startupCheckboxJustClicked))
            _reopenMenuAfterCheckboxClick = true;
        _overlayCheckboxJustClicked = false;
        _startupCheckboxJustClicked = false;
    }

    private void OnContextMenuClosed(object? sender, ToolStripDropDownClosedEventArgs e)
    {
        _outsideClickHook.Uninstall();
        if (_reopenMenuAfterCheckboxClick)
        {
            _reopenMenuAfterCheckboxClick = false;
            var pos = Cursor.Position;
            _contextMenu.BeginInvoke(() =>
            {
                _contextMenu.Show(pos);
                _outsideClickHook.Install();
            });
        }
    }

    private void UpdateOverlayCheckBoxState()
    {
        if (_overlayCheckBox != null)
        {
            _overlayCheckBox.Checked = _overlay.OverlayActive;
            _settings.OverlayEnabled = _overlay.OverlayActive;
        }
    }

    private void UpdateStartupCheckBoxState()
    {
        if (_startupCheckBox != null)
        {
            _startupCheckBox.Checked = StartupHelper.IsEnabled || _settings.StartOnStartup;
            _settings.StartOnStartup = _startupCheckBox.Checked;
        }
    }

    private ContextMenuStrip BuildContextMenu()
    {
        var menu = new ContextMenuStrip { Width = 280 };

        var dimPanel = new Panel { Width = 260, Height = 70 };
        var dimLabel = new Label { Text = "Dimming:", Location = new Point(8, 4), AutoSize = true };
        _dimTrack = new TrackBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = _overlay.DimLevel,
            Location = new Point(8, 24),
            Width = 240,
            TickFrequency = 25
        };
        _dimTrack.ValueChanged += (_, _) =>
        {
            _overlay.DimLevel = _dimTrack!.Value;
            _settings.DimLevel = _dimTrack.Value;
            _settings.Save();
        };
        dimPanel.Controls.Add(dimLabel);
        dimPanel.Controls.Add(_dimTrack);
        menu.Items.Add(new ToolStripControlHost(dimPanel) { Margin = Padding.Empty, Padding = new Padding(8, 6, 8, 6) });

        var colorPanel = new Panel { Width = 260, Height = 70 };
        var colorLabel = new Label { Text = "Color (None → Warmest):", Location = new Point(8, 4), AutoSize = true };
        _colorTrack = new TrackBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = _overlay.ColorTemp,
            Location = new Point(8, 24),
            Width = 240,
            TickFrequency = 17
        };
        _colorTrack.ValueChanged += (_, _) =>
        {
            _overlay.ColorTemp = _colorTrack!.Value;
            _settings.ColorTemp = _colorTrack.Value;
            _settings.Save();
        };
        colorPanel.Controls.Add(colorLabel);
        colorPanel.Controls.Add(_colorTrack);
        menu.Items.Add(new ToolStripControlHost(colorPanel) { Margin = Padding.Empty, Padding = new Padding(8, 6, 8, 6) });

        menu.Items.Add(new ToolStripSeparator());

        var overlayPanel = new Panel { Width = 260, Height = 28 };
        _overlayCheckBox = new CheckBox
        {
            Text = "Overlay enabled",
            Location = new Point(8, 4),
            AutoSize = true,
            Checked = _settings.OverlayEnabled
        };
        _overlayCheckBox.MouseDown += (_, _) => _overlayCheckboxJustClicked = true;
        _overlayCheckBox.CheckedChanged += (_, _) =>
        {
            if (_overlayCheckBox == null) return;
            _overlay.OverlayActive = _overlayCheckBox.Checked;
            _settings.OverlayEnabled = _overlayCheckBox.Checked;
            _settings.Save();
        };
        overlayPanel.Controls.Add(_overlayCheckBox);
        menu.Items.Add(new ToolStripControlHost(overlayPanel) { Margin = Padding.Empty, Padding = new Padding(8, 6, 8, 6) });
        _overlay.OverlayActive = _settings.OverlayEnabled;

        var startupPanel = new Panel { Width = 260, Height = 28 };
        _startupCheckBox = new CheckBox
        {
            Text = "Start on system startup",
            Location = new Point(8, 4),
            AutoSize = true,
            Checked = StartupHelper.IsEnabled || _settings.StartOnStartup
        };
        if (_startupCheckBox.Checked)
            StartupHelper.SetEnabled(true); // Refresh path (fixes wrong path from dotnet run / IDE)
        _settings.StartOnStartup = _startupCheckBox.Checked;
        _startupCheckBox.MouseDown += (_, _) => _startupCheckboxJustClicked = true;
        _startupCheckBox.CheckedChanged += (_, _) =>
        {
            if (_startupCheckBox == null) return;
            StartupHelper.SetEnabled(_startupCheckBox.Checked);
            _settings.StartOnStartup = _startupCheckBox.Checked;
            _settings.Save();
        };
        startupPanel.Controls.Add(_startupCheckBox);
        menu.Items.Add(new ToolStripControlHost(startupPanel) { Margin = Padding.Empty, Padding = new Padding(8, 6, 8, 6) });

        menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => { _contextMenu.Close(); Application.Exit(); }));

        return menu;
    }

    public void Dispose()
    {
        _outsideClickHook.Uninstall();
        _outsideClickHook.Dispose();
        _contextMenu.Closing -= OnContextMenuClosing;
        _contextMenu.Closed -= OnContextMenuClosed;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}

/// <summary>Uses a low-level mouse hook to close the menu when the user clicks anywhere outside it (desktop, overlay, other apps).</summary>
internal sealed class MenuOutsideClickHook : IDisposable
{
    private readonly ContextMenuStrip _menu;
    private readonly Win32.LowLevelMouseProc _proc;
    private IntPtr _hookHandle = IntPtr.Zero;
    private bool _disposed;

    public MenuOutsideClickHook(ContextMenuStrip menu)
    {
        _menu = menu;
        _proc = HookCallback;
    }

    public void Install()
    {
        if (_hookHandle != IntPtr.Zero || _disposed) return;
        using var curProcess = System.Diagnostics.Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule;
        if (curModule?.ModuleName == null) return;
        _hookHandle = Win32.SetWindowsHookEx(Win32.WH_MOUSE_LL, _proc, Win32.GetModuleHandle(curModule.ModuleName), 0);
    }

    public void Uninstall()
    {
        if (_hookHandle == IntPtr.Zero) return;
        Win32.UnhookWindowsHookEx(_hookHandle);
        _hookHandle = IntPtr.Zero;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam == (IntPtr)Win32.WM_LBUTTONDOWN_LL && _menu.Visible)
        {
            var hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            var clickPos = new Point(hookStruct.pt.X, hookStruct.pt.Y);
            Rectangle bounds;
            var form = _menu.FindForm();
            if (form != null && form.Visible)
                bounds = form.Bounds;
            else if (_menu.IsHandleCreated && Win32.GetWindowRect(_menu.Handle, out RECT r))
                bounds = new Rectangle(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
            else
                bounds = Rectangle.Empty;
            if (!bounds.IsEmpty && !bounds.Contains(clickPos))
                _menu.BeginInvoke(() => { if (_menu.Visible) _menu.Close(); });
        }
        return Win32.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed) return;
        Uninstall();
        _disposed = true;
    }
}
