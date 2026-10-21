using SpecsView.Core.Diagnostics;

namespace SpecsView.Core.Metrics;

/// <summary>
/// Samples the active providers on a background thread at a fixed interval and publishes a
/// <see cref="MetricSnapshot"/> per tick. <see cref="SnapshotReady"/> is raised on the scheduler
/// thread; consumers must marshal to their own thread.
/// </summary>
public sealed class MetricScheduler : IDisposable
{
    public static readonly TimeSpan MinInterval = TimeSpan.FromMilliseconds(250);

    private readonly Lock _gate = new();
    private readonly HashSet<string> _failingProviders = new(StringComparer.Ordinal);
    private readonly PeriodicTimer _timer;
    private readonly CancellationTokenSource _cts = new();
    private ProviderSet _providers = ProviderSet.Empty;
    private IReadOnlyDictionary<string, MetricDescriptor> _descriptors = ProviderSet.Empty.Descriptors;
    private Task? _loop;
    private long _tick;

    public MetricScheduler(TimeSpan interval)
    {
        _timer = new PeriodicTimer(Clamp(interval));
    }

    public event Action<MetricSnapshot>? SnapshotReady;

    public bool IsPaused { get; set; }

    public TimeSpan Interval
    {
        get => _timer.Period;
        set => _timer.Period = Clamp(value);
    }

    public void Start()
    {
        _loop ??= Task.Run(RunAsync);
    }

    /// <summary>Swaps the active providers; the previous set is disposed.</summary>
    public void SetProviders(ProviderSet providers)
    {
        ProviderSet old;
        lock (_gate)
        {
            old = _providers;
            _providers = providers;
            _descriptors = providers.Descriptors;
            _failingProviders.Clear();
        }

        old.Dispose();
    }

    public MetricSnapshot SampleOnce()
    {
        lock (_gate)
        {
            var sink = new MetricSink(++_tick);
            foreach (var provider in _providers.Providers)
            {
                try
                {
                    provider.Sample(sink);
                }
                catch (Exception ex)
                {
                    // Log only the first failure per provider to avoid flooding the log every tick.
                    if (_failingProviders.Add(provider.Id))
                    {
                        Log.Error($"Provider '{provider.Id}' failed to sample.", ex);
                    }
                }
            }

            MergeDescriptors(sink.Descriptors);
            return new MetricSnapshot(DateTime.Now, sink.Values, _descriptors);
        }
    }

    /// <summary>
    /// Adds runtime-discovered descriptors. A new dictionary is published only when something is
    /// new, so snapshots already handed to the UI are never mutated.
    /// </summary>
    private void MergeDescriptors(IReadOnlyDictionary<string, MetricDescriptor>? announced)
    {
        if (announced is null || announced.Keys.All(_descriptors.ContainsKey))
        {
            return;
        }

        var merged = new Dictionary<string, MetricDescriptor>(_descriptors, StringComparer.Ordinal);
        foreach (var (id, descriptor) in announced)
        {
            merged.TryAdd(id, descriptor);
        }

        _descriptors = merged;
    }

    private async Task RunAsync()
    {
        try
        {
            do
            {
                if (!IsPaused)
                {
                    SnapshotReady?.Invoke(SampleOnce());
                }
            }
            while (await _timer.WaitForNextTickAsync(_cts.Token).ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Log.Error("Metric scheduler stopped unexpectedly.", ex);
        }
    }

    private static TimeSpan Clamp(TimeSpan interval) => interval < MinInterval ? MinInterval : interval;

    public void Dispose()
    {
        _cts.Cancel();
        try
        {
            _loop?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }

        _timer.Dispose();
        SetProviders(ProviderSet.Empty);
        _cts.Dispose();
    }
}
