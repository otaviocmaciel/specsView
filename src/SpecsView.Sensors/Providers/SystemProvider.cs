using SpecsView.Core.Metrics;
using SpecsView.Sensors.Pdh;

namespace SpecsView.Sensors.Providers;

public sealed class SystemProvider : IMetricProvider
{
    public const string Processes = "sys.processes";
    public const string Threads = "sys.threads";
    public const string Uptime = "sys.uptime";

    private readonly PdhQuery _query;
    private readonly PdhCounter? _processes;
    private readonly PdhCounter? _threads;

    public SystemProvider(PdhQuery query)
    {
        _query = query;
        query.TryAddCounter(@"\System\Processes", out _processes);
        query.TryAddCounter(@"\System\Threads", out _threads);
    }

    public string Id => "sys";

    public IReadOnlyList<MetricDescriptor> Metrics { get; } =
    [
        new(Processes, "Processos", MetricUnit.Count),
        new(Threads, "Threads", MetricUnit.Count),
        new(Uptime, "Tempo ligado", MetricUnit.Seconds),
    ];

    public bool RequiresElevation => false;

    public void Sample(MetricSink sink)
    {
        _query.CollectFor(sink.Tick);
        if (_processes?.TryGetValue(out var processes) == true)
        {
            sink.Add(Processes, processes);
        }

        if (_threads?.TryGetValue(out var threads) == true)
        {
            sink.Add(Threads, threads);
        }

        sink.Add(Uptime, Environment.TickCount64 / 1000.0);
    }

    public void Dispose()
    {
    }
}
