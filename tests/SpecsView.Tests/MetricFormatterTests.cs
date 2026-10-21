using System.Globalization;
using SpecsView.Core.Formatting;
using SpecsView.Core.Metrics;

namespace SpecsView.Tests;

public class MetricFormatterTests
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    [Theory]
    [InlineData(42.4, "42%")]
    [InlineData(150, "100%")]
    [InlineData(-3, "0%")]
    public void Percent_is_rounded_and_clamped(double value, string expected) =>
        Assert.Equal(expected, MetricFormatter.Format(value, MetricUnit.Percent, Invariant));

    [Theory]
    [InlineData(512, "512 B")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(8.5 * 1024 * 1024 * 1024, "8.5 GB")]
    [InlineData(250.0 * 1024 * 1024, "250 MB")]
    public void Bytes_use_binary_units(double value, string expected) =>
        Assert.Equal(expected, MetricFormatter.Format(value, MetricUnit.Bytes, Invariant));

    [Fact]
    public void Rate_appends_per_second() =>
        Assert.Equal("1.0 MB/s", MetricFormatter.Format(1024 * 1024, MetricUnit.BytesPerSecond, Invariant));

    [Fact]
    public void Uses_culture_decimal_separator() =>
        Assert.Equal("1,5 KB", MetricFormatter.Format(1536, MetricUnit.Bytes, new CultureInfo("pt-BR")));

    [Theory]
    [InlineData(45 * 60, "45 min")]
    [InlineData(2 * 3600 + 5 * 60, "2h05")]
    [InlineData(3 * 86400 + 4 * 3600, "3d 4h")]
    public void Durations_are_compact(double seconds, string expected) =>
        Assert.Equal(expected, MetricFormatter.Format(seconds, MetricUnit.Seconds, Invariant));

    [Fact]
    public void Clock_switches_to_gigahertz() =>
        Assert.Equal("4.62 GHz", MetricFormatter.Format(4620, MetricUnit.Megahertz, Invariant));

    [Fact]
    public void Missing_value_is_unavailable()
    {
        Assert.Equal(MetricFormatter.Unavailable, MetricFormatter.Format(null, MetricUnit.Percent));
        Assert.Equal(MetricFormatter.Unavailable, MetricFormatter.Format(double.NaN, MetricUnit.Percent));
    }
}
