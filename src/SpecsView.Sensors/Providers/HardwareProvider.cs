using Microsoft.Win32;
using LibreHardwareMonitor.Hardware;
using SpecsView.Core.Metrics;
using SpecsView.Sensors.Pdh;

namespace SpecsView.Sensors.Providers;

/// <summary>
/// Temperatures, clocks, fans, power and voltages via LibreHardwareMonitor. Opening it is slow
/// (about a second), so it is only created when an "hw.*" metric is selected, and only the
/// hardware groups those metrics refer to are enabled. CPU and motherboard sensors need
/// administrator rights; most GPU sensors work without them.
/// </summary>
/// <remarks>
/// Ids are generic, e.g. "hw.cpu.temp.cpu-package", plus stable aliases such as "hw.cpu.temp"
/// and "hw.gpu.temp" that pick the most representative sensor.
/// </remarks>
public sealed class HardwareProvider : IMetricProvider
{
    private readonly Computer _computer;
    private readonly List<IHardware> _hardware = [];
    private readonly List<(ISensor Sensor, string Id, double Scale)> _sensors = [];
    private readonly bool _hasDriverAccess = IsElevated && IsDriverInstalled;
    private readonly List<(string Id, ISensor Sensor)> _aliases = [];

    public HardwareProvider(IReadOnlyCollection<string> metricIds, bool enableAll = false)
    {
        bool Wants(string group) => enableAll || metricIds.Any(id => id.StartsWith("hw." + group, StringComparison.OrdinalIgnoreCase));

        _computer = new Computer
        {
            IsCpuEnabled = Wants("cpu"),
            IsGpuEnabled = Wants("gpu"),
            IsMotherboardEnabled = Wants("mb"),
            IsControllerEnabled = Wants("mb"),
            IsStorageEnabled = Wants("storage"),
            IsMemoryEnabled = Wants("ram"),
            IsPsuEnabled = Wants("psu"),
        };
        _computer.Open();

        var descriptors = new List<MetricDescriptor>();
        var groupCounts = new Dictionary<string, int>();
        var usedIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var hardware in _computer.Hardware)
        {
            _hardware.Add(hardware);
            Update(hardware);

            var group = GroupOf(hardware.HardwareType);
            var count = groupCounts[group] = groupCounts.GetValueOrDefault(group) + 1;
            var groupId = count == 1 ? group : group + count;
            var groupSensors = new List<ISensor>();

            foreach (var sensor in AllSensors(hardware))
            {
                if (Describe(sensor.SensorType) is not var (kind, unit, scale))
                {
                    continue;
                }

                var id = Unique($"hw.{groupId}.{kind}.{MetricIds.Slug(sensor.Name)}", usedIds);
                _sensors.Add((sensor, id, scale));
                groupSensors.Add(sensor);
                descriptors.Add(new(id, $"{hardware.Name}: {sensor.Name} ({kind})", unit));
            }

            AddAliases(groupId, hardware.Name, groupSensors, descriptors, usedIds);
        }

        Metrics = descriptors;
    }

    public string Id => "hw";

    public IReadOnlyList<MetricDescriptor> Metrics { get; }

    public bool RequiresElevation => true;

    public static bool IsElevated => Environment.IsPrivilegedProcess;

    /// <summary>LHM 0.9.6+ reads CPU/motherboard sensors through the PawnIO kernel driver (WinRing0 is no longer used).</summary>
    public static bool IsDriverInstalled
    {
        get
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\PawnIO");
            return key is not null;
        }
    }

    /// <summary>Why CPU/motherboard sensors cannot be read, or null when they can.</summary>
    public static string? AccessProblem =>
        !IsDriverInstalled ? "Driver PawnIO não instalado (winget install namazso.PawnIO)."
        : !IsElevated ? "Requer administrador: use \"Reiniciar como administrador\" no menu."
        : null;

    public void Sample(MetricSink sink)
    {
        foreach (var hardware in _hardware)
        {
            Update(hardware);
        }

        // Sensors without a trustworthy reading are skipped and show "—".
        foreach (var (sensor, id, scale) in _sensors)
        {
            if (TryRead(sensor, out var value))
            {
                sink.Add(id, value * scale);
            }
        }

        foreach (var (id, sensor) in _aliases)
        {
            if (TryRead(sensor, out var value))
            {
                sink.Add(id, value);
            }
        }
    }

    private bool TryRead(ISensor sensor, out double value)
    {
        value = sensor.Value ?? double.NaN;
        if (double.IsNaN(value))
        {
            return false;
        }

        // Without the kernel driver (missing, or no admin to open it), CPU/motherboard sensors read 0
        // or stale defaults instead of null. Only OS-provided load values are reliable there.
        if (!_hasDriverAccess && sensor.SensorType != SensorType.Load &&
            sensor.Hardware.HardwareType is HardwareType.Cpu or HardwareType.Motherboard or HardwareType.SuperIO)
        {
            return false;
        }

        // No running component sits at 0 °C; it means "no reading".
        return sensor.SensorType != SensorType.Temperature || value > 0;
    }

    private void AddAliases(string groupId, string hardwareName, List<ISensor> sensors, List<MetricDescriptor> descriptors, HashSet<string> usedIds)
    {
        // strict: only an exact name match counts (e.g. a hot spot must not fall back to the core sensor).
        void Alias(string suffix, string displayName, SensorType type, MetricUnit unit, bool strict, params string[] preferredNames)
        {
            var candidates = sensors.Where(s => s.SensorType == type).ToList();
            var sensor = preferredNames
                .Select(name => candidates.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                .FirstOrDefault(s => s is not null) ?? (strict ? null : candidates.FirstOrDefault());

            var id = $"hw.{groupId}.{suffix}";
            if (sensor is not null && usedIds.Add(id))
            {
                _aliases.Add((id, sensor));
                descriptors.Add(new(id, $"{displayName} ({hardwareName}: {sensor.Name})", unit));
            }
        }

        if (groupId.StartsWith("cpu"))
        {
            Alias("temp", "Temperatura da CPU", SensorType.Temperature, MetricUnit.Celsius, false, "CPU Package", "Core (Tctl/Tdie)", "Core (Tctl)", "Core Average", "Core Max");
            Alias("power", "Potência da CPU", SensorType.Power, MetricUnit.Watts, false, "CPU Package", "Package");
        }
        else if (groupId.StartsWith("gpu"))
        {
            Alias("temp", "Temperatura da GPU", SensorType.Temperature, MetricUnit.Celsius, false, "GPU Core");
            Alias("hotspot", "Hot spot da GPU", SensorType.Temperature, MetricUnit.Celsius, true, "GPU Hot Spot");
            Alias("power", "Potência da GPU", SensorType.Power, MetricUnit.Watts, false, "GPU Package", "GPU Power");
            Alias("clock", "Clock da GPU", SensorType.Clock, MetricUnit.Megahertz, false, "GPU Core");
            Alias("fan", "Ventoinha da GPU", SensorType.Fan, MetricUnit.Rpm, false);
        }
        else if (groupId.StartsWith("storage"))
        {
            Alias("temp", "Temperatura do armazenamento", SensorType.Temperature, MetricUnit.Celsius, false, "Temperature");
        }
    }

    private static (string Kind, MetricUnit Unit, double Scale)? Describe(SensorType type) => type switch
    {
        SensorType.Temperature => ("temp", MetricUnit.Celsius, 1),
        SensorType.Clock => ("clock", MetricUnit.Megahertz, 1),
        SensorType.Load => ("load", MetricUnit.Percent, 1),
        SensorType.Fan => ("fan", MetricUnit.Rpm, 1),
        SensorType.Power => ("power", MetricUnit.Watts, 1),
        SensorType.Voltage => ("volt", MetricUnit.Volts, 1),
        SensorType.Control => ("control", MetricUnit.Percent, 1),
        SensorType.Level => ("level", MetricUnit.Percent, 1),
        SensorType.Throughput => ("rate", MetricUnit.BytesPerSecond, 1),
        SensorType.Data => ("data", MetricUnit.Bytes, 1024d * 1024 * 1024),  // LHM reports GB
        SensorType.SmallData => ("data", MetricUnit.Bytes, 1024d * 1024),     // LHM reports MB
        _ => null,
    };

    private static string GroupOf(HardwareType type) => type switch
    {
        HardwareType.Cpu => "cpu",
        HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel => "gpu",
        HardwareType.Motherboard or HardwareType.SuperIO or HardwareType.EmbeddedController or HardwareType.Cooler => "mb",
        HardwareType.Storage => "storage",
        HardwareType.Memory => "ram",
        HardwareType.Psu => "psu",
        _ => MetricIds.Slug(type.ToString()),
    };

    private static string Unique(string id, HashSet<string> usedIds)
    {
        var candidate = id;
        for (var i = 2; !usedIds.Add(candidate); i++)
        {
            candidate = $"{id}-{i}";
        }

        return candidate;
    }

    private static void Update(IHardware hardware)
    {
        hardware.Update();
        foreach (var sub in hardware.SubHardware)
        {
            Update(sub);
        }
    }

    private static IEnumerable<ISensor> AllSensors(IHardware hardware) =>
        hardware.Sensors.Concat(hardware.SubHardware.SelectMany(AllSensors));

    public void Dispose() => _computer.Close();
}
