using SpecsView.Core.Metrics;
using SpecsView.Sensors.Pdh;

namespace SpecsView.Sensors.Providers;

/// <summary>Physical disk activity (like Task Manager's "Active time" and read/write speed).</summary>
public sealed class DiskProvider : IMetricProvider
{
    public const string TotalActive = "disk.total.active";
    public const string TotalRead = "disk.total.read";
    public const string TotalWrite = "disk.total.write";

    private readonly PdhQuery _query;
    private readonly PdhCounter _idle;
    private readonly PdhCounter _read;
    private readonly PdhCounter _write;
    private readonly InstanceTable<Disk> _disks = new(name => new Disk(name));
    private readonly PdhInstanceHandler _onIdle;
    private readonly PdhInstanceHandler _onRead;
    private readonly PdhInstanceHandler _onWrite;

    public DiskProvider(PdhQuery query)
    {
        _query = query;
        _idle = query.AddCounter(@"\PhysicalDisk(*)\% Idle Time");
        _read = query.AddCounter(@"\PhysicalDisk(*)\Disk Read Bytes/sec");
        _write = query.AddCounter(@"\PhysicalDisk(*)\Disk Write Bytes/sec");

        // Cached delegates: lambdas over instance state would allocate on every tick.
        _onIdle = (name, value) => _disks.Get(name).Active = Math.Clamp(100 - value, 0, 100);
        _onRead = (name, value) => _disks.Get(name).Read = value;
        _onWrite = (name, value) => _disks.Get(name).Write = value;
    }

    public string Id => "disk";

    public IReadOnlyList<MetricDescriptor> Metrics { get; } =
    [
        new(TotalActive, "Disco: tempo ativo (disco mais ocupado)", MetricUnit.Percent),
        new(TotalRead, "Disco: leitura (todos)", MetricUnit.BytesPerSecond),
        new(TotalWrite, "Disco: escrita (todos)", MetricUnit.BytesPerSecond),
    ];

    public bool RequiresElevation => false;

    public void Sample(MetricSink sink)
    {
        _query.CollectFor(sink.Tick);

        // Reset first so a removed disk reports nothing instead of its last value (NaN is skipped by the sink).
        foreach (var disk in _disks.Items)
        {
            disk.Active = disk.Read = disk.Write = double.NaN;
        }

        _idle.TryReadInstances(_onIdle);
        _read.TryReadInstances(_onRead);
        _write.TryReadInstances(_onWrite);

        var maxActive = double.NaN;
        foreach (var disk in _disks.Items)
        {
            if (disk.IsTotal)
            {
                sink.Add(TotalRead, disk.Read);
                sink.Add(TotalWrite, disk.Write);
                continue;
            }

            maxActive = double.IsNaN(maxActive) ? disk.Active : Math.Max(maxActive, disk.Active);
            sink.Add(disk.ActiveDescriptor, disk.Active);
            sink.Add(disk.ReadDescriptor, disk.Read);
            sink.Add(disk.WriteDescriptor, disk.Write);
        }

        sink.Add(TotalActive, maxActive);
    }

    public void Dispose()
    {
    }

    private sealed class Disk
    {
        public Disk(string instance)
        {
            // Instances look like "0 C:" or "1 D: E:"; the leading number is the disk index.
            IsTotal = instance == "_Total";
            var space = instance.IndexOf(' ');
            var index = space > 0 ? instance[..space] : MetricIds.Slug(instance);
            var volumes = space > 0 ? instance[(space + 1)..].Trim() : "";
            var display = volumes.Length > 0 ? $"Disco {index} ({volumes})" : $"Disco {index}";

            ActiveDescriptor = new($"disk.{index}.active", $"{display}: tempo ativo", MetricUnit.Percent);
            ReadDescriptor = new($"disk.{index}.read", $"{display}: leitura", MetricUnit.BytesPerSecond);
            WriteDescriptor = new($"disk.{index}.write", $"{display}: escrita", MetricUnit.BytesPerSecond);
        }

        public bool IsTotal { get; }

        public MetricDescriptor ActiveDescriptor { get; }

        public MetricDescriptor ReadDescriptor { get; }

        public MetricDescriptor WriteDescriptor { get; }

        public double Active { get; set; } = double.NaN;

        public double Read { get; set; } = double.NaN;

        public double Write { get; set; } = double.NaN;
    }
}
