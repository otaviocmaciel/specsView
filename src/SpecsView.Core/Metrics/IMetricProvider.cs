namespace SpecsView.Core.Metrics;

/// <summary>
/// A source of metrics (CPU, memory, disk...). Providers are created only when at least one of
/// their metrics is selected, and <see cref="Sample"/> is always called from the scheduler thread.
/// </summary>
public interface IMetricProvider : IDisposable
{
    string Id { get; }

    IReadOnlyList<MetricDescriptor> Metrics { get; }

    bool RequiresElevation { get; }

    void Sample(MetricSink sink);
}
