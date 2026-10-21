using System.Text;
using System.Text.RegularExpressions;
using SpecsView.Core.Formatting;
using SpecsView.Core.Metrics;
using SpecsView.Sensors;
using SpecsView.Sensors.Providers;

namespace SpecsView.App;

/// <summary>
/// Lists every metric this machine exposes, with its current value, so users can copy ids into
/// config.json until the settings window exists.
/// </summary>
internal static class MetricsReport
{
    public static void Write(string path)
    {
        using var scheduler = new MetricScheduler(TimeSpan.FromSeconds(1));
        scheduler.SetProviders(ProviderCatalog.CreateAll());

        // Rate counters (CPU %, disk, network) need two samples before they have values.
        scheduler.SampleOnce();
        Thread.Sleep(1000);
        var snapshot = scheduler.SampleOnce();

        var text = new StringBuilder()
            .AppendLine("# Métricas disponíveis")
            .AppendLine()
            .AppendLine($"Gerado em {DateTime.Now:g}. Copie o id para \"metric\" em config.json.")
            .AppendLine(HardwareProvider.AccessProblem is { } problem
                ? $"Sensores de CPU/placa-mãe (hw.cpu.*, hw.mb.*) aparecem como \"—\": {problem}"
                : "")
            .AppendLine();

        foreach (var group in snapshot.Descriptors.Values.GroupBy(d => d.Id.Split('.', 2)[0]).OrderBy(g => OrderOf(g.Key)))
        {
            text.AppendLine($"## {group.Key}").AppendLine()
                .AppendLine("| id | descrição | valor atual |")
                .AppendLine("|---|---|---|");
            foreach (var descriptor in group.OrderBy(d => NaturalKey(d.Id), StringComparer.Ordinal))
            {
                var value = MetricFormatter.Format(snapshot.GetValue(descriptor.Id), descriptor.Unit);
                text.AppendLine($"| `{descriptor.Id}` | {descriptor.DisplayName} | {value} |");
            }

            text.AppendLine();
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text.ToString());
    }

    /// <summary>"cpu.core.2" sorts before "cpu.core.10".</summary>
    private static string NaturalKey(string id) => Regex.Replace(id, @"\d+", m => m.Value.PadLeft(6, '0'));

    private static int OrderOf(string prefix)
    {
        var index = ProviderCatalog.Prefixes.ToList().IndexOf(prefix);
        return index < 0 ? int.MaxValue : index;
    }
}
