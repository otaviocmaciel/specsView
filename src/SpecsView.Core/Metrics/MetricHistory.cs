namespace SpecsView.Core.Metrics;

/// <summary>Fixed-size history per metric, used to draw sparklines. Not thread-safe.</summary>
public sealed class MetricHistory(int capacity)
{
    private readonly Dictionary<string, RingBuffer> _buffers = new(StringComparer.Ordinal);
    private int _capacity = Math.Max(2, capacity);

    public int Capacity
    {
        get => _capacity;
        set
        {
            value = Math.Max(2, value);
            if (value != _capacity)
            {
                _capacity = value;
                _buffers.Clear();
            }
        }
    }

    public void Append(MetricSnapshot snapshot)
    {
        foreach (var (id, value) in snapshot.Values)
        {
            if (!_buffers.TryGetValue(id, out var buffer))
            {
                buffer = new RingBuffer(_capacity);
                _buffers[id] = buffer;
            }

            buffer.Add(value);
        }
    }

    /// <summary>Returns the values from oldest to newest.</summary>
    public double[] Get(string metricId) =>
        _buffers.TryGetValue(metricId, out var buffer) ? buffer.ToArray() : [];

    private sealed class RingBuffer(int capacity)
    {
        private readonly double[] _items = new double[capacity];
        private int _start;
        private int _count;

        public void Add(double value)
        {
            if (_count < _items.Length)
            {
                _items[(_start + _count) % _items.Length] = value;
                _count++;
            }
            else
            {
                _items[_start] = value;
                _start = (_start + 1) % _items.Length;
            }
        }

        public double[] ToArray()
        {
            var result = new double[_count];
            for (var i = 0; i < _count; i++)
            {
                result[i] = _items[(_start + i) % _items.Length];
            }

            return result;
        }
    }
}
