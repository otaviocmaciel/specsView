using SpecsView.Core.Metrics;
using SpecsView.Sensors.Native;

namespace SpecsView.Sensors.Providers;

/// <summary>Battery level and time remaining. Desktops without a battery simply report nothing.</summary>
public sealed class BatteryProvider : IMetricProvider
{
    public const string Percent = "battery.percent";
    public const string TimeRemaining = "battery.timeRemaining";
    public const string Charging = "battery.charging";

    public string Id => "battery";

    public IReadOnlyList<MetricDescriptor> Metrics { get; } =
    [
        new(Percent, "Bateria (%)", MetricUnit.Percent),
        new(TimeRemaining, "Bateria: tempo restante", MetricUnit.Seconds),
        new(Charging, "Bateria: carregando (1 = sim)", MetricUnit.Count),
    ];

    public bool RequiresElevation => false;

    public void Sample(MetricSink sink)
    {
        if (!Kernel32.GetSystemPowerStatus(out var status) ||
            status.BatteryFlag is Kernel32.BatteryFlagNoBattery or Kernel32.BatteryFlagUnknown)
        {
            return;
        }

        if (status.BatteryLifePercent <= 100)
        {
            sink.Add(Percent, status.BatteryLifePercent);
        }

        // -1 while charging or while Windows is still estimating.
        if (status.BatteryLifeTime >= 0)
        {
            sink.Add(TimeRemaining, status.BatteryLifeTime);
        }

        sink.Add(Charging, (status.BatteryFlag & Kernel32.BatteryFlagCharging) != 0 ? 1 : 0);
    }

    public void Dispose()
    {
    }
}
