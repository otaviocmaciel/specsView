using System.Globalization;
using SpecsView.Core.Metrics;

namespace SpecsView.Core.Formatting;

public static class MetricFormatter
{
    public const string Unavailable = "—";

    private static readonly string[] ByteSuffixes = ["B", "KB", "MB", "GB", "TB"];

    public static string Format(double? value, MetricUnit unit, IFormatProvider? culture = null)
    {
        if (value is not { } v || !double.IsFinite(v))
        {
            return Unavailable;
        }

        culture ??= CultureInfo.CurrentCulture;
        return unit switch
        {
            MetricUnit.Percent => string.Format(culture, "{0:0}%", Math.Clamp(v, 0, 100)),
            MetricUnit.Bytes => FormatBytes(v, culture),
            MetricUnit.BytesPerSecond => FormatBytes(v, culture) + "/s",
            MetricUnit.Celsius => string.Format(culture, "{0:0}°C", v),
            MetricUnit.Megahertz => v >= 1000
                ? string.Format(culture, "{0:0.00} GHz", v / 1000)
                : string.Format(culture, "{0:0} MHz", v),
            MetricUnit.Rpm => string.Format(culture, "{0:0} RPM", v),
            MetricUnit.Watts => string.Format(culture, "{0:0.0} W", v),
            MetricUnit.Volts => string.Format(culture, "{0:0.000} V", v),
            MetricUnit.Seconds => FormatDuration(v),
            _ => string.Format(culture, "{0:0}", v),
        };
    }

    /// <summary>
    /// The widest text <see cref="Format"/> can produce for a unit. The renderer reserves this width
    /// so the widget does not jitter as values change.
    /// </summary>
    public static string WidestSample(MetricUnit unit, IFormatProvider? culture = null) => unit switch
    {
        MetricUnit.Percent => "100%",
        MetricUnit.Bytes or MetricUnit.BytesPerSecond => Format(999.9 * 1024 * 1024, unit, culture),
        MetricUnit.Celsius => "100°C",
        MetricUnit.Megahertz => Format(9999, unit, culture),
        MetricUnit.Rpm => "9999 RPM",
        MetricUnit.Watts => Format(999.9, unit, culture),
        MetricUnit.Volts => Format(1.888, unit, culture),
        MetricUnit.Seconds => "99d 23h",
        _ => "99999",
    };

    private static string FormatDuration(double seconds)
    {
        var time = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return time.TotalDays >= 1 ? $"{(int)time.TotalDays}d {time.Hours}h"
            : time.TotalHours >= 1 ? $"{(int)time.TotalHours}h{time.Minutes:00}"
            : $"{time.Minutes} min";
    }

    private static string FormatBytes(double bytes, IFormatProvider culture)
    {
        var index = 0;
        while (bytes >= 1000 && index < ByteSuffixes.Length - 1)
        {
            bytes /= 1024;
            index++;
        }

        var format = index == 0 || bytes >= 100 ? "{0:0} {1}" : "{0:0.0} {1}";
        return string.Format(culture, format, bytes, ByteSuffixes[index]);
    }
}
