// SPDX-License-Identifier: AGPL-3.0-only
namespace CouchSubs.Shell;

/// <summary>
/// Cheap temporal gate for *possible* voice-like audio in a mixed game soundtrack.
/// This is NOT a voice activity neural model: music and effects can pass the gate.
/// External synchronization is required: GameProsodyMeter owns the lock.
/// </summary>
public sealed class SpeechCandidateTracker
{
    private double evidenceMs;
    private long lastCandidateTicks;

    /// <summary>
    /// Update once per decoded PCM packet. Rejects DC, silence and stable tones,
    /// and requires sustained dynamic audio instead of a single loud transient.
    /// </summary>
    public void Observe(float rms, float zeroCrossing, float change, double packetMs, long nowTicks)
    {
        if (!float.IsFinite(rms) || !float.IsFinite(zeroCrossing) || !float.IsFinite(change) ||
            !double.IsFinite(packetMs) || packetMs <= 0)
            return;

        var duration = Math.Min(packetMs, 100);
        var plausible = rms >= 0.025f && rms <= 0.8f &&
            zeroCrossing >= 0.015f && zeroCrossing <= 0.55f && change >= 0.004f;

        if (plausible)
        {
            evidenceMs = Math.Min(1200, evidenceMs + duration);
            lastCandidateTicks = nowTicks;
        }
        else
        {
            evidenceMs = Math.Max(0, evidenceMs - duration * 1.5);
        }
    }

    public bool IsCandidate(long nowTicks) =>
        evidenceMs >= 240 && nowTicks >= lastCandidateTicks &&
        nowTicks - lastCandidateTicks <= 450;

    public void Reset()
    {
        evidenceMs = 0;
        lastCandidateTicks = 0;
    }
}
