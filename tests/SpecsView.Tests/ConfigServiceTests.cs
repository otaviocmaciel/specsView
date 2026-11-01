using SpecsView.Core.Config;
using SpecsView.Core.Diagnostics;

namespace SpecsView.Tests;

public sealed class ConfigServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SpecsViewTests", Guid.NewGuid().ToString("N"));

    public ConfigServiceTests()
    {
        // Keep warnings from invalid-JSON tests out of the user's real log.
        Log.Directory = Path.Combine(_directory, "logs");
    }

    [Fact]
    public void Load_creates_default_file_when_missing()
    {
        using var service = new ConfigService(_directory);

        var config = service.Load();

        Assert.True(File.Exists(service.FilePath));
        Assert.Equal(1000, config.RefreshIntervalMs);
        Assert.Contains(config.Items, i => i.Metric == "cpu.total");
    }

    [Fact]
    public void Save_then_load_round_trips()
    {
        using var service = new ConfigService(_directory);
        var config = new AppConfig
        {
            RefreshIntervalMs = 2000,
            Items = [new ItemConfig { Metric = "mem.used", Label = "RAM", Style = ItemStyle.TextBar, Color = "#00FF00" }],
        };

        service.Save(config);
        using var reloaded = new ConfigService(_directory);
        var loaded = reloaded.Load();

        Assert.Equal(2000, loaded.RefreshIntervalMs);
        var item = Assert.Single(loaded.Items);
        Assert.Equal(ItemStyle.TextBar, item.Style);
        Assert.Equal("#00FF00", item.Color);
    }

    [Fact]
    public void Parse_accepts_comments_trailing_commas_and_style_names()
    {
        const string json = """
            {
              // comentário
              "refreshIntervalMs": 500,
              "items": [ { "metric": "cpu.total", "style": "text+sparkline" }, ],
            }
            """;

        var config = ConfigService.Parse(json);

        Assert.NotNull(config);
        Assert.Equal(ItemStyle.TextSparkline, Assert.Single(config.Items).Style);
    }

    [Fact]
    public void Parse_clamps_out_of_range_values()
    {
        var config = ConfigService.Parse("""{ "refreshIntervalMs": 10, "layout": { "rows": 9 }, "theme": { "font": { "size": 100 } } }""");

        Assert.NotNull(config);
        Assert.Equal(250, config.RefreshIntervalMs);
        Assert.Equal(2, config.Layout.Rows);
        Assert.Equal(24, config.Theme.Font.Size);
    }

    [Fact]
    public void Partial_sections_keep_defaults_for_missing_values()
    {
        var config = ConfigService.Parse("""{ "layout": { "rows": 1 }, "theme": { "font": { "size": 10 } } }""");

        Assert.NotNull(config);
        Assert.Equal(1, config.Layout.Rows);
        Assert.Equal(8, config.Layout.ItemSpacing);
        Assert.Equal(30, config.Layout.SparklinePoints);
        Assert.Equal("Segoe UI", config.Theme.Font.Family);
    }

    [Fact]
    public void Secondary_metric_is_parsed_and_sampled()
    {
        var config = ConfigService.Parse("""{ "items": [ { "metric": "cpu.total", "secondary": "hw.cpu.temp" }, { "metric": "mem.used", "secondary": " " } ] }""");

        Assert.NotNull(config);
        Assert.Equal(["cpu.total", "hw.cpu.temp"], config.Items[0].MetricIds);
        Assert.Null(config.Items[1].Secondary);
        Assert.Equal(["mem.used"], config.Items[1].MetricIds);
    }

    [Fact]
    public void Computed_properties_are_not_written_to_disk() =>
        Assert.DoesNotContain("metricIds", ConfigService.Serialize(new AppConfig()), StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void Parse_returns_null_for_invalid_json() =>
        Assert.Null(ConfigService.Parse("{ not json"));

    [Fact]
    public void Serialized_style_uses_readable_names() =>
        Assert.Contains("\"text+sparkline\"", ConfigService.Serialize(new AppConfig()));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
