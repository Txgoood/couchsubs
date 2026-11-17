// SPDX-License-Identifier: AGPL-3.0-only
namespace CouchSubs.Shell;

/// <summary>
/// Non-ML, bounded cost analysis of PCM game audio. Estimates speech-like energy
/// and dynamics; cannot separate dialogue from music/effects or identify emotion.
/// Never stores source audio and never performs blocking work on the audio callback.
/// </summary>
public sealed class GameProsodyMeter
{
    private readonly object gate = new();
    private float energy;
    private float peak;
    private long lastTicks;
    private int packets;

    public void Push(ReadOnlySpan<short> pcm, int channels, int sampleRate)
    {
        if (channels < 1 || channels > 8 || sampleRate < 8000 || sampleRate > 192000 || pcm.IsEmpty)
            return;
        // Sample sparse points only, bounded even for unusually large PCM buffers.
        var stride = Math.Max(channels, (pcm.Length / 256 / channels) * channels);
        double squared = 0;
        int count = 0;
        for (int i = 0; i < pcm.Length; i += stride)
        {
            var value = pcm[i] / 32768.0;
            squared += value * value;
            count++;
        }
        if (count == 0) return;
        var rms = (float)Math.Sqrt(squared / count);
        lock (gate)
        {
            energy = packets == 0 ? rms : 0.75f * energy + 0.25f * rms;
            peak = Math.Max(rms, peak * 0.88f);
            packets = Math.Min(packets + 1, 100000);
            lastTicks = Environment.TickCount64;
        }
    }

    /// <summary>Recent loudness indication only: false for silence, stale or uncertain input.</summary>
    public bool RecentStrongEnergy()
    {
        lock (gate)
            return packets >= 3 && Environment.TickCount64 - lastTicks <= 1500
                && energy >= 0.09f && peak >= 0.16f;
    }

    public void Clear()
    {
        lock (gate)
        {
            energy = peak = 0;
            packets = 0;
            lastTicks = 0;
        }
    }
}
