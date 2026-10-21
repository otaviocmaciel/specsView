using System.Runtime.InteropServices;
using SpecsView.Core.Metrics;
using SpecsView.Sensors.Native;

namespace SpecsView.Sensors.Providers;

public sealed class MemoryProvider : IMetricProvider
{
    public const string UsedPercent = "mem.usedPercent";
    public const string Used = "mem.used";
    public const string Available = "mem.available";
    public const string Total = "mem.total";
    public const string CommitPercent = "mem.commitPercent";
    public const string CommitUsed = "mem.commitUsed";
    public const string CommitLimit = "mem.commitLimit";

    public string Id => "mem";

    public IReadOnlyList<MetricDescriptor> Metrics { get; } =
    [
        new(UsedPercent, "Memória usada (%)", MetricUnit.Percent),
        new(Used, "Memória usada", MetricUnit.Bytes),
        new(Available, "Memória disponível", MetricUnit.Bytes),
        new(Total, "Memória total", MetricUnit.Bytes),
        new(CommitPercent, "Memória confirmada (%)", MetricUnit.Percent),
        new(CommitUsed, "Memória confirmada", MetricUnit.Bytes),
        new(CommitLimit, "Limite de confirmação", MetricUnit.Bytes),
    ];

    public bool RequiresElevation => false;

    public void Sample(MetricSink sink)
    {
        var status = new Kernel32.MemoryStatusEx { Length = (uint)Marshal.SizeOf<Kernel32.MemoryStatusEx>() };
        if (!Kernel32.GlobalMemoryStatusEx(ref status) || status.TotalPhys == 0)
        {
            return;
        }

        double total = status.TotalPhys;
        double available = status.AvailPhys;
        sink.Add(Total, total);
        sink.Add(Available, available);
        sink.Add(Used, total - available);
        sink.Add(UsedPercent, (total - available) / total * 100);

        // "Page file" fields are really the commit charge: RAM + page file.
        double commitLimit = status.TotalPageFile;
        double commitUsed = status.TotalPageFile - status.AvailPageFile;
        sink.Add(CommitLimit, commitLimit);
        sink.Add(CommitUsed, commitUsed);
        if (commitLimit > 0)
        {
            sink.Add(CommitPercent, commitUsed / commitLimit * 100);
        }
    }

    public void Dispose()
    {
    }
}
