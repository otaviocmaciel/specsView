using System.Runtime.InteropServices;
using SpecsView.Sensors.Native;

namespace SpecsView.Sensors.Pdh;

/// <summary>
/// One PDH query shared by every PDH-based provider, so all counters are collected with a single
/// <c>PdhCollectQueryData</c> call per tick.
/// </summary>
public sealed class PdhQuery : IDisposable
{
    private nint _handle;
    private long _lastTick = -1;

    public PdhQuery()
    {
        Check(PdhNative.PdhOpenQuery(null, 0, out _handle), "PdhOpenQuery");
    }

    /// <summary>Adds a counter by its English path, e.g. <c>\Processor Information(_Total)\% Processor Utility</c>.</summary>
    public PdhCounter AddCounter(string englishPath)
    {
        Check(PdhNative.PdhAddEnglishCounter(_handle, englishPath, 0, out var counter), $"PdhAddEnglishCounter({englishPath})");
        return new PdhCounter(counter);
    }

    public bool TryAddCounter(string englishPath, out PdhCounter? counter)
    {
        if (PdhNative.PdhAddEnglishCounter(_handle, englishPath, 0, out var handle) == PdhNative.ErrorSuccess)
        {
            counter = new PdhCounter(handle);
            return true;
        }

        counter = null;
        return false;
    }

    /// <summary>Collects data at most once per scheduler tick, no matter how many providers ask.</summary>
    public void CollectFor(long tick)
    {
        if (tick == _lastTick)
        {
            return;
        }

        _lastTick = tick;
        PdhNative.PdhCollectQueryData(_handle);
    }

    private static void Check(uint status, string call)
    {
        if (status != PdhNative.ErrorSuccess)
        {
            throw new InvalidOperationException($"{call} failed with 0x{status:X8}.");
        }
    }

    public void Dispose()
    {
        if (_handle != 0)
        {
            PdhNative.PdhCloseQuery(_handle);
            _handle = 0;
        }
    }
}

/// <summary>Receives one instance of a wildcard counter; the name span is only valid during the call.</summary>
public delegate void PdhInstanceHandler(ReadOnlySpan<char> instance, double value);

public sealed class PdhCounter
{
    private readonly nint _handle;
    private byte[] _buffer = [];

    internal PdhCounter(nint handle) => _handle = handle;

    /// <summary>Reads every instance of a wildcard counter (e.g. <c>\PhysicalDisk(*)\% Idle Time</c>).</summary>
    public unsafe bool TryReadInstances(PdhInstanceHandler handler)
    {
        // The instance list can grow between the size query and the read; retry a few times.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            uint size = 0;
            var status = PdhNative.PdhGetFormattedCounterArray(_handle, PdhNative.PdhFmtDouble, ref size, out _, null);
            if (status != PdhNative.PdhMoreData)
            {
                return false;
            }

            if (_buffer.Length < size)
            {
                _buffer = new byte[size * 2];
            }

            size = (uint)_buffer.Length;
            fixed (byte* buffer = _buffer)
            {
                status = PdhNative.PdhGetFormattedCounterArray(_handle, PdhNative.PdhFmtDouble, ref size, out var count, buffer);
                if (status == PdhNative.PdhMoreData)
                {
                    continue;
                }

                if (status != PdhNative.ErrorSuccess)
                {
                    return false;
                }

                var items = (PdhNative.PdhFmtCounterValueItem*)buffer;
                for (var i = 0; i < count; i++)
                {
                    if (items[i].Value.CStatus is PdhNative.PdhCstatusValidData or PdhNative.PdhCstatusNewData)
                    {
                        handler(MemoryMarshal.CreateReadOnlySpanFromNullTerminated(items[i].Name), items[i].Value.DoubleValue);
                    }
                }

                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Rate counters need two collections before they have data, so the first tick returns false.
    /// </summary>
    public bool TryGetValue(out double value)
    {
        var status = PdhNative.PdhGetFormattedCounterValue(_handle, PdhNative.PdhFmtDouble, out _, out var result);
        if (status == PdhNative.ErrorSuccess &&
            result.CStatus is PdhNative.PdhCstatusValidData or PdhNative.PdhCstatusNewData)
        {
            value = result.DoubleValue;
            return true;
        }

        value = 0;
        return false;
    }
}
