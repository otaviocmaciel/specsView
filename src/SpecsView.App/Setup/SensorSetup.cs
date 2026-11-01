using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
using SpecsView.App.Native;
using SpecsView.Core.Diagnostics;
using SpecsView.Sensors.Providers;

namespace SpecsView.App.Setup;

/// <summary>
/// Installs the PawnIO kernel driver that CPU/motherboard sensors need. The official installer is
/// downloaded on demand rather than bundled: PawnIO is GPL-2.0, so shipping its binary would
/// oblige every SpecsView release to carry its source as well.
/// </summary>
internal static class SensorSetup
{
    /// <summary>"--install-driver &lt;setup.exe&gt;": run elevated; installs the driver, then starts normally.</summary>
    public const string InstallDriverArg = "--install-driver";

    private const string SetupUrl = "https://github.com/namazso/PawnIO.Setup/releases/latest/download/PawnIO_setup.exe";
    private const string Publisher = "CN=namazso.eu";

    public static async Task<string> DownloadAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SpecsView");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "PawnIO_setup.exe");

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("SpecsView");
        await using (var source = await http.GetStreamAsync(SetupUrl).ConfigureAwait(false))
        await using (var target = File.Create(path))
        {
            await source.CopyToAsync(target).ConfigureAwait(false);
        }

        return VerifyPublisher(path) is { } problem ? throw new InvalidOperationException(problem) : path;
    }

    /// <summary>Runs the installer silently. Must be called from an elevated process.</summary>
    /// <returns>Null on success, otherwise a message for the user.</returns>
    public static string? Install(string setupPath)
    {
        try
        {
            // Held open without write/delete sharing so the verified file cannot be swapped before it runs.
            using var guard = new FileStream(setupPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (VerifyPublisher(setupPath) is { } problem)
            {
                return problem;
            }

            using var process = Process.Start(new ProcessStartInfo(setupPath, "-install -silent") { UseShellExecute = false })!;
            if (!process.WaitForExit(TimeSpan.FromMinutes(2)))
            {
                return "O instalador do PawnIO não terminou a tempo.";
            }

            Log.Info($"PawnIO setup exited with code {process.ExitCode}.");
            return HardwareProvider.IsDriverInstalled
                ? null
                : $"O instalador do PawnIO falhou (código {process.ExitCode}).";
        }
        catch (Exception ex)
        {
            Log.Error("Could not install PawnIO.", ex);
            return $"Não foi possível instalar o PawnIO: {ex.Message}";
        }
    }

    private static string? VerifyPublisher(string path)
    {
        if (!WinTrust.IsSignatureValid(path))
        {
            return "O instalador do PawnIO baixado não tem uma assinatura digital válida.";
        }

        // The signature is valid; make sure it is namazso's and not any other trusted publisher's.
#pragma warning disable SYSLIB0057 // There is no X509CertificateLoader equivalent for signed files.
        using var signer = X509Certificate.CreateFromSignedFile(path);
#pragma warning restore SYSLIB0057
        return signer.Subject.Split(", ").Contains(Publisher, StringComparer.OrdinalIgnoreCase)
            ? null
            : $"O instalador do PawnIO foi assinado por um editor inesperado ({signer.Subject}).";
    }
}
