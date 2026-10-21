namespace SpecsView.Core.Metrics;

/// <summary>The active providers plus any resources they share (e.g. a single PDH query).</summary>
public sealed class ProviderSet : IDisposable
{
    private readonly IReadOnlyList<IDisposable> _sharedResources;

    public ProviderSet(IReadOnlyList<IMetricProvider> providers, IReadOnlyList<IDisposable>? sharedResources = null)
    {
        Providers = providers;
        _sharedResources = sharedResources ?? [];
        Descriptors = providers
            .SelectMany(p => p.Metrics)
            .GroupBy(d => d.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
    }

    public static ProviderSet Empty => new([]);

    public IReadOnlyList<IMetricProvider> Providers { get; }

    public IReadOnlyDictionary<string, MetricDescriptor> Descriptors { get; }

    public void Dispose()
    {
        foreach (var provider in Providers)
        {
            provider.Dispose();
        }

        foreach (var resource in _sharedResources)
        {
            resource.Dispose();
        }
    }
}
