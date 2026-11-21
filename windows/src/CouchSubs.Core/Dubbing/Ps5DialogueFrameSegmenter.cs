// SPDX-License-Identifier: AGPL-3.0-only
namespace CouchSubs.Core.Dubbing;

/// <summary>
/// Streaming PS5 48kHz PCM16 stereo -> fixed 20ms float32 frames for the
/// experimental Inworld worker. Call ONLY from a dedicated capture work queue:
/// it performs allocations and must never run on the native Opus callback.
/// Unsupported sample rates/channels fail without changing the original game
/// audio path. A future resampler must preserve proper sample timestamps.
/// </summary>
public sealed class Ps5DialogueFrameSegmenter
{
    public const int SamplesPerChannel = 960;
    private readonly float[] pending = new float[SamplesPerChannel * 2];
    private int pendingSamples;
    private long nextFrameId;
    private long rejected;

    public long Frames => nextFrameId;
    public long RejectedChunks => rejected;
    public int PendingSamples => pendingSamples;

    public bool Append(ReadOnlySpan<short> pcm, int channels, int sampleRate,
        Action<long, float[]> onFrame)
    {
        ArgumentNullException.ThrowIfNull(onFrame);
        if (sampleRate != 48000 || channels != 2 ||
            pcm.Length == 0 || pcm.Length % 2 != 0 || pcm.Length > 96000 * 2)
        {
            Interlocked.Increment(ref rejected);
            ResetPartial();
            return false;
        }
        for (var index = 0; index < pcm.Length;)
        {
            int take = Math.Min(pending.Length - pendingSamples, pcm.Length - index);
            for (int i = 0; i < take; i++)
                pending[pendingSamples + i] = pcm[index + i] / 32768f;
            pendingSamples += take;
            index += take;
            if (pendingSamples != pending.Length) continue;
            var finished = (float[])pending.Clone();
            var id = nextFrameId++;
            pendingSamples = 0;
            onFrame(id, finished);
        }
        return true;
    }

    public void Reset()
    {
        nextFrameId = 0;
        rejected = 0;
        ResetPartial();
    }

    private void ResetPartial()
    {
        pendingSamples = 0;
        Array.Clear(pending);
    }
}
