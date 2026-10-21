using SpecsView.Core.Metrics;
using SpecsView.Sensors.Pdh;

namespace SpecsView.Sensors.Providers;

public sealed class CpuProvider : IMetricProvider
{
    public const string Total = "cpu.total";
    public const string Clock = "cpu.clock";
    public const string CorePrefix = "cpu.core.";

    private readonly PdhQuery _query;
    private readonly PdhCounter _total;
    private readonly PdhCounter? _cores;
    private readonly PdhCounter? _frequency;
    private readonly PdhCounter? _performance;
    private readonly List<(int Group, int Number, double Value)> _coreValues = [];
    private readonly string[] _coreIds;
    private readonly PdhInstanceHandler _onCore;

    public CpuProvider(PdhQuery query)
    {
        _query = query;

        // "% Processor Utility" matches Task Manager; "% Processor Time" is the fallback for old systems.
        if (query.TryAddCounter(@"\Processor Information(_Total)\% Processor Utility", out var utility))
        {
            _total = utility!;
            query.TryAddCounter(@"\Processor Information(*)\% Processor Utility", out _cores);
        }
        else
        {
            _total = query.AddCounter(@"\Processor(_Total)\% Processor Time");
        }

        // Effective clock = nominal frequency x "% Processor Performance" (can exceed 100% with turbo).
        query.TryAddCounter(@"\Processor Information(_Total)\Processor Frequency", out _frequency);
        query.TryAddCounter(@"\Processor Information(_Total)\% Processor Performance", out _performance);

        _onCore = OnCoreInstance;
        _coreIds = Enumerable.Range(0, Environment.ProcessorCount).Select(i => CorePrefix + i).ToArray();
        Metrics =
        [
            new(Total, "CPU (total)", MetricUnit.Percent),
            new(Clock, "CPU clock efetivo", MetricUnit.Megahertz),
            .. _coreIds.Select((id, i) => new MetricDescriptor(id, $"CPU núcleo lógico {i}", MetricUnit.Percent)),
        ];
    }

    public string Id => "cpu";

    public IReadOnlyList<MetricDescriptor> Metrics { get; }

    public bool RequiresElevation => false;

    public void Sample(MetricSink sink)
    {
        _query.CollectFor(sink.Tick);
        if (_total.TryGetValue(out var total))
        {
            sink.Add(Total, Math.Clamp(total, 0, 100));
        }

        if (_frequency?.TryGetValue(out var mhz) == true && _performance?.TryGetValue(out var perf) == true)
        {
            sink.Add(Clock, mhz * perf / 100);
        }

        SampleCores(sink);
    }

    private void SampleCores(MetricSink sink)
    {
        _coreValues.Clear();
        _cores?.TryReadInstances(_onCore);
        _coreValues.Sort((a, b) => a.Group != b.Group ? a.Group.CompareTo(b.Group) : a.Number.CompareTo(b.Number));
        for (var i = 0; i < _coreValues.Count && i < _coreIds.Length; i++)
        {
            sink.Add(_coreIds[i], Math.Clamp(_coreValues[i].Value, 0, 100));
        }
    }

    // Instances are "group,number" (e.g. "0,7") plus "_Total" and "0,_Total" aggregates.
    private void OnCoreInstance(ReadOnlySpan<char> name, double value)
    {
        var comma = name.IndexOf(',');
        if (comma > 0 && int.TryParse(name[..comma], out var group) && int.TryParse(name[(comma + 1)..], out var number))
        {
            _coreValues.Add((group, number, value));
        }
    }

    public void Dispose()
    {
        // Counters are released together with the shared query.
    }
}
