using SpecsView.Core.Metrics;
using SpecsView.Sensors.Pdh;

namespace SpecsView.Sensors.Providers;

public sealed class NetworkProvider : IMetricProvider
{
    public const string TotalDown = "net.total.down";
    public const string TotalUp = "net.total.up";

    // Virtual adapters mirror traffic of the physical ones; counting them would double the totals.
    private static readonly string[] VirtualKeywords =
        ["vEthernet", "Hyper-V", "Virtual", "VMware", "VirtualBox", "Loopback", "isatap", "Teredo", "WAN Miniport", "WSL"];

    private readonly PdhQuery _query;
    private readonly PdhCounter _received;
    private readonly PdhCounter _sent;
    private readonly InstanceTable<Adapter> _adapters = new(name => new Adapter(name));
    private readonly PdhInstanceHandler _onReceived;
    private readonly PdhInstanceHandler _onSent;

    public NetworkProvider(PdhQuery query)
    {
        _query = query;
        _received = query.AddCounter(@"\Network Interface(*)\Bytes Received/sec");
        _sent = query.AddCounter(@"\Network Interface(*)\Bytes Sent/sec");
        _onReceived = (name, value) => _adapters.Get(name).Down = value;
        _onSent = (name, value) => _adapters.Get(name).Up = value;
    }

    public string Id => "net";

    public IReadOnlyList<MetricDescriptor> Metrics { get; } =
    [
        new(TotalDown, "Rede: download (adaptadores físicos)", MetricUnit.BytesPerSecond),
        new(TotalUp, "Rede: upload (adaptadores físicos)", MetricUnit.BytesPerSecond),
    ];

    public bool RequiresElevation => false;

    public void Sample(MetricSink sink)
    {
        _query.CollectFor(sink.Tick);
        foreach (var adapter in _adapters.Items)
        {
            adapter.Down = adapter.Up = double.NaN;
        }

        _received.TryReadInstances(_onReceived);
        _sent.TryReadInstances(_onSent);

        double down = 0, up = 0;
        var any = false;
        foreach (var adapter in _adapters.Items)
        {
            sink.Add(adapter.DownDescriptor, adapter.Down);
            sink.Add(adapter.UpDescriptor, adapter.Up);
            if (!adapter.IsVirtual && double.IsFinite(adapter.Down) && double.IsFinite(adapter.Up))
            {
                down += adapter.Down;
                up += adapter.Up;
                any = true;
            }
        }

        if (any)
        {
            sink.Add(TotalDown, down);
            sink.Add(TotalUp, up);
        }
    }

    public void Dispose()
    {
    }

    private sealed class Adapter
    {
        public Adapter(string instance)
        {
            IsVirtual = VirtualKeywords.Any(k => instance.Contains(k, StringComparison.OrdinalIgnoreCase));
            var slug = MetricIds.Slug(instance);
            DownDescriptor = new($"net.{slug}.down", $"{instance}: download", MetricUnit.BytesPerSecond);
            UpDescriptor = new($"net.{slug}.up", $"{instance}: upload", MetricUnit.BytesPerSecond);
        }

        public bool IsVirtual { get; }

        public MetricDescriptor DownDescriptor { get; }

        public MetricDescriptor UpDescriptor { get; }

        public double Down { get; set; } = double.NaN;

        public double Up { get; set; } = double.NaN;
    }
}
