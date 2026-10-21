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

        using var mutex = new Mutex(initiallyOwned: false, @"Local\SpecsView.SingleInstance");
        if (!AcquireSingleInstance(mutex, wait: args.Contains(WaitForPreviousInstanceArg)))
        {
            return 1;
        }

        Application.ThreadException += (_, e) => Log.Error("Unhandled UI exception.", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error("Unhandled exception.", e.ExceptionObject as Exception);

        ApplicationConfiguration.Initialize();
        Application.Run(new SpecsViewContext());
        mutex.ReleaseMutex();
        return 0;
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
