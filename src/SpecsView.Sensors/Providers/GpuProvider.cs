using SpecsView.Core.Metrics;
using SpecsView.Sensors.Native;
using SpecsView.Sensors.Pdh;

namespace SpecsView.Sensors.Providers;

/// <summary>
/// GPU load and video memory from the same counters Task Manager uses. Load per engine is the sum
/// over processes; a GPU's load is its busiest engine. Aggregate "gpu.*" metrics refer to the GPU
/// with the most dedicated memory (the discrete card on hybrid laptops), except "gpu.total", which
/// is the busiest GPU.
/// </summary>
public sealed class GpuProvider : IMetricProvider
{
    public const string Total = "gpu.total";
    public const string ThreeD = "gpu.3d";
    public const string MemUsed = "gpu.memUsed";
    public const string MemTotal = "gpu.memTotal";
    public const string MemUsedPercent = "gpu.memUsedPercent";
    public const string SharedUsed = "gpu.sharedUsed";

    private const int LuidLength = 26; // "luid_0x00000000_0x00000000"

    private readonly PdhQuery _query;
    private readonly PdhCounter _engines;
    private readonly PdhCounter? _dedicated;
    private readonly PdhCounter? _shared;
    private readonly Gpu[] _gpus;
    private readonly Gpu? _primary;
    private readonly PdhInstanceHandler _onEngine;
    private readonly PdhInstanceHandler _onDedicated;
    private readonly PdhInstanceHandler _onShared;

    public GpuProvider(PdhQuery query)
    {
        _query = query;
        _engines = query.AddCounter(@"\GPU Engine(*)\Utilization Percentage");
        query.TryAddCounter(@"\GPU Adapter Memory(*)\Dedicated Usage", out _dedicated);
        query.TryAddCounter(@"\GPU Adapter Memory(*)\Shared Usage", out _shared);

        _gpus = Dxgi.EnumerateHardwareAdapters().Select((adapter, i) => new Gpu(i, adapter)).ToArray();
        _primary = _gpus.MaxBy(g => g.Adapter.DedicatedMemory);

        _onEngine = OnEngine;
        _onDedicated = (name, value) => { if (Find(name) is { } gpu) gpu.Dedicated = value; };
        _onShared = (name, value) => { if (Find(name) is { } gpu) gpu.Shared = value; };

        Metrics =
        [
            new(Total, "GPU: uso (GPU mais ocupada)", MetricUnit.Percent),
            new(ThreeD, $"GPU: 3D ({_primary?.Adapter.Name ?? "principal"})", MetricUnit.Percent),
            new(MemUsed, "GPU: memória dedicada usada", MetricUnit.Bytes),
            new(MemTotal, "GPU: memória dedicada total", MetricUnit.Bytes),
            new(MemUsedPercent, "GPU: memória dedicada usada (%)", MetricUnit.Percent),
            new(SharedUsed, "GPU: memória compartilhada usada", MetricUnit.Bytes),
            .. _gpus.SelectMany(g => g.Descriptors),
        ];
    }

    public string Id => "gpu";

    public IReadOnlyList<MetricDescriptor> Metrics { get; }

    public bool RequiresElevation => false;

    public void Sample(MetricSink sink)
    {
        _query.CollectFor(sink.Tick);
        foreach (var gpu in _gpus)
        {
            gpu.Reset();
        }

        _engines.TryReadInstances(_onEngine);
        _dedicated?.TryReadInstances(_onDedicated);
        _shared?.TryReadInstances(_onShared);

        var busiest = double.NaN;
        foreach (var gpu in _gpus)
        {
            var (load, load3D) = gpu.Load();
            busiest = double.IsNaN(busiest) ? load : Math.Max(busiest, load);
            gpu.Write(sink, load, load3D);

            if (gpu == _primary)
            {
                sink.Add(ThreeD, load3D);
                sink.Add(MemUsed, gpu.Dedicated);
                sink.Add(MemTotal, gpu.Adapter.DedicatedMemory);
                sink.Add(MemUsedPercent, gpu.DedicatedPercent);
                sink.Add(SharedUsed, gpu.Shared);
            }
        }

        sink.Add(Total, busiest);
    }

    // Instance: "pid_1234_luid_0x00000000_0x0000D6F2_phys_0_eng_3_engtype_3D".
    private void OnEngine(ReadOnlySpan<char> name, double value)
    {
        var gpu = Find(name);
        var phys = name.IndexOf("_phys_");
        var eng = name.IndexOf("_eng_");
        var type = name.IndexOf("_engtype_");
        if (gpu is null || phys < 0 || eng < 0 || type < 0 ||
            !int.TryParse(name[(phys + 6)..eng], out var physIndex) ||
            !int.TryParse(name[(eng + 5)..type], out var engIndex))
        {
            return;
        }

        var key = ((long)physIndex << 32) | (uint)engIndex;
        if (!gpu.Engines.TryGetValue(key, out var engine))
        {
            engine = new Engine(name[(type + 9)..].SequenceEqual("3D"));
            gpu.Engines[key] = engine;
        }

        engine.Sum += value;
    }

    private Gpu? Find(ReadOnlySpan<char> instance)
    {
        var start = instance.IndexOf("luid_");
        if (start < 0 || instance.Length < start + LuidLength)
        {
            return null;
        }

        var luid = instance.Slice(start, LuidLength);
        foreach (var gpu in _gpus)
        {
            if (luid.Equals(gpu.Adapter.PdhLuid, StringComparison.OrdinalIgnoreCase))
            {
                return gpu;
            }
        }

        return null;
    }

    public void Dispose()
    {
    }

    private sealed class Engine(bool is3D)
    {
        public bool Is3D { get; } = is3D;

        public double Sum { get; set; }
    }

    private sealed class Gpu
    {
        private readonly MetricDescriptor _total;
        private readonly MetricDescriptor _threeD;
        private readonly MetricDescriptor _memUsed;
        private readonly MetricDescriptor _memTotal;
        private readonly MetricDescriptor _memPercent;
        private readonly MetricDescriptor _shared;

        public Gpu(int index, Dxgi.Adapter adapter)
        {
            Adapter = adapter;
            var prefix = $"gpu.{index}.";
            var display = $"GPU {index} ({adapter.Name})";
            _total = new(prefix + "total", $"{display}: uso", MetricUnit.Percent);
            _threeD = new(prefix + "3d", $"{display}: 3D", MetricUnit.Percent);
            _memUsed = new(prefix + "memUsed", $"{display}: memória dedicada usada", MetricUnit.Bytes);
            _memTotal = new(prefix + "memTotal", $"{display}: memória dedicada total", MetricUnit.Bytes);
            _memPercent = new(prefix + "memUsedPercent", $"{display}: memória dedicada usada (%)", MetricUnit.Percent);
            _shared = new(prefix + "sharedUsed", $"{display}: memória compartilhada usada", MetricUnit.Bytes);
        }

        public Dxgi.Adapter Adapter { get; }

        public Dictionary<long, Engine> Engines { get; } = [];

        public double Dedicated { get; set; } = double.NaN;

        public double Shared { get; set; } = double.NaN;

        public double DedicatedPercent => Adapter.DedicatedMemory > 0 ? Dedicated / Adapter.DedicatedMemory * 100 : double.NaN;

        public IEnumerable<MetricDescriptor> Descriptors => [_total, _threeD, _memUsed, _memTotal, _memPercent, _shared];

        public void Reset()
        {
            Dedicated = Shared = double.NaN;
            foreach (var engine in Engines.Values)
            {
                engine.Sum = 0;
            }
        }

        public (double Load, double Load3D) Load()
        {
            double load = 0, load3D = 0;
            foreach (var engine in Engines.Values)
            {
                load = Math.Max(load, engine.Sum);
                if (engine.Is3D)
                {
                    load3D = Math.Max(load3D, engine.Sum);
                }
            }

            return (Math.Clamp(load, 0, 100), Math.Clamp(load3D, 0, 100));
        }

        public void Write(MetricSink sink, double load, double load3D)
        {
            sink.Add(_total.Id, load);
            sink.Add(_threeD.Id, load3D);
            sink.Add(_memUsed.Id, Dedicated);
            sink.Add(_memTotal.Id, Adapter.DedicatedMemory);
            sink.Add(_memPercent.Id, DedicatedPercent);
            sink.Add(_shared.Id, Shared);
        }
    }
}
