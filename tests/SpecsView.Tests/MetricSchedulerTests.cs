using SpecsView.Core.Metrics;

namespace SpecsView.Tests;

public class MetricSchedulerTests
{
    [Fact]
    public void Failing_provider_does_not_hide_others()
    {
        using var scheduler = new MetricScheduler(TimeSpan.FromSeconds(1));
        scheduler.SetProviders(new ProviderSet([new FakeProvider("bad", throws: true), new FakeProvider("good")]));

        var snapshot = scheduler.SampleOnce();

        Assert.Equal(42, snapshot.GetValue("good.value"));
        Assert.Null(snapshot.GetValue("bad.value"));
        Assert.NotNull(snapshot.GetDescriptor("good.value"));
    }

    [Fact]
    public void Replacing_providers_disposes_the_previous_set()
    {
        using var scheduler = new MetricScheduler(TimeSpan.FromSeconds(1));
        var old = new FakeProvider("old");
        scheduler.SetProviders(new ProviderSet([old]));

        scheduler.SetProviders(ProviderSet.Empty);

        Assert.True(old.Disposed);
    }

    [Fact]
    public void Ticks_increase_between_samples()
    {
        using var scheduler = new MetricScheduler(TimeSpan.FromSeconds(1));
        var provider = new FakeProvider("p");
        scheduler.SetProviders(new ProviderSet([provider]));

        scheduler.SampleOnce();
        scheduler.SampleOnce();

        Assert.Equal([1L, 2L], provider.Ticks);
    }

    [Fact]
    public void Runtime_descriptors_are_published_without_mutating_old_snapshots()
    {
        using var scheduler = new MetricScheduler(TimeSpan.FromSeconds(1));
        var dynamicProvider = new DynamicProvider();
        scheduler.SetProviders(new ProviderSet([dynamicProvider]));

        var first = scheduler.SampleOnce();
        dynamicProvider.Instances.Add("disk.1.read");
        var second = scheduler.SampleOnce();

        Assert.NotNull(first.GetDescriptor("disk.0.read"));
        Assert.Null(first.GetDescriptor("disk.1.read"));
        Assert.Equal(MetricUnit.BytesPerSecond, second.GetDescriptor("disk.1.read")?.Unit);
    }

    private sealed class DynamicProvider : IMetricProvider
    {
        public List<string> Instances { get; } = ["disk.0.read"];

        public string Id => "disk";

        public IReadOnlyList<MetricDescriptor> Metrics => [];

        public bool RequiresElevation => false;

        public void Sample(MetricSink sink)
        {
            foreach (var id in Instances)
            {
                sink.Add(new MetricDescriptor(id, id, MetricUnit.BytesPerSecond), 1);
            }
        }

        public void Dispose()
        {
        }
    }

    private sealed class FakeProvider(string id, bool throws = false) : IMetricProvider
    {
        public string Id => id;

        public IReadOnlyList<MetricDescriptor> Metrics => [new($"{id}.value", id, MetricUnit.Percent)];

        public bool RequiresElevation => false;

        public bool Disposed { get; private set; }

        public List<long> Ticks { get; } = [];

        public void Sample(MetricSink sink)
        {
            Ticks.Add(sink.Tick);
            if (throws)
            {
                throw new InvalidOperationException("boom");
            }

            sink.Add($"{id}.value", 42);
        }

        public void Dispose() => Disposed = true;
    }
}
