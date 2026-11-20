// SPDX-License-Identifier: AGPL-3.0-only
namespace CouchSubs.Core.Dubbing;

/// <summary>
/// Optional frame-aligned transport from an external dialogue source separator
/// to the existing game PCM mixer. This class does NOT generate a dialogue stem.
/// Frames are matched by caller-supplied sequential IDs; old/out-of-order work
/// is never allowed to overwrite newer audio. The safe default is passthrough.
/// No model inference, allocations, or synchronization waits on audio callbacks.
/// </summary>
public sealed class SeparatedAudioFrameRouter
{
    private long lastFrameId = -1;
    private long accepted;
    private long fallback;
    private long rejectedOutOfOrder;

    public long AcceptedFrames => Interlocked.Read(ref accepted);
    public long FallbackFrames => Interlocked.Read(ref fallback);
    public long OutOfOrderFrames => Interlocked.Read(ref rejectedOutOfOrder);

    /// <summary>
    /// Optional source separation results are *only* used for the original
    /// 48kHz stereo frame with the matching ID; null/late/uncertain results
    /// preserve the original samples.
    /// </summary>
    public bool Process(
        long frameId, ReadOnlySpan<float> original, ReadOnlySpan<float> stem,
        Span<float> destination, float confidence, int ageMilliseconds,
        bool turkishDubActive)
    {
        if (destination.Length != original.Length)
            throw new ArgumentException("Output length must equal original.", nameof(destination));

        if (frameId <= Interlocked.Read(ref lastFrameId) || frameId < 0)
        {
            Interlocked.Increment(ref rejectedOutOfOrder);
            original.CopyTo(destination);
            return false;
        }

        Interlocked.Exchange(ref lastFrameId, frameId);
        bool ok = DialogueStemSafetyGate.TryApply(
            original, stem, destination, confidence, ageMilliseconds, turkishDubActive);
        if (ok) Interlocked.Increment(ref accepted);
        else Interlocked.Increment(ref fallback);
        return ok;
    }

    public void Reset()
    {
        Interlocked.Exchange(ref lastFrameId, -1);
        Interlocked.Exchange(ref accepted, 0);
        Interlocked.Exchange(ref fallback, 0);
        Interlocked.Exchange(ref rejectedOutOfOrder, 0);
    }
}
