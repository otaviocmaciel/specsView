using System.Diagnostics;
using SpecsView.App.Overlay;
using SpecsView.App.Settings;
using SpecsView.App.Setup;
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
    private readonly ToolStripMenuItem _autostartItem;
    private ToolStripMenuItem? _enableSensorsItem;
    private MetricsSettingsForm? _settingsForm;
    private string _activeMetricsKey = "";
    private Task _providerSwap = Task.CompletedTask;

    public SpecsViewContext()
    {
        var config = _configService.Load();
        _history = new MetricHistory(config.Layout.SparklinePoints);
        _scheduler = new MetricScheduler(TimeSpan.FromMilliseconds(config.RefreshIntervalMs));

        _overlay = new TaskbarOverlayForm(config, _history);
        _pauseItem = new ToolStripMenuItem("Pausar", null, (_, _) => TogglePause());
        _autostartItem = new ToolStripMenuItem("Iniciar com o Windows", null, (_, _) => ToggleAutostart())
        {
            Checked = Autostart.IsEnabled,
        };
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
        _trayIcon.DoubleClick += (_, _) => ShowSettings();

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
        menu.Items.Add(new ToolStripMenuItem("Escolher métricas...", null, (_, _) => ShowSettings()) { Font = new Font(menu.Font, FontStyle.Bold) });
        menu.Items.Add("Editar configurações", null, (_, _) => OpenWithShell(_configService.FilePath));
        menu.Items.Add("Ver métricas disponíveis", null, (_, _) => ShowMetricsReport());
        menu.Items.Add("Abrir pasta de configurações", null, (_, _) => OpenWithShell(_configService.Directory));
        menu.Items.Add(_pauseItem);
        menu.Items.Add(_autostartItem);
        if (!HardwareProvider.IsDriverInstalled)
        {
            _enableSensorsItem = new ToolStripMenuItem("Ativar sensores da CPU...", SystemIcons.Shield.ToBitmap(), (_, _) => EnableSensors());
            menu.Items.Add(_enableSensorsItem);
        }
        else if (!HardwareProvider.IsElevated)
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
        var metrics = config.Items.SelectMany(i => i.MetricIds).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToList();
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

    private void ShowSettings()
    {
        if (_settingsForm is { IsDisposed: false })
        {
            _settingsForm.Activate();
            return;
        }

        _settingsForm = new MetricsSettingsForm(_configService.Current, selected =>
        {
            // Saving does not raise Changed (our own write), so apply it here.
            _configService.Save(ItemPresets.Apply(_configService.Current, selected));
            ApplyConfig(_configService.Current);
        });
        _settingsForm.FormClosed += (_, _) => _settingsForm = null;
        _settingsForm.Show();
        _settingsForm.Activate();
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

    private void EnableSensors()
    {
        var answer = MessageBox.Show(
            "Para ler a temperatura e a potência da CPU, o SpecsView vai:\n\n" +
            "• baixar o driver PawnIO do site oficial (github.com/namazso) e conferir a assinatura digital;\n" +
            "• instalá-lo, pedindo permissão de administrador uma única vez;\n" +
            "• passar a iniciar com o Windows como administrador, sem novos avisos (desative em \"Iniciar com o Windows\").\n\n" +
            "Continuar?",
            "Ativar sensores da CPU",
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Information);
        if (answer != DialogResult.OK)
        {
            return;
        }

        _enableSensorsItem!.Enabled = false;
        _trayIcon.ShowBalloonTip(3000, "SpecsView", "Baixando o driver PawnIO...", ToolTipIcon.Info);
        Task.Run(async () =>
        {
            try
            {
                var setupPath = await SensorSetup.DownloadAsync().ConfigureAwait(false);
                PostToUi(() =>
                {
                    if (!RestartElevated($"{SensorSetup.InstallDriverArg} \"{setupPath}\""))
                    {
                        _enableSensorsItem.Enabled = true;
                    }
                });
            }
            catch (Exception ex)
            {
                Log.Error("Could not download PawnIO.", ex);
                PostToUi(() =>
                {
                    MessageBox.Show($"Não foi possível baixar o driver PawnIO:\n{ex.Message}", "SpecsView", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    _enableSensorsItem.Enabled = true;
                });
            }
        });
    }

    private async void ToggleAutostart()
    {
        _autostartItem.Enabled = false;
        try
        {
            await Autostart.SetAsync(!_autostartItem.Checked);
        }
        finally
        {
            _autostartItem.Checked = Autostart.IsEnabled;
            _autostartItem.Enabled = true;
        }
    }

    /// <summary>Relaunches elevated (the new process waits for this one to exit); false if UAC was declined.</summary>
    private bool RestartElevated(string extraArgs = "")
    {
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!, $"{extraArgs} {Program.WaitForPreviousInstanceArg}".Trim())
            {
                UseShellExecute = true,
                Verb = "runas",
            });
            ExitThread();
            return true;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // User declined the UAC prompt; keep running unelevated.
            return false;
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
        _settingsForm?.Dispose();
        _overlay.Dispose();
        Log.Info("SpecsView stopped.");
        base.ExitThreadCore();
    }
}
