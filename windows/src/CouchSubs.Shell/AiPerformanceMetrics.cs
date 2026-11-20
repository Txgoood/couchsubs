// SPDX-License-Identifier: AGPL-3.0-only
using System.Diagnostics;

namespace CouchSubs.Shell;

/// <summary>Metadata-only model performance metrics: no game PCM, subtitles, or voices are stored.</summary>
public readonly record struct AiPerformanceSnapshot(
    long AudioPackets, long InferenceCalls, long InferenceErrors,
    double AverageInferenceMs, double PeakInferenceMs);

public sealed class AiPerformanceMetrics
{
    private long packets;
    private long calls;
    private long errors;
    private long ticks;
    private long peakTicks;

    public void RecordPacket() => Interlocked.Increment(ref packets);
    public void RecordError() => Interlocked.Increment(ref errors);

    public void RecordInference(long startedTimestamp)
    {
        var delta = Math.Max(0, Stopwatch.GetTimestamp() - startedTimestamp);
        Interlocked.Add(ref ticks, delta);
        Interlocked.Increment(ref calls);
        long peak;
        while (delta > (peak = Interlocked.Read(ref peakTicks)))
            if (Interlocked.CompareExchange(ref peakTicks, delta, peak) == peak) break;
    }

    public AiPerformanceSnapshot Snapshot()
    {
        var count = Interlocked.Read(ref calls);
        double milliseconds = 1000d / Stopwatch.Frequency;
        return new AiPerformanceSnapshot(
            Interlocked.Read(ref packets), count, Interlocked.Read(ref errors),
            count == 0 ? 0 : Interlocked.Read(ref ticks) * milliseconds / count,
            Interlocked.Read(ref peakTicks) * milliseconds);
    }

    public void Reset()
    {
        Interlocked.Exchange(ref packets, 0);
        Interlocked.Exchange(ref calls, 0);
        Interlocked.Exchange(ref errors, 0);
        Interlocked.Exchange(ref ticks, 0);
        Interlocked.Exchange(ref peakTicks, 0);
    }
}
