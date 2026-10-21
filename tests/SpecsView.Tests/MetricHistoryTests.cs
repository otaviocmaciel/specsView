using SpecsView.Core.Metrics;

namespace SpecsView.Tests;

public class MetricHistoryTests
{
    [Fact]
    public void Keeps_last_values_in_order()
    {
        var history = new MetricHistory(3);
        foreach (var v in new[] { 1.0, 2, 3, 4, 5 })
        {
            history.Append(Snapshot(("cpu.total", v)));
        }

        Assert.Equal([3.0, 4, 5], history.Get("cpu.total"));
    }

    [Fact]
    public void Unknown_metric_is_empty() =>
        Assert.Empty(new MetricHistory(5).Get("nope"));

    [Fact]
    public void Changing_capacity_resets_history()
    {
        var history = new MetricHistory(3);
        history.Append(Snapshot(("cpu.total", 1)));
        history.Capacity = 10;

        Assert.Empty(history.Get("cpu.total"));
    }

    private static MetricSnapshot Snapshot(params (string Id, double Value)[] values) =>
        new(DateTime.Now, values.ToDictionary(v => v.Id, v => v.Value), new Dictionary<string, MetricDescriptor>());
}
