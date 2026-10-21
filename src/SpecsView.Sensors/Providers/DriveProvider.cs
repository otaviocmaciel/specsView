using SpecsView.Core.Metrics;

namespace SpecsView.Sensors.Providers;

/// <summary>Free/used space per fixed volume (C:, D:...). Refreshed every few seconds, not every tick.</summary>
public sealed class DriveProvider : IMetricProvider
{
    private static readonly TimeSpan RefreshEvery = TimeSpan.FromSeconds(10);

    private readonly Dictionary<string, Drive> _drives = new(StringComparer.OrdinalIgnoreCase);
    private long _nextRefresh;

    public string Id => "drive";

    public IReadOnlyList<MetricDescriptor> Metrics => [];

    public bool RequiresElevation => false;

    public void Sample(MetricSink sink)
    {
        if (Environment.TickCount64 >= _nextRefresh)
        {
            Refresh();
            _nextRefresh = Environment.TickCount64 + (long)RefreshEvery.TotalMilliseconds;
        }

        foreach (var drive in _drives.Values)
        {
            sink.Add(drive.UsedPercent, drive.Used / drive.Total * 100);
            sink.Add(drive.UsedBytes, drive.Used);
            sink.Add(drive.FreeBytes, drive.Total - drive.Used);
            sink.Add(drive.TotalBytes, drive.Total);
        }
    }

    private void Refresh()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var info in DriveInfo.GetDrives())
        {
            // Only fixed disks: network and optical drives can block for seconds.
            if (info.DriveType != DriveType.Fixed || !info.IsReady || info.TotalSize <= 0)
            {
                continue;
            }

            var letter = info.Name.TrimEnd('\\', ':');
            if (!_drives.TryGetValue(letter, out var drive))
            {
                drive = new Drive(letter, info.VolumeLabel);
                _drives[letter] = drive;
            }

            drive.Total = info.TotalSize;
            drive.Used = info.TotalSize - info.TotalFreeSpace;
            seen.Add(letter);
        }

        foreach (var removed in _drives.Keys.Where(k => !seen.Contains(k)).ToList())
        {
            _drives.Remove(removed);
        }
    }

    public void Dispose()
    {
    }

    private sealed class Drive
    {
        public Drive(string letter, string label)
        {
            var display = string.IsNullOrWhiteSpace(label) ? $"Unidade {letter}:" : $"Unidade {letter}: ({label})";
            UsedPercent = new($"drive.{letter}.usedPercent", $"{display} usado (%)", MetricUnit.Percent);
            UsedBytes = new($"drive.{letter}.used", $"{display} usado", MetricUnit.Bytes);
            FreeBytes = new($"drive.{letter}.free", $"{display} livre", MetricUnit.Bytes);
            TotalBytes = new($"drive.{letter}.total", $"{display} total", MetricUnit.Bytes);
        }

        public MetricDescriptor UsedPercent { get; }

        public MetricDescriptor UsedBytes { get; }

        public MetricDescriptor FreeBytes { get; }

        public MetricDescriptor TotalBytes { get; }

        public double Total { get; set; }

        public double Used { get; set; }
    }
}
