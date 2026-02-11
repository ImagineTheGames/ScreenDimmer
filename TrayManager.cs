using System.Drawing;

namespace ScreenDimmer;

public sealed class TrayManager : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _contextMenu;
    private readonly OverlayForm _overlay;
    private readonly AppSettings _settings;

    public TrayManager(OverlayForm overlay, AppSettings settings)
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
        _contextMenu.Opening += (_, _) =>
        {
            if (_dimTrack != null) _dimTrack.Value = _overlay.DimLevel;
            if (_colorTrack != null) _colorTrack.Value = _overlay.ColorTemp;
            UpdateEnabledMenuItemCheckState();
            UpdateStartupMenuItemCheckState();
        };
        _notifyIcon.ContextMenuStrip = _contextMenu;

    }

    private TrackBar? _dimTrack;
    private TrackBar? _colorTrack;
    private ToolStripMenuItem? _startupMenuItem;
    private ToolStripMenuItem? _enabledMenuItem;

    private void ToggleOverlayEnabled(ToolStripMenuItem item)
    {
        bool enabled = item.Checked;
        _overlay.OverlayActive = enabled;
        _settings.OverlayEnabled = enabled;
        _settings.Save();
    }

    private void UpdateEnabledMenuItemCheckState()
    {
        if (_enabledMenuItem != null)
        {
            _enabledMenuItem.Checked = _overlay.OverlayActive;
            _settings.OverlayEnabled = _overlay.OverlayActive;
        }
    }

    private void ToggleStartOnStartup(ToolStripMenuItem item)
    {
        bool enabled = item.Checked;
        StartupHelper.SetEnabled(enabled);
        _settings.StartOnStartup = enabled;
        _settings.Save();
    }

    private void UpdateStartupMenuItemCheckState()
    {
        if (_startupMenuItem != null)
        {
            _startupMenuItem.Checked = StartupHelper.IsEnabled;
            _settings.StartOnStartup = _startupMenuItem.Checked;
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

        _enabledMenuItem = new ToolStripMenuItem("Overlay enabled", null, (_, _) => { if (_enabledMenuItem != null) ToggleOverlayEnabled(_enabledMenuItem); })
        {
            CheckOnClick = true,
            Checked = _settings.OverlayEnabled
        };
        _overlay.OverlayActive = _settings.OverlayEnabled;
        menu.Items.Add(_enabledMenuItem);

        _startupMenuItem = new ToolStripMenuItem("Start on system startup", null, (_, _) => { if (_startupMenuItem != null) ToggleStartOnStartup(_startupMenuItem); })
        {
            CheckOnClick = true,
            Checked = StartupHelper.IsEnabled || _settings.StartOnStartup
        };
        if (_startupMenuItem.Checked && !StartupHelper.IsEnabled)
            StartupHelper.SetEnabled(true);
        _settings.StartOnStartup = _startupMenuItem.Checked;
        menu.Items.Add(_startupMenuItem);

        menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => Application.Exit()));

        return menu;
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
