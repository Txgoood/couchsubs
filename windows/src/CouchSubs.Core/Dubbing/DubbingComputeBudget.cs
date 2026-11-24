// SPDX-License-Identifier: AGPL-3.0-only
namespace CouchSubs.Core.Dubbing;

/// <summary>
/// Safe, deterministic resource policy for Windows game dubbing. It never
/// enables a GPU backend without a separately packaged and verified provider.
/// </summary>
public static class DubbingComputeBudget
{
    public const string CpuThreadsSetting = "COUCHSUBS_TTS_THREADS";
    public const string OnnxThreadsSetting = "COUCHSUBS_ONNX_THREADS";

    public static int CpuThreads(int logicalProcessors, string? requested)
    {
        var available = Math.Max(1, logicalProcessors);
        // Leave CPU headroom for PS5 capture, OCR, translation and the mixer.
        var recommended = Math.Clamp(available / 4, 1, 4);
        if (int.TryParse(requested, out var selected) && selected >= 1 && selected <= 8)
            return Math.Clamp(selected, 1, available);
        return recommended;
    }

    public static int OnnxThreads(int logicalProcessors, string? requested)
    {
        var available = Math.Max(1, logicalProcessors);
        if (int.TryParse(requested, out var selected) && selected >= 1 && selected <= 8)
            return Math.Clamp(selected, 1, available);
        // Encoder and decoder each own a pool. Keep both bounded during Remote Play.
        return Math.Clamp(available / 6, 1, 4);
    }

    public static bool UseGpu(bool providerPackaged, bool modelValidated,
        bool runtimeVerified, bool userOptedIn) =>
        providerPackaged && modelValidated && runtimeVerified && userOptedIn;
}

/// <summary>Metadata-only latency snapshot, never stores raw audio or subtitles.</summary>
public sealed class DubbingLatencyTracker
{
    private long calls;
    private long totalMs;
    private long slowCalls;
    private long peakMs;
    public void Record(int milliseconds)
    {
        if (milliseconds < 0 || milliseconds > 600000) return;
        Interlocked.Increment(ref calls);
        Interlocked.Add(ref totalMs, milliseconds);
        if (milliseconds > 1500) Interlocked.Increment(ref slowCalls);
        long old;
        while (milliseconds > (old = Interlocked.Read(ref peakMs)) &&
               Interlocked.CompareExchange(ref peakMs, milliseconds, old) != old) { }
    }
    public (long Calls, double AverageMs, long PeakMs, long SlowCalls) Snapshot()
    {
        var count = Interlocked.Read(ref calls);
        return (count, count == 0 ? 0 : (double)Interlocked.Read(ref totalMs) / count,
            Interlocked.Read(ref peakMs), Interlocked.Read(ref slowCalls));
    }
    public void Reset()
    {
        Interlocked.Exchange(ref calls, 0);
        Interlocked.Exchange(ref totalMs, 0);
        Interlocked.Exchange(ref slowCalls, 0);
        Interlocked.Exchange(ref peakMs, 0);
    }
}
