// SPDX-License-Identifier: AGPL-3.0-only
namespace CouchSubs.Shell;

/// <summary>
/// Lightweight audio descriptors, NOT a speech/voice-emotion classifier.
/// The input is mixed PS5 game audio; effects and music are not separated.
/// </summary>
public readonly record struct ProsodySnapshot(float Rms, float PeakRms, float EnergyVariation,
    float ZeroCrossing, bool Fresh, int Packets);

/// <summary>
/// Bounded, thread-safe and allocation-free sampling of mixed Remote Play PCM.
/// Never records audio, does not access a network or block on inference.
/// </summary>
public sealed class GameProsodyMeter
{
    private readonly object gate = new();
    private float energy;
    private float peak;
    private float variation;
    private float crossings;
    private long lastTicks;
    private int packets;

    public void Push(ReadOnlySpan<short> pcm, int channels, int sampleRate)
    {
        if (channels < 1 || channels > 8 || sampleRate < 8000 || sampleRate > 192000 || pcm.IsEmpty)
            return;
        // Sample <= 256 points in one channel, with no heap allocation.
        var frames = pcm.Length / channels;
        var stride = Math.Max(1, (frames + 255) / 256);
        double squared = 0;
        int count = 0, signs = 0;
        int lastSign = 0;
        for (int frame = 0; frame < frames; frame += stride)
        {
            var value = pcm[frame * channels] / 32768.0;
            squared += value * value;
            var sign = value > 0.015 ? 1 : value < -0.015 ? -1 : 0;
            if (sign != 0 && lastSign != 0 && sign != lastSign) signs++;
            if (sign != 0) lastSign = sign;
            count++;
        }
        if (count == 0) return;
        var rms = (float)Math.Sqrt(squared / count);
        var zcr = count > 1 ? (float)signs / (count - 1) : 0f;
        lock (gate)
        {
            var delta = Math.Abs(rms - energy);
            variation = packets == 0 ? 0f : variation * 0.8f + delta * 0.2f;
            energy = packets == 0 ? rms : 0.75f * energy + 0.25f * rms;
            peak = Math.Max(rms, peak * 0.88f);
            crossings = packets == 0 ? zcr : crossings * 0.8f + zcr * 0.2f;
            packets = Math.Min(packets + 1, 100000);
            lastTicks = Environment.TickCount64;
        }
    }

    public ProsodySnapshot Snapshot()
    {
        lock (gate)
            return new ProsodySnapshot(energy, peak, variation, crossings,
                packets >= 3 && Environment.TickCount64 - lastTicks <= 1500, packets);
    }

    /// <summary>Legacy loudness indicator; no claim of identifying speech.</summary>
    public bool RecentStrongEnergy()
    {
        var state = Snapshot();
        return state.Fresh && state.Rms >= 0.09f && state.PeakRms >= 0.16f;
    }

    /// <summary>
    /// Conservative gate to avoid treating silence or steady music as emphatic dialogue.
    /// Output only supports text-based emotion selection, not independent emotion recognition.
    /// </summary>
    public bool RecentDynamicEnergy()
    {
        var state = Snapshot();
        return state.Fresh && state.Rms >= 0.06f &&
            state.PeakRms >= 0.12f && state.EnergyVariation >= 0.01f &&
            state.ZeroCrossing >= 0.005f && state.ZeroCrossing <= 0.75f;
    }

    public void Clear()
    {
        lock (gate)
        {
            energy = peak = variation = crossings = 0;
            packets = 0;
            lastTicks = 0;
        }
    }
}
