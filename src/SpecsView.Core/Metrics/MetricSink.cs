namespace SpecsView.Core.Metrics;

/// <summary>Collects the values produced by all providers during one scheduler tick.</summary>
public sealed class MetricSink(long tick)
{
    private readonly Dictionary<string, double> _values = new(StringComparer.Ordinal);
    private Dictionary<string, MetricDescriptor>? _descriptors;

    /// <summary>Monotonic tick number; providers sharing a resource use it to collect once per tick.</summary>
    public long Tick { get; } = tick;

    public IReadOnlyDictionary<string, double> Values => _values;

    /// <summary>Descriptors announced during this tick for metrics not known up front (disks, adapters...).</summary>
    public IReadOnlyDictionary<string, MetricDescriptor>? Descriptors => _descriptors;

    public void Add(string metricId, double value)
    {
        if (double.IsFinite(value))
        {
            _values[metricId] = value;
        }
    }

    /// <summary>Adds a value for a metric discovered at runtime, announcing its descriptor.</summary>
    public void Add(MetricDescriptor descriptor, double value)
    {
        if (double.IsFinite(value))
        {
            _values[descriptor.Id] = value;
            (_descriptors ??= new(StringComparer.Ordinal))[descriptor.Id] = descriptor;
        }
    }
}
