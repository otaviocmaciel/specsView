using System.Text;

namespace SpecsView.Sensors.Pdh;

/// <summary>
/// Per-instance state for wildcard counters, looked up by span so steady-state ticks allocate no
/// strings; a string is created only the first time an instance appears.
/// </summary>
internal sealed class InstanceTable<T>(Func<string, T> create) where T : class
{
    private readonly Dictionary<string, T> _items = new(StringComparer.Ordinal);

    public IEnumerable<T> Items => _items.Values;

    public T Get(ReadOnlySpan<char> instance)
    {
        var lookup = _items.GetAlternateLookup<ReadOnlySpan<char>>();
        if (!lookup.TryGetValue(instance, out var item))
        {
            var name = instance.ToString();
            item = create(name);
            _items[name] = item;
        }

        return item;
    }
}

internal static class MetricIds
{
    /// <summary>Turns "Intel[R] Ethernet (7) I219-V" into "intel-r-ethernet-7-i219-v".</summary>
    public static string Slug(string text, int maxLength = 40)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                builder.Append(c);
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        var slug = builder.ToString().Trim('-');
        return slug.Length <= maxLength ? slug : slug[..maxLength].TrimEnd('-');
    }
}
