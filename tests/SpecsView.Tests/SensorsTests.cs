using SpecsView.Core.Metrics;
using SpecsView.Sensors;
using SpecsView.Sensors.Pdh;

namespace SpecsView.Tests;

public class SensorsTests
{
    [Theory]
    [InlineData("Intel[R] Ethernet Connection (7) I219-V", "intel-r-ethernet-connection-7-i219-v")]
    [InlineData("Core (Tctl/Tdie)", "core-tctl-tdie")]
    [InlineData("  --Weird__Name--  ", "weird-name")]
    public void Slug_is_lowercase_ascii_with_single_dashes(string input, string expected) =>
        Assert.Equal(expected, MetricIds.Slug(input));

    [Fact]
    public void Slug_is_truncated_without_trailing_dash() =>
        Assert.Equal("abc", MetricIds.Slug("abc def", maxLength: 4));

    [Fact]
    public void Catalog_creates_only_requested_providers()
    {
        using var set = ProviderCatalog.CreateFor(["mem.usedPercent", "battery.percent"]);

        Assert.Equal(["mem", "battery"], set.Providers.Select(p => p.Id));
    }

    [Fact]
    public void Memory_provider_reports_consistent_values()
    {
        using var scheduler = new MetricScheduler(TimeSpan.FromSeconds(1));
        scheduler.SetProviders(ProviderCatalog.CreateFor(["mem.used"]));

        var snapshot = scheduler.SampleOnce();

        var total = snapshot.GetValue("mem.total");
        var used = snapshot.GetValue("mem.used");
        Assert.NotNull(total);
        Assert.InRange(used!.Value, 0, total!.Value);
    }
}
