using System.Diagnostics;
using SpecsView.App.Overlay;
using SpecsView.Core.Config;
using SpecsView.Core.Diagnostics;
using SpecsView.Core.Metrics;
using SpecsView.Sensors;
using SpecsView.Sensors.Providers;

namespace SpecsView.App;

/// <summary>Wires config, scheduler, overlay and tray icon together for the app lifetime.</summary>
internal sealed class SpecsViewContext : ApplicationContext
{
    private readonly ConfigService _configService = new();
    private readonly MetricScheduler _scheduler;
    private readonly MetricHistory _history;
    private readonly TaskbarOverlayForm _overlay;
    private readonly NotifyIcon _trayIcon;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _pauseItem;
    private string _activeMetricsKey = "";
    private Task _providerSwap = Task.CompletedTask;

    public SpecsViewContext()
    {
        var config = _configService.Load();
        _history = new MetricHistory(config.Layout.SparklinePoints);
        _scheduler = new MetricScheduler(TimeSpan.FromMilliseconds(config.RefreshIntervalMs));

        _overlay = new TaskbarOverlayForm(config, _history);
        _pauseItem = new ToolStripMenuItem("Pausar", null, (_, _) => TogglePause());
        _menu = BuildMenu();
        _overlay.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Right)
            {
                _menu.Show(Cursor.Position);
            }
        };

        _trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "SpecsView",
            ContextMenuStrip = _menu,
            Visible = true,
        };

        // Forces the handle so BeginInvoke works before the first frame is shown.
        _ = _overlay.Handle;

        ApplyConfig(config);
        _scheduler.SnapshotReady += OnSnapshotReady;
        _configService.Changed += c => PostToUi(() => ApplyConfig(c));
        _configService.StartWatching();
        _scheduler.Start();

        Log.Info("SpecsView started.");
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Editar configurações", null, (_, _) => OpenWithShell(_configService.FilePath));
        menu.Items.Add("Ver métricas disponíveis", null, (_, _) => ShowMetricsReport());
        menu.Items.Add("Abrir pasta de configurações", null, (_, _) => OpenWithShell(_configService.Directory));
        menu.Items.Add(_pauseItem);
        if (!HardwareProvider.IsElevated)
        {
            menu.Items.Add("Reiniciar como administrador", SystemIcons.Shield.ToBitmap(), (_, _) => RestartElevated());
        }

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Sair", null, (_, _) => ExitThread());
        return menu;
    }

    private void ApplyConfig(AppConfig config)
    {
        _history.Capacity = config.Layout.SparklinePoints;
        _scheduler.Interval = TimeSpan.FromMilliseconds(config.RefreshIntervalMs);

        // Only rebuild providers when the selected metrics actually change. Creation runs off the UI
        // thread (hardware sensors take ~1 s to open) and swaps are chained so they apply in order.
        var metrics = config.Items.Select(i => i.Metric).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToList();
        var key = string.Join('|', metrics);
        if (key != _activeMetricsKey)
        {
            _activeMetricsKey = key;
            _providerSwap = _providerSwap.ContinueWith(
                _ => _scheduler.SetProviders(ProviderCatalog.CreateFor(metrics)),
                TaskScheduler.Default);
        }

        _overlay.ApplyConfig(config);
    }

    private void OnSnapshotReady(MetricSnapshot snapshot) => PostToUi(() =>
    {
        _history.Append(snapshot);
        _overlay.UpdateSnapshot(snapshot);
    });

    private void PostToUi(Action action)
    {
        if (_overlay.IsDisposed)
        {
            return;
        }

        try
        {
            _overlay.BeginInvoke(action);
        }
        catch (InvalidOperationException)
        {
            // Handle destroyed during shutdown.
        }
    }

    private void TogglePause()
    {
        _scheduler.IsPaused = !_scheduler.IsPaused;
        _pauseItem.Text = _scheduler.IsPaused ? "Retomar" : "Pausar";
    }

    private void ShowMetricsReport()
    {
        var path = Path.Combine(_configService.Directory, "metricas.md");
        Task.Run(() =>
        {
            try
            {
                MetricsReport.Write(path);
                OpenWithShell(path);
            }
            catch (Exception ex)
            {
                Log.Error("Could not generate the metrics report.", ex);
            }
        });
    }

    private void RestartElevated()
    {
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!, Program.WaitForPreviousInstanceArg)
            {
                UseShellExecute = true,
                Verb = "runas",
            });
            ExitThread();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // User declined the UAC prompt; keep running unelevated.
        }
    }

    private static void OpenWithShell(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error($"Could not open '{path}'.", ex);
        }
    }

    protected override void ExitThreadCore()
    {
        _scheduler.SnapshotReady -= OnSnapshotReady;

        // A swap still in flight would hand the scheduler providers after it is disposed.
        _providerSwap.Wait(TimeSpan.FromSeconds(5));
        _scheduler.Dispose();
        _configService.Dispose();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _menu.Dispose();
        _overlay.Dispose();
        Log.Info("SpecsView stopped.");
        base.ExitThreadCore();
    }
}
