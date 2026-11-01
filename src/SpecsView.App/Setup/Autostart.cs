using System.Diagnostics;
using System.Security;
using System.Security.Principal;
using SpecsView.Core.Diagnostics;

namespace SpecsView.App.Setup;

/// <summary>
/// Starts SpecsView at logon through a scheduled task with "run with highest privileges", so
/// hardware sensors work without a UAC prompt every time. Creating or deleting the task needs
/// elevation, which <see cref="SetAsync"/> requests by relaunching this exe with <see cref="Arg"/>.
/// </summary>
internal static class Autostart
{
    /// <summary>"--autostart on|off": run elevated; changes the task and exits.</summary>
    public const string Arg = "--autostart";

    private const string TaskName = "SpecsView";

    public static bool IsEnabled => RunSchtasks($"/Query /TN \"{TaskName}\"") == 0;

    /// <summary>Asks for elevation (one UAC prompt) and waits; returns whether the change was applied.</summary>
    public static async Task<bool> SetAsync(bool enabled)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, $"{Arg} {(enabled ? "on" : "off")}")
            {
                UseShellExecute = true,
                Verb = "runas",
            })!;
            await process.WaitForExitAsync().ConfigureAwait(false);
            return process.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false; // UAC prompt declined.
        }
    }

    /// <summary>Creates or deletes the task. Must be called from an elevated process.</summary>
    public static bool Apply(bool enabled)
    {
        if (!enabled)
        {
            return !IsEnabled || RunSchtasks($"/Delete /TN \"{TaskName}\" /F") == 0;
        }

        var xmlPath = Path.Combine(Path.GetTempPath(), "SpecsView-task.xml");
        try
        {
            File.WriteAllText(xmlPath, TaskXml(Environment.ProcessPath!), System.Text.Encoding.Unicode);
            return RunSchtasks($"/Create /TN \"{TaskName}\" /XML \"{xmlPath}\" /F") == 0;
        }
        finally
        {
            File.Delete(xmlPath);
        }
    }

    private static string TaskXml(string exePath)
    {
        var user = SecurityElement.Escape(WindowsIdentity.GetCurrent().Name);
        var command = SecurityElement.Escape(exePath);
        var directory = SecurityElement.Escape(Path.GetDirectoryName(exePath));

        // Priority 5 is a normal-priority process (tasks default to below normal); no time limit, runs on battery.
        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Description>Inicia o SpecsView ao entrar no Windows, com acesso aos sensores de hardware.</Description>
              </RegistrationInfo>
              <Triggers>
                <LogonTrigger>
                  <Enabled>true</Enabled>
                  <UserId>{user}</UserId>
                </LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{user}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>5</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>{command}</Command>
                  <WorkingDirectory>{directory}</WorkingDirectory>
                </Exec>
              </Actions>
            </Task>
            """;
    }

    private static int RunSchtasks(string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("schtasks.exe", arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            })!;
            process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0 && !arguments.StartsWith("/Query", StringComparison.Ordinal))
            {
                Log.Warn($"schtasks {arguments} failed ({process.ExitCode}): {error.Trim()}");
            }

            return process.ExitCode;
        }
        catch (Exception ex)
        {
            Log.Error("Could not run schtasks.", ex);
            return -1;
        }
    }
}
