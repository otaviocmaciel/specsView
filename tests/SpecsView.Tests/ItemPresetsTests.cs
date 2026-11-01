using SpecsView.Core.Config;

namespace SpecsView.Tests;

public sealed class ItemPresetsTests
{
    private static HashSet<string> Set(params string[] ids) => new(ids, StringComparer.OrdinalIgnoreCase);

    private static AppConfig WithItems(params ItemConfig[] items) => new() { Items = items };

    [Fact]
    public void Temperature_and_power_become_secondary_values_of_the_component()
    {
        var config = ItemPresets.Apply(WithItems(), Set("cpu.total", "hw.cpu.temp", "hw.cpu.power"));

        var cpu = Assert.Single(config.Items);
        Assert.Equal("cpu.total", cpu.Metric);
        Assert.Equal(["hw.cpu.temp", "hw.cpu.power"], cpu.Secondary);
        Assert.Equal(ItemStyle.TextSparkline, cpu.Style);
        Assert.Equal("CPU", cpu.Label);
    }

    [Fact]
    public void Without_load_the_first_selected_metric_is_the_main_value()
    {
        var config = ItemPresets.Apply(WithItems(), Set("hw.gpu.power"));

        var gpu = Assert.Single(config.Items);
        Assert.Equal("hw.gpu.power", gpu.Metric);
        Assert.Null(gpu.Secondary);
        Assert.Equal(ItemStyle.Text, gpu.Style);
    }

    [Fact]
    public void Unchecking_everything_removes_the_item_but_keeps_custom_items()
    {
        var custom = new ItemConfig { Metric = "sys.uptime", Label = "Up" };
        var config = WithItems(new ItemConfig { Metric = "cpu.total", Label = "CPU" }, custom);

        var result = ItemPresets.Apply(config, Set());

        Assert.Equal([custom], result.Items);
    }

    [Fact]
    public void Existing_label_color_and_style_are_kept()
    {
        var config = WithItems(new ItemConfig { Metric = "cpu.total", Label = "Proc", Color = "#00FF00", Style = ItemStyle.TextBar });

        var cpu = Assert.Single(ItemPresets.Apply(config, Set("cpu.total", "hw.cpu.power")).Items);

        Assert.Equal("Proc", cpu.Label);
        Assert.Equal("#00FF00", cpu.Color);
        Assert.Equal(ItemStyle.TextBar, cpu.Style);
        Assert.Equal(["hw.cpu.power"], cpu.Secondary);
    }

    [Fact]
    public void Re_enabled_component_is_inserted_in_preset_order()
    {
        var config = WithItems(
            new ItemConfig { Metric = "cpu.total" },
            new ItemConfig { Metric = "gpu.total" },
            new ItemConfig { Metric = "net.total.down" });

        var result = ItemPresets.Apply(config, Set("cpu.total", "mem.usedPercent", "gpu.total", "net.total.down"));

        Assert.Equal(["cpu.total", "mem.usedPercent", "gpu.total", "net.total.down"], result.Items.Select(i => i.Metric));
    }

    [Fact]
    public void Hand_picked_main_metric_stays_first()
    {
        var config = WithItems(new ItemConfig { Metric = "cpu.clock", Secondary = ["hw.cpu.temp"] });

        var item = Assert.Single(ItemPresets.Apply(config, Set("hw.cpu.temp", "hw.cpu.power")).Items);

        Assert.Equal("cpu.clock", item.Metric);
        Assert.Equal(["hw.cpu.temp", "hw.cpu.power"], item.Secondary);
    }

    [Fact]
    public void Selected_reflects_secondary_metrics() =>
        Assert.Contains("hw.cpu.temp", ItemPresets.Selected(WithItems(new ItemConfig { Metric = "cpu.total", Secondary = ["hw.cpu.temp"] })));

    [Fact]
    public void Secondary_accepts_a_string_or_an_array_and_round_trips()
    {
        var config = ConfigService.Parse("""{ "items": [ { "metric": "cpu.total", "secondary": ["hw.cpu.temp", "hw.cpu.power"] }, { "metric": "gpu.total", "secondary": "hw.gpu.temp" } ] }""");

        Assert.NotNull(config);
        Assert.Equal(["cpu.total", "hw.cpu.temp", "hw.cpu.power"], config.Items[0].MetricIds);
        Assert.Equal(["hw.gpu.temp"], config.Items[1].Secondary);

        var json = ConfigService.Serialize(config);
        Assert.Contains("\"secondary\": \"hw.gpu.temp\"", json);
        Assert.Equal(config.Items[0].Secondary, ConfigService.Parse(json)!.Items[0].Secondary);
    }
}
