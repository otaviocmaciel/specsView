using SpecsView.Core.Diagnostics;
using SpecsView.Core.Metrics;
using SpecsView.Sensors.Pdh;
using SpecsView.Sensors.Providers;

namespace SpecsView.Sensors;

/// <summary>Creates only the providers needed for the selected metrics.</summary>
/// <remarks>Creation can take a second or more (hardware sensors); call it off the UI thread.</remarks>
public static class ProviderCatalog
{
    private sealed record Context(Func<PdhQuery> Pdh, IReadOnlyCollection<string> MetricIds, bool All);

    // Prefix of the metric id -> factory. Order defines the order of discovery listings.
    private static readonly (string Prefix, Func<Context, IMetricProvider> Create)[] Registrations =
    [
        ("cpu", c => new CpuProvider(c.Pdh())),
        ("mem", _ => new MemoryProvider()),
        ("gpu", c => new GpuProvider(c.Pdh())),
        ("disk", c => new DiskProvider(c.Pdh())),
        ("drive", _ => new DriveProvider()),
        ("net", c => new NetworkProvider(c.Pdh())),
        ("battery", _ => new BatteryProvider()),
        ("sys", c => new SystemProvider(c.Pdh())),
        ("hw", c => new HardwareProvider(c.MetricIds, c.All)),
    ];

    public static IEnumerable<string> Prefixes => Registrations.Select(r => r.Prefix);

    public static ProviderSet CreateFor(IReadOnlyCollection<string> metricIds)
    {
        var prefixes = metricIds
            .Select(id => id.Split('.', 2)[0])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Create(metricIds, prefixes.Contains, all: false);
    }

    /// <summary>Every provider, with every hardware group enabled; used to list available metrics.</summary>
    public static ProviderSet CreateAll() => Create([], _ => true, all: true);

    private static ProviderSet Create(IReadOnlyCollection<string> metricIds, Func<string, bool> include, bool all)
    {
        PdhQuery? pdh = null;
        var context = new Context(() => pdh ??= new PdhQuery(), metricIds, all);

        var providers = new List<IMetricProvider>();
        foreach (var (prefix, create) in Registrations)
        {
            if (!include(prefix))
            {
                continue;
            }

            try
            {
                providers.Add(create(context));
            }
            catch (Exception ex)
            {
                // One broken source (missing counter, driver issue) must not hide the others.
                Log.Error($"Could not create provider '{prefix}'.", ex);
            }
        }

        return new ProviderSet(providers, pdh is null ? [] : [pdh]);
    }
}
