// SPDX-License-Identifier: AGPL-3.0-only
using System.Buffers.Binary;
using CouchSubs.Core.Dubbing;

namespace CouchSubs.Core.Tests;

public sealed class InworldStemWireProtocolTests
{
    [Fact]
    public void InputUsesExactLittleEndianStereoContract()
    {
        var game = new float[InworldStemWireProtocol.FrameSamplesStereo];
        game[0] = .25f;
        game[1] = -.5f;
        var bytes = InworldStemWireProtocol.EncodeInput(12345, game);
        Assert.Equal(InworldStemWireProtocol.InputBytes, bytes.Length);
        Assert.Equal(12345, BinaryPrimitives.ReadInt64LittleEndian(bytes));
        Assert.Equal(.25f, BitConverter.Int32BitsToSingle(
            BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8, 4))));
        Assert.Equal(-.5f, BitConverter.Int32BitsToSingle(
            BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(12, 4))));
    }

    [Fact]
    public void ModelOutputIsDecodedOnlyWithValidIdentityAndPcm()
    {
        var bytes = new byte[InworldStemWireProtocol.OutputBytes];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, 9);
        for (var i = 0; i < InworldStemWireProtocol.StemSamplesMono; i++)
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8 + 4 * i, 4),
                BitConverter.SingleToInt32Bits(.125f));
        Assert.True(InworldStemWireProtocol.TryDecodeOutput(bytes, out var id, out var stem));
        Assert.Equal(9, id);
        Assert.Equal(960, stem.Length);
        Assert.All(stem, x => Assert.Equal(.125f, x));

        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8, 4),
            BitConverter.SingleToInt32Bits(float.NaN));
        Assert.False(InworldStemWireProtocol.TryDecodeOutput(bytes, out _, out _));
        Assert.False(InworldStemWireProtocol.TryDecodeOutput(bytes[..^1], out _, out _));
    }

    [Fact]
    public void ClippingAndNonFinitePcmAreNeverSentToExternalProcess()
    {
        var original = new float[InworldStemWireProtocol.FrameSamplesStereo];
        original[3] = 1.1f;
        Assert.Throws<ArgumentException>(() =>
            InworldStemWireProtocol.EncodeInput(1, original));
        original[3] = float.NegativeInfinity;
        Assert.Throws<ArgumentException>(() =>
            InworldStemWireProtocol.EncodeInput(1, original));
    }
}
