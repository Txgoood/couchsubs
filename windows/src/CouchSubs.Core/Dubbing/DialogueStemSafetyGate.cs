// SPDX-License-Identifier: AGPL-3.0-only
namespace CouchSubs.Core.Dubbing;

/// <summary>
/// Validates a time-aligned dialogue stem produced by an OPTIONAL future source
/// separation engine. Never infers dialogue itself and never caches game audio.
/// A missing/late/uncertain stem is a byte-for-byte game-audio passthrough.
/// This class is not wired to the real-time mixer until a measured inference
/// engine can provide synchronized PCM and bounded latency.
/// </summary>
public static class DialogueStemSafetyGate
{
    public const float MinimumConfidence = 0.90f;
    public const int MaximumAgeMilliseconds = 90;
    public const float MaximumStemSuppression = 0.25f;

    /// <summary>
    /// Processes interleaved stereo float PCM. `stem` must have the exact same
    /// sample count and time alignment as `original`. Returns true only if a
    /// conservative suppression was applied; otherwise copies original exactly.
    /// Caller must not overlap destination with stem/original.
    /// </summary>
    public static bool TryApply(
        ReadOnlySpan<float> original,
        ReadOnlySpan<float> stem,
        Span<float> destination,
        float confidence,
        int ageMilliseconds,
        bool turkishDubActive)
    {
        if (destination.Length != original.Length)
            throw new ArgumentException("Output must match original PCM length.", nameof(destination));

        // Preserve original on every failure (including model absence).
        if (!turkishDubActive || !float.IsFinite(confidence) ||
            confidence < MinimumConfidence || ageMilliseconds < 0 ||
            ageMilliseconds > MaximumAgeMilliseconds ||
            stem.Length != original.Length || original.Length == 0 ||
            original.Length % 2 != 0)
        {
            original.CopyTo(destination);
            return false;
        }

        for (var i = 0; i < original.Length; i++)
        {
            if (!float.IsFinite(original[i]) || !float.IsFinite(stem[i]) ||
                MathF.Abs(original[i]) > 1f || MathF.Abs(stem[i]) > 1f)
            {
                original.CopyTo(destination);
                return false;
            }
        }

        // Reject unrealistic stem estimates: hallucinated peaks or very weak
        // estimates must never distort the game ambience.
        double originalEnergy = 0;
        double stemEnergy = 0;
        for (var i = 0; i < original.Length; i++)
        {
            originalEnergy += original[i] * original[i];
            stemEnergy += stem[i] * stem[i];
        }
        if (originalEnergy < 1e-8 || stemEnergy < 1e-8 ||
            stemEnergy > originalEnergy * 1.20)
        {
            original.CopyTo(destination);
            return false;
        }

        // By design cannot erase dialogue, which could remove sirens, music,
        // and impact effects. The attenuation cap is intentionally 25%.
        for (var i = 0; i < original.Length; i++)
            destination[i] = Math.Clamp(original[i] -
                stem[i] * MaximumStemSuppression, -1f, 1f);
        return true;
    }
}
