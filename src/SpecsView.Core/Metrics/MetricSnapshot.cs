namespace SpecsView.Core.Metrics;

public sealed record MetricSnapshot(
    DateTime Timestamp,
    IReadOnlyDictionary<string, double> Values,
    IReadOnlyDictionary<string, MetricDescriptor> Descriptors)
{
    public static MetricSnapshot Empty { get; } = new(
        DateTime.MinValue,
        new Dictionary<string, double>(),
        new Dictionary<string, MetricDescriptor>());

    public double? GetValue(string metricId) => Values.TryGetValue(metricId, out var v) ? v : null;

    public MetricDescriptor? GetDescriptor(string metricId) => Descriptors.GetValueOrDefault(metricId);
}
