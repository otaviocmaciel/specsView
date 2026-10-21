using SpecsView.App.Native;
using SpecsView.App.Rendering;
using SpecsView.App.Theming;
using SpecsView.Core.Config;
using SpecsView.Core.Formatting;
using SpecsView.Core.Metrics;
using SpecsView.Sensors.Providers;

namespace SpecsView.App.Overlay;

/// <summary>
/// Borderless, click-through-free, non-activating layered window drawn on top of the taskbar.
/// All painting goes through <see cref="LayeredWindow.Update"/>; WM_PAINT is never used.
/// </summary>
internal sealed class TaskbarOverlayForm : Form
{
    private const int EdgeMargin = 4;

    private readonly uint _taskbarCreatedMessage = User32.RegisterWindowMessage("TaskbarCreated");

    // Kept in a field: the native hook calls this delegate, so it must not be garbage collected.
    private readonly User32.WinEventProc _onForegroundChanged;
    private nint _foregroundHook;
    private readonly WidgetRenderer _renderer = new();
    private readonly ToolTip _toolTip = new() { ShowAlways = true, InitialDelay = 400 };
    private readonly MetricHistory _history;
    private ItemConfig? _hoveredItem;
    private AppConfig _config;
    private MetricSnapshot _snapshot = MetricSnapshot.Empty;
    private Palette _palette;

    public TaskbarOverlayForm(AppConfig config, MetricHistory history)
    {
        _config = config;
        _history = history;
        _palette = Palette.From(config.Theme);

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Text = "SpecsView";
        _onForegroundChanged = (_, _, _, _, _, _, _) => Render();
        Size = new Size(1, 1);
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= User32.WsExLayered | User32.WsExToolWindow | User32.WsExTopmost | User32.WsExNoActivate;
            return cp;
        }
    }

    public void ApplyConfig(AppConfig config)
    {
        _config = config;
        RefreshTheme();
    }

    /// <summary>Re-reads colors, e.g. after the Windows light/dark theme changes.</summary>
    public void RefreshTheme()
    {
        _palette = Palette.From(_config.Theme);
        Render();
    }

    public void UpdateSnapshot(MetricSnapshot snapshot)
    {
        _snapshot = snapshot;
        Render();
    }

    public void Render()
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        var taskbar = TaskbarLocator.Locate();
        if (taskbar is null || TaskbarLocator.IsFullScreenAppRunning())
        {
            if (Visible)
            {
                Hide();
            }

            return;
        }

        var request = new RenderRequest(
            _config,
            _snapshot,
            _history,
            _palette,
            taskbar.Scale,
            taskbar.IsHorizontal ? taskbar.Bounds.Height : null);

        using var bitmap = _renderer.Render(request);
        LayeredWindow.Update(Handle, bitmap, ComputeLocation(taskbar, bitmap.Size));

        if (!Visible)
        {
            Show();
        }

        // Clicking the taskbar raises it above us; re-assert topmost on every frame (cheap no-op otherwise).
        User32.SetWindowPos(Handle, User32.HwndTopmost, 0, 0, 0, 0, User32.SwpNoMove | User32.SwpNoSize | User32.SwpNoActivate);
    }

    private Point ComputeLocation(TaskbarInfo taskbar, Size size)
    {
        var scale = taskbar.Scale;
        var margin = (int)(EdgeMargin * scale);
        var offset = (int)(_config.Layout.OffsetX * scale);
        var bounds = taskbar.Bounds;

        if (!taskbar.IsHorizontal)
        {
            // Vertical taskbar (Windows 10): stack above the notification area.
            var bottom = taskbar.Tray?.Top ?? bounds.Bottom;
            return new Point(bounds.Left, bottom - size.Height - margin);
        }

        var x = _config.Layout.Position switch
        {
            WidgetPosition.Left => bounds.Left + margin,
            _ => (taskbar.Tray?.Left ?? bounds.Right) - size.Width - margin,
        };

        return new Point(x + offset, bounds.Top);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        // ShowAlways is required: this window never activates, so a normal tooltip would never appear.
        var item = _renderer.LastLayout.FirstOrDefault(c => c.Bounds.Contains(e.Location)).Item;
        if (item != _hoveredItem)
        {
            _hoveredItem = item;
            _toolTip.SetToolTip(this, item is null ? null : DescribeItem(item));
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hoveredItem = null;
    }

    private string DescribeItem(ItemConfig item) =>
        string.Join("\n\n", item.MetricIds.Select(DescribeMetric));

    private string DescribeMetric(string metricId)
    {
        var descriptor = _snapshot.GetDescriptor(metricId);
        if (descriptor is null)
        {
            return $"{metricId}\nMétrica não encontrada nesta máquina.\nVeja \"Ver métricas disponíveis\" no menu.";
        }

        var value = _snapshot.GetValue(metricId);
        var text = $"{descriptor.DisplayName}\n{MetricFormatter.Format(value, descriptor.Unit)}";
        if (value is null && metricId.StartsWith("hw.", StringComparison.OrdinalIgnoreCase) &&
            HardwareProvider.AccessProblem is { } problem)
        {
            text += $"\n{problem}";
        }

        return text;
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);

        if (m.Msg == (int)_taskbarCreatedMessage || m.Msg is User32.WmDisplayChange or User32.WmDpiChanged)
        {
            // Explorer restarted, resolution changed or DPI changed: the taskbar may have moved.
            Render();
        }
        else if (m.Msg == User32.WmSettingChange)
        {
            RefreshTheme();
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        // React to focus changes right away instead of on the next tick: entering/leaving full
        // screen, and the taskbar being raised above us when clicked.
        _foregroundHook = User32.SetWinEventHook(
            User32.EventSystemForeground, User32.EventSystemForeground, 0, _onForegroundChanged, 0, 0, User32.WinEventOutOfContext);
    }

    protected override void Dispose(bool disposing)
    {
        if (_foregroundHook != 0)
        {
            User32.UnhookWinEvent(_foregroundHook);
            _foregroundHook = 0;
        }

        if (disposing)
        {
            _renderer.Dispose();
            _toolTip.Dispose();
        }

        base.Dispose(disposing);
    }
}
