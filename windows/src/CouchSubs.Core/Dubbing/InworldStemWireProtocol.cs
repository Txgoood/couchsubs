// SPDX-License-Identifier: AGPL-3.0-only
using System.Buffers.Binary;

namespace CouchSubs.Core.Dubbing;

/// <summary>
/// Strict little-endian interprocess wire contract for the experimental
/// Inworld 48kHz stereo-to-mono streaming model worker.
/// The audio callback must NEVER perform process IO or wait for a reply.
/// </summary>
public static class InworldStemWireProtocol
{
    public const int FrameSamplesStereo = DialogueStemSynchronizer.SamplesPerFrame;
    public const int StemSamplesMono = FrameSamplesStereo / 2;
    public const int InputBytes = sizeof(long) + FrameSamplesStereo * sizeof(float);
    public const int OutputBytes = sizeof(long) + StemSamplesMono * sizeof(float);

    public static byte[] EncodeInput(long frameId, ReadOnlySpan<float> stereo)
    {
        if (frameId < 0 || stereo.Length != FrameSamplesStereo)
            throw new ArgumentException("Expected 960 48k stereo frames and a nonnegative ID.");
        var destination = new byte[InputBytes];
        BinaryPrimitives.WriteInt64LittleEndian(destination.AsSpan(0, 8), frameId);
        for (var i = 0; i < stereo.Length; i++)
        {
            if (!float.IsFinite(stereo[i]) || MathF.Abs(stereo[i]) > 1f)
                throw new ArgumentException("Invalid original PCM");
            BinaryPrimitives.WriteInt32LittleEndian(destination.AsSpan(8 + 4 * i, 4),
                BitConverter.SingleToInt32Bits(stereo[i]));
        }
        return destination;
    }

    public static bool TryDecodeOutput(ReadOnlySpan<byte> data,
        out long frameId, out float[] stem)
    {
        frameId = -1;
        stem = [];
        if (data.Length != OutputBytes) return false;
        var id = BinaryPrimitives.ReadInt64LittleEndian(data[..8]);
        if (id < 0) return false;
        var decoded = new float[StemSamplesMono];
        for (var i = 0; i < decoded.Length; i++)
        {
            var sample = BitConverter.Int32BitsToSingle(
                BinaryPrimitives.ReadInt32LittleEndian(data.Slice(8 + 4 * i, 4)));
            if (!float.IsFinite(sample) || MathF.Abs(sample) > 1f)
                return false;
            decoded[i] = sample;
        }
        frameId = id;
        stem = decoded;
        return true;
    }
}
