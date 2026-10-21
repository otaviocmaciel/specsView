namespace SpecsView.Core.Metrics;

/// <summary>Describes one metric a provider can produce, e.g. "cpu.total" in percent.</summary>
public sealed record MetricDescriptor(string Id, string DisplayName, MetricUnit Unit);
