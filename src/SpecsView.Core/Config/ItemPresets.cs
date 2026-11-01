namespace SpecsView.Core.Config;

/// <summary>A metric the user can toggle in the settings window, e.g. the CPU temperature.</summary>
public sealed record MetricOption(string MetricId, string Name);

/// <summary>
/// One widget item built from a component's selected metrics: the first selected one is the main
/// value and the rest are drawn as secondary values (CPU 6% ▁▂▅ 69°C 45 W).
/// </summary>
public sealed record ComponentPreset(string Name, string Label, ItemStyle Style, IReadOnlyList<MetricOption> Options)
{
    public bool Manages(string metricId) =>
        Options.Any(o => o.MetricId.Equals(metricId, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Maps the settings window checkboxes to and from <see cref="AppConfig.Items"/>.</summary>
public static class ItemPresets
{
    public static IReadOnlyList<ComponentPreset> All { get; } =
    [
        new("CPU", "CPU", ItemStyle.TextSparkline,
        [
            new("cpu.total", "Uso"),
            new("hw.cpu.temp", "Temperatura"),
            new("hw.cpu.power", "Potência"),
        ]),
        new("Memória", "RAM", ItemStyle.TextBar, [new("mem.usedPercent", "Uso")]),
        new("GPU", "GPU", ItemStyle.TextSparkline,
        [
            new("gpu.total", "Uso"),
            new("hw.gpu.temp", "Temperatura"),
            new("hw.gpu.power", "Potência"),
        ]),
        new("Disco", "Disco", ItemStyle.TextBar, [new("disk.total.active", "Atividade")]),
        new("Rede", "↓", ItemStyle.Text, [new("net.total.down", "Download")]),
        new("Rede", "↑", ItemStyle.Text, [new("net.total.up", "Upload")]),
    ];

    /// <summary>Every metric id currently shown by the config.</summary>
    public static IReadOnlySet<string> Selected(AppConfig config) =>
        config.Items.SelectMany(i => i.MetricIds).ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Rewrites the items managed by the presets so they show exactly the selected metrics. Labels,
    /// colors, styles and items that no preset manages (hand-written in config.json) are kept; a
    /// re-enabled component is inserted after the previous component instead of at the end.
    /// </summary>
    public static AppConfig Apply(AppConfig config, IReadOnlySet<string> selected)
    {
        var items = config.Items.ToList();
        var insertAt = 0;
        foreach (var preset in All)
        {
            var index = items.FindIndex(i => i.MetricIds.Any(preset.Manages));
            var existing = index >= 0 ? items[index] : null;
            var item = Build(preset, selected, existing);

            if (existing is not null)
            {
                if (item is null)
                {
                    items.RemoveAt(index);
                    insertAt = index;
                }
                else
                {
                    items[index] = item;
                    insertAt = index + 1;
                }
            }
            else if (item is not null)
            {
                items.Insert(Math.Min(insertAt, items.Count), item);
                insertAt++;
            }
        }

        return config with { Items = items };
    }

    private static ItemConfig? Build(ComponentPreset preset, IReadOnlySet<string> selected, ItemConfig? existing)
    {
        var chosen = preset.Options.Select(o => o.MetricId).Where(selected.Contains).ToList();
        var unmanaged = existing?.MetricIds.Where(id => !preset.Manages(id)).ToList() ?? [];

        // A hand-picked main metric (e.g. "cpu.clock" with the CPU temperature next to it) stays first.
        List<string> ids = existing is not null && !preset.Manages(existing.Metric)
            ? [.. unmanaged.Take(1), .. chosen, .. unmanaged.Skip(1)]
            : [.. chosen, .. unmanaged];
        if (ids.Count == 0)
        {
            return null;
        }

        var main = ids[0];
        var style = existing is not null && main.Equals(existing.Metric, StringComparison.OrdinalIgnoreCase) ? existing.Style
            : main.Equals(preset.Options[0].MetricId, StringComparison.OrdinalIgnoreCase) ? preset.Style
            : ItemStyle.Text;

        return (existing ?? new ItemConfig { Label = preset.Label }) with
        {
            Metric = main,
            Style = style,
            Secondary = ids.Count > 1 ? ids[1..] : null,
        };
    }
}
