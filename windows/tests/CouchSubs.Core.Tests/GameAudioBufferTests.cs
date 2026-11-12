// SPDX-License-Identifier: AGPL-3.0-only

using CouchSubs.Core.Dubbing;
using Xunit;

namespace CouchSubs.Core.Tests;

public class GameAudioBufferTests
{
    [Fact]
    public void ReadsSamplesInSequence()
    {
        var buffer = new GameAudioBuffer(8);
        buffer.Write([0.1f, 0.2f, 0.3f]);
        var output = new float[3];
        buffer.Read(output);
        Assert.Equal(new float[] { 0.1f, 0.2f, 0.3f }, output);
        Assert.Equal(0, buffer.BufferedSamples);
    }

    [Fact]
    public void OverrunDropsOldSamplesInsteadOfIncreasingDelay()
    {
        var buffer = new GameAudioBuffer(4);
        buffer.Write([1f, 2f, 3f]);
        buffer.Write([4f, 5f, 6f]);
        var output = new float[4];
        buffer.Read(output);
        Assert.Equal(new float[] { 3f, 4f, 5f, 6f }, output);
    }

    [Fact]
    public void HugePacketKeepsOnlyLatestSamples()
    {
        var buffer = new GameAudioBuffer(3);
        buffer.Write([1f, 2f, 3f, 4f, 5f]);
        var output = new float[3];
        buffer.Read(output);
        Assert.Equal(new float[] { 3f, 4f, 5f }, output);
    }

    [Fact]
    public void UnderrunPadsWithSilence()
    {
        var buffer = new GameAudioBuffer(4);
        buffer.Write([0.7f]);
        var output = new float[3];
        buffer.Read(output);
        Assert.Equal(new float[] { 0.7f, 0f, 0f }, output);
    }

    [Fact]
    public void ClearRemovesStaleGameAudio()
    {
        var buffer = new GameAudioBuffer(4);
        buffer.Write([1f, 2f]);
        buffer.Clear();
        var output = new float[2];
        buffer.Read(output);
        Assert.Equal(new float[] { 0f, 0f }, output);
    }
}
