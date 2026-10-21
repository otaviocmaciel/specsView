using System.Text.Json.Serialization;

namespace SpecsView.Core.Config;

public sealed record AppConfig
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public int RefreshIntervalMs { get; init; } = 1000;

    public LayoutConfig Layout { get; init; } = new();

    public ThemeConfig Theme { get; init; } = new();

    public IReadOnlyList<ItemConfig> Items { get; init; } =
    [
        new() { Metric = "cpu.total", Label = "CPU", Style = ItemStyle.TextSparkline },
        new() { Metric = "mem.usedPercent", Label = "RAM", Style = ItemStyle.TextBar },
        new() { Metric = "gpu.total", Label = "GPU", Style = ItemStyle.TextSparkline },
        new() { Metric = "net.total.down", Label = "↓", Style = ItemStyle.Text },
    ];

    /// <summary>Clamps out-of-range values instead of rejecting the whole file.</summary>
    public AppConfig Normalize() => this with
    {
        SchemaVersion = CurrentSchemaVersion,
        RefreshIntervalMs = Math.Clamp(RefreshIntervalMs, 250, 60_000),
        Layout = (Layout ?? new()).Normalize(),
        Theme = (Theme ?? new()).Normalize(),
        Items = (Items ?? []).Where(i => !string.IsNullOrWhiteSpace(i?.Metric)).ToList(),
    };
}

public sealed record LayoutConfig
{
    public int Rows { get; init; } = 2;

    public int ItemSpacing { get; init; } = 8;

    public int SparklinePoints { get; init; } = 30;

    public WidgetPosition Position { get; init; } = WidgetPosition.BeforeTray;

    /// <summary>Extra horizontal offset in logical pixels (negative moves left).</summary>
    public int OffsetX { get; init; }

    public LayoutConfig Normalize() => this with
    {
        Rows = Math.Clamp(Rows, 1, 2),
        ItemSpacing = Math.Clamp(ItemSpacing, 0, 64),
        SparklinePoints = Math.Clamp(SparklinePoints, 5, 120),
    };
}

public sealed record ThemeConfig
{
    public FontConfig Font { get; init; } = new();

    /// <summary>When true, foreground follows the Windows taskbar theme (light/dark).</summary>
    public bool FollowSystemTheme { get; init; } = true;

    public string Foreground { get; init; } = "#FFFFFF";

    /// <summary>#AARRGGBB. Fully transparent by default.</summary>
    public string Background { get; init; } = "#00000000";

    public string Accent { get; init; } = "#4CC2FF";

    public IReadOnlyList<ThresholdConfig> Thresholds { get; init; } =
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
    public string Family { get; init; } = "Segoe UI";

    /// <summary>Size in points.</summary>
    public float Size { get; init; } = 9;

    public bool Bold { get; init; }

    public FontConfig Normalize() => this with
    {
        Family = string.IsNullOrWhiteSpace(Family) ? "Segoe UI" : Family,
        Size = Math.Clamp(Size, 6, 24),
    };
}

public sealed record ThresholdConfig
{
    public double Above { get; init; }

    public string Color { get; init; } = "#FFFFFF";
}

public sealed record ItemConfig
{
    public string Metric { get; init; } = "";

    public string Label { get; init; } = "";

    public ItemStyle Style { get; init; } = ItemStyle.Text;

    /// <summary>Optional value color override (#RRGGBB); thresholds still apply on top.</summary>
    public string? Color { get; init; }
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
