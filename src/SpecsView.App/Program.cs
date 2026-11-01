using SpecsView.App.Setup;
using SpecsView.Core.Diagnostics;

namespace SpecsView.App;

internal static class Program
{
    public const string WaitForPreviousInstanceArg = "--wait-for-previous";
    private const string ListMetricsArg = "--list-metrics";

    [STAThread]
    private static int Main(string[] args)
    {
        // "--list-metrics <file>": writes the metrics report and exits (no UI).
        if (args.Length == 2 && args[0] == ListMetricsArg)
        {
            MetricsReport.Write(Path.GetFullPath(args[1]));
            return 0;
        }

        // "--autostart on|off": elevated helper launched by the menu; changes the logon task and exits.
        if (args.Length == 2 && args[0] == Autostart.Arg)
        {
            return Autostart.Apply(args[1] == "on") ? 0 : 1;
        }

        ApplicationConfiguration.Initialize();

        // "--install-driver <setup.exe>": elevated relaunch from "Ativar sensores da CPU". Installs the
        // driver and the logon task, then carries on as the (now elevated) widget.
        var installIndex = Array.IndexOf(args, SensorSetup.InstallDriverArg);
        if (installIndex >= 0 && installIndex + 1 < args.Length)
        {
            InstallSensors(args[installIndex + 1]);
        }

        using var mutex = new Mutex(initiallyOwned: false, @"Local\SpecsView.SingleInstance");
        if (!AcquireSingleInstance(mutex, wait: args.Contains(WaitForPreviousInstanceArg)))
        {
            return 1;
        }

        Application.ThreadException += (_, e) => Log.Error("Unhandled UI exception.", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error("Unhandled exception.", e.ExceptionObject as Exception);

        Application.Run(new SpecsViewContext());
        mutex.ReleaseMutex();
        return 0;
    }

    private static void InstallSensors(string setupPath)
    {
        var problem = SensorSetup.Install(setupPath);
        if (problem is null && !Autostart.Apply(true))
        {
            problem = "O driver foi instalado, mas não foi possível configurar o início com o Windows.";
        }

        if (problem is not null)
        {
            MessageBox.Show(problem, "SpecsView", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    /// <summary>
    /// When restarting as administrator, the new process waits for the old one to exit instead of
    /// giving up immediately.
    /// </summary>
    private static bool AcquireSingleInstance(Mutex mutex, bool wait)
    {
        try
        {
            return mutex.WaitOne(wait ? TimeSpan.FromSeconds(10) : TimeSpan.Zero);
        }
        catch (AbandonedMutexException)
        {
            return true; // Previous instance crashed; we own the mutex now.
        }
    }
}
