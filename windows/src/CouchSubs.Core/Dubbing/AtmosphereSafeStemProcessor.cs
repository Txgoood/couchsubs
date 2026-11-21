// SPDX-License-Identifier: AGPL-3.0-only
namespace CouchSubs.Core.Dubbing;

/// <summary>
/// Conservative postprocessor for a VERIFIED external dialogue-only stem.
/// Not an inference model: never guesses vocals from stereo game audio.
/// Preserves the original L-R (side) channel and softly ramps attenuation
/// of the common (mid) channel so stereo position and ambience remain intact.
/// </summary>
public sealed class AtmosphereSafeStemProcessor(int sampleRate = 48000)
{
    private readonly int attackFrames = Math.Max(1, sampleRate / 100); // 10ms
    private float gain;
    private long accepted;
    private long fallback;
    public long Accepted => Interlocked.Read(ref accepted);
    public long Fallback => Interlocked.Read(ref fallback);

    public bool Process(ReadOnlySpan<float> original, ReadOnlySpan<float> stem,
        Span<float> destination, float confidence, int ageMilliseconds,
        bool turkishDubActive)
    {
        if (destination.Length != original.Length)
            throw new ArgumentException("Output length must equal input.", nameof(destination));
        // Gate first with all existing invariants: confidence, age, finite PCM,
        // exact alignment and realistic stem energy.
        var validated = new float[original.Length];
        if (!DialogueStemSafetyGate.TryApply(original, stem, validated,
            confidence, ageMilliseconds, turkishDubActive))
        {
            original.CopyTo(destination);
            gain = 0;
            Interlocked.Increment(ref fallback);
            return false;
        }

        // A trusted dialogue stem may itself contain panned speech.
        // Preserve all L-R positional cues by attenuating only its shared mid.
        // At most 25% of the estimated center dialogue can be suppressed.
        var rampStep = DialogueStemSafetyGate.MaximumStemSuppression / attackFrames;
        for (var i = 0; i < original.Length; i += 2)
        {
            gain = MathF.Min(DialogueStemSafetyGate.MaximumStemSuppression, gain + rampStep);
            var midStem = (stem[i] + stem[i + 1]) * 0.5f;
            var attenuate = midStem * gain;
            // Side-channel difference is mathematically unchanged except if
            // an extreme peak would require limiting.
            var left = original[i] - attenuate;
            var right = original[i + 1] - attenuate;
            var peak = MathF.Max(MathF.Abs(left), MathF.Abs(right));
            if (peak > 1f)
            {
                // Avoid sudden clipping without modifying the original
                // side channel: reject this frame's attenuation.
                destination[i] = original[i];
                destination[i + 1] = original[i + 1];
            }
            else
            {
                destination[i] = left;
                destination[i + 1] = right;
            }
        }
        Interlocked.Increment(ref accepted);
        return true;
    }

    public void Reset()
    {
        gain = 0;
        Interlocked.Exchange(ref accepted, 0);
        Interlocked.Exchange(ref fallback, 0);
    }
}
