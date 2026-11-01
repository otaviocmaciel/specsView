using System.Text.Json.Serialization;

namespace SpecsView.Core.Config;

/// <remarks>
/// The config records use <c>set</c> rather than <c>init</c>: the System.Text.Json source generator
/// assigns every init-only property in the object initializer, so values missing from the file would
/// become default(T) instead of the defaults below. Treat instances as immutable and use <c>with</c>.
/// </remarks>
public sealed record AppConfig
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public int RefreshIntervalMs { get; set; } = 1000;

    public LayoutConfig Layout { get; set; } = new();

    public ThemeConfig Theme { get; set; } = new();

    public IReadOnlyList<ItemConfig> Items { get; set; } =
    [
        new() { Metric = "cpu.total", Label = "CPU", Style = ItemStyle.TextSparkline },
        new() { Metric = "mem.usedPercent", Label = "RAM", Style = ItemStyle.TextBar },
        new() { Metric = "gpu.total", Label = "GPU", Style = ItemStyle.TextSparkline },
        new() { Metric = "disk.total.active", Label = "Disco", Style = ItemStyle.TextBar },
        new() { Metric = "net.total.down", Label = "↓", Style = ItemStyle.Text },
        new() { Metric = "net.total.up", Label = "↑", Style = ItemStyle.Text },
    ];

    /// <summary>Clamps out-of-range values instead of rejecting the whole file.</summary>
    public AppConfig Normalize() => this with
    {
        SchemaVersion = CurrentSchemaVersion,
        RefreshIntervalMs = Math.Clamp(RefreshIntervalMs, 250, 60_000),
        Layout = (Layout ?? new()).Normalize(),
        Theme = (Theme ?? new()).Normalize(),
        Items = (Items ?? [])
            .Where(i => !string.IsNullOrWhiteSpace(i?.Metric))
            .Select(i => i.NormalizeSecondary())
            .ToList(),
    };
}

public sealed record LayoutConfig
{
    public int Rows { get; set; } = 2;

    public int ItemSpacing { get; set; } = 8;

    public int SparklinePoints { get; set; } = 30;

    public WidgetPosition Position { get; set; } = WidgetPosition.BeforeTray;

    /// <summary>Extra horizontal offset in logical pixels (negative moves left).</summary>
    public int OffsetX { get; set; }

    public LayoutConfig Normalize() => this with
    {
        Rows = Math.Clamp(Rows, 1, 2),
        ItemSpacing = Math.Clamp(ItemSpacing, 0, 64),
        SparklinePoints = Math.Clamp(SparklinePoints, 5, 120),
    };
}

public sealed record ThemeConfig
{
    public FontConfig Font { get; set; } = new();

    /// <summary>When true, foreground follows the Windows taskbar theme (light/dark).</summary>
    public bool FollowSystemTheme { get; set; } = true;

    public string Foreground { get; set; } = "#FFFFFF";

    /// <summary>#AARRGGBB. Fully transparent by default.</summary>
    public string Background { get; set; } = "#00000000";

    public string Accent { get; set; } = "#4CC2FF";

    public IReadOnlyList<ThresholdConfig> Thresholds { get; set; } =
    [
        new() { Above = 80, Color = "#FFB900" },
        new() { Above = 95, Color = "#E81123" },
    ];

    public ThemeConfig Normalize() => this with
    {
        Font = (Font ?? new()).Normalize(),
        Thresholds = (Thresholds ?? []).OrderBy(t => t.Above).ToList(),
    };
}

public sealed record FontConfig
{
    public string Family { get; set; } = "Segoe UI";

    /// <summary>Size in points.</summary>
    public float Size { get; set; } = 9;

    public bool Bold { get; set; }

    public FontConfig Normalize() => this with
    {
        Family = string.IsNullOrWhiteSpace(Family) ? "Segoe UI" : Family,
        Size = Math.Clamp(Size, 6, 24),
    };
}

public sealed record ThresholdConfig
{
    public double Above { get; set; }

    public string Color { get; set; } = "#FFFFFF";
}

public sealed record ItemConfig
{
    public string Metric { get; set; } = "";

    public string Label { get; set; } = "";

    public ItemStyle Style { get; set; } = ItemStyle.Text;

    /// <summary>Optional value color override (#RRGGBB); thresholds still apply on top.</summary>
    public string? Color { get; set; }

    /// <summary>
    /// Optional extra metrics drawn after the graphic, e.g. "hw.cpu.temp" and "hw.cpu.power" next
    /// to "cpu.total", so one item shows load, sparkline, temperature and power together. Written
    /// as a string when there is one and as an array when there are several.
    /// </summary>
    [JsonConverter(typeof(StringOrArrayConverter))]
    public IReadOnlyList<string>? Secondary { get; set; }

    /// <summary>The metric ids this item needs sampled.</summary>
    [JsonIgnore]
    public IEnumerable<string> MetricIds => [Metric, .. Secondary ?? []];

    internal ItemConfig NormalizeSecondary()
    {
        var secondary = (Secondary ?? [])
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return this with { Secondary = secondary.Count == 0 ? null : secondary };
    }
}

public enum ItemStyle
{
    [JsonStringEnumMemberName("text")]
    Text,

    [JsonStringEnumMemberName("text+bar")]
    TextBar,

    [JsonStringEnumMemberName("text+sparkline")]
    TextSparkline,
}

public enum WidgetPosition
{
    /// <summary>Right-aligned against the notification area (clock/tray icons).</summary>
    [JsonStringEnumMemberName("beforeTray")]
    BeforeTray,

    /// <summary>Left edge of the taskbar (free space when icons are centered on Windows 11).</summary>
    [JsonStringEnumMemberName("left")]
    Left,
}
