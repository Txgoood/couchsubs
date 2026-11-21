// SPDX-License-Identifier: AGPL-3.0-only
namespace CouchSubs.Core.Dubbing;

/// <summary>
/// Frame-accurate, bounded 20ms join for a future streaming voice isolator.
/// It NEVER performs inference and never touches a real-time audio callback.
/// Missing, invalid, out-of-order, or late stems become unchanged game PCM.
/// </summary>
public readonly record struct JoinedDialogueFrame(
    long FrameId, float[] Original, float[]? DialogueStem,
    float Confidence, int AgeMilliseconds, bool TurkishDubActive)
{
    public bool HasTrustedStem => DialogueStem is not null;
}

public sealed class DialogueStemSynchronizer
{
    public const int SamplesPerFrame = 960 * 2; // 20ms stereo @48kHz
    public const int MaxPendingFrames = 6;     // bounded 120ms, never growing
    private sealed class Pending(long id, float[] original, long capturedMs)
    {
        public long Id { get; } = id;
        public float[] Original { get; } = original;
        public long CapturedMs { get; } = capturedMs;
        public float[]? Stem;
        public float Confidence;
        public bool DubActive;
        public bool Completed;
    }

    private readonly object gate = new();
    private readonly Queue<Pending> queue = new();
    private long lastFrameId = -1;
    private long fallback;
    private long joined;

    public int BufferedFrames { get { lock (gate) return queue.Count; } }
    public long FallbackFrames => Interlocked.Read(ref fallback);
    public long JoinedFrames => Interlocked.Read(ref joined);

    /// <summary>Adds an original frame; returns any forced safe fallbacks.</summary>
    public IReadOnlyList<JoinedDialogueFrame> OfferOriginal(
        long frameId, ReadOnlySpan<float> original, long capturedMs)
    {
        if (frameId < 0 || original.Length != SamplesPerFrame)
            throw new ArgumentException("Expected valid 20ms stereo frame.");
        if (!Finite(original))
            throw new ArgumentException("Non-finite or clipped input PCM.");
        lock (gate)
        {
            if (frameId <= lastFrameId)
                throw new ArgumentOutOfRangeException(nameof(frameId), "Frame IDs must increase.");
            lastFrameId = frameId;
            var ready = new List<JoinedDialogueFrame>(1);
            if (queue.Count >= MaxPendingFrames)
            {
                var oldest = queue.Dequeue();
                ready.Add(Fallback(oldest, ageMs: int.MaxValue));
            }
            queue.Enqueue(new Pending(frameId, original.ToArray(), capturedMs));
            return ready;
        }
    }

    /// <summary>
    /// Model worker results may arrive out of order. Never release newer
    /// audio ahead of an older frame: Drain() enforces the original order.
    /// </summary>
    public bool OfferStem(long frameId, ReadOnlySpan<float> monoSpeech,
        float confidence, long returnedMs, bool turkishDubActive)
    {
        if (monoSpeech.Length != SamplesPerFrame / 2 || !Finite(monoSpeech) ||
            !float.IsFinite(confidence) || confidence < DialogueStemSafetyGate.MinimumConfidence)
            return false;
        lock (gate)
        {
            foreach (var pending in queue)
            {
                if (pending.Id != frameId) continue;
                if (pending.Completed || returnedMs < pending.CapturedMs ||
                    returnedMs - pending.CapturedMs > DialogueStemSafetyGate.MaximumAgeMilliseconds)
                    return false;
                // Mono analysis -> identical L/R dialogue center. The
                // original interchannel L-R difference remains in Original.
                var expanded = new float[SamplesPerFrame];
                for (var i = 0; i < monoSpeech.Length; i++)
                {
                    expanded[2 * i] = monoSpeech[i];
                    expanded[2 * i + 1] = monoSpeech[i];
                }
                pending.Stem = expanded;
                pending.Confidence = confidence;
                pending.DubActive = turkishDubActive;
                pending.Completed = true;
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Call from a worker/timer, not a live audio callback. A result is ready
    /// when its model stem arrived or its 90ms deadline expired.
    /// </summary>
    public IReadOnlyList<JoinedDialogueFrame> Drain(long nowMs)
    {
        lock (gate)
        {
            var ready = new List<JoinedDialogueFrame>(queue.Count);
            while (queue.Count != 0)
            {
                var oldest = queue.Peek();
                var age = nowMs - oldest.CapturedMs;
                if (age < 0 || age > DialogueStemSafetyGate.MaximumAgeMilliseconds)
                {
                    queue.Dequeue();
                    ready.Add(Fallback(oldest, age < 0 ? int.MaxValue :
                        (int)Math.Min(int.MaxValue, age)));
                    continue;
                }
                if (!oldest.Completed) break;
                queue.Dequeue();
                Interlocked.Increment(ref joined);
                ready.Add(new JoinedDialogueFrame(oldest.Id, oldest.Original,
                    oldest.Stem, oldest.Confidence, (int)age, oldest.DubActive));
            }
            return ready;
        }
    }

    public IReadOnlyList<JoinedDialogueFrame> FlushOriginals()
    {
        lock (gate)
        {
            var outputs = new List<JoinedDialogueFrame>(queue.Count);
            while (queue.Count > 0)
                outputs.Add(Fallback(queue.Dequeue(), int.MaxValue));
            return outputs;
        }
    }

    public void Reset()
    {
        lock (gate)
        {
            queue.Clear();
            lastFrameId = -1;
            Interlocked.Exchange(ref fallback, 0);
            Interlocked.Exchange(ref joined, 0);
        }
    }

    private JoinedDialogueFrame Fallback(Pending pending, int ageMs)
    {
        Interlocked.Increment(ref fallback);
        return new JoinedDialogueFrame(pending.Id, pending.Original, null,
            0f, ageMs, false);
    }

    private static bool Finite(ReadOnlySpan<float> audio)
    {
        foreach (var sample in audio)
            if (!float.IsFinite(sample) || MathF.Abs(sample) > 1f)
                return false;
        return true;
    }
}
