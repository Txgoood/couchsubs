// SPDX-License-Identifier: AGPL-3.0-only
using CouchSubs.Core.Dubbing;

namespace CouchSubs.Core.Tests;

public sealed class Ps5DialogueFrameSegmenterTests
{
    [Fact]
    public void SplitsVariableSizedNativePacketsIntoOrderedAlignedFrames()
    {
        var segmenter = new Ps5DialogueFrameSegmenter();
        var emitted = new List<(long, float[])>();
        var input = Enumerable.Range(0, 960 * 2 * 3).Select(i => (short)((i % 256) - 128)).ToArray();
        foreach (var slice in new[] { (0, 240), (240, 2000), (2240, 600),
                     (2840, input.Length - 2840) })
        {
            Assert.True(segmenter.Append(input.AsSpan(slice.Item1, slice.Item2), 2, 48000,
                (id, frame) => emitted.Add((id, frame))));
        }
        Assert.Equal(3, emitted.Count);
        Assert.Equal([0L, 1L, 2L], emitted.Select(f => f.Item1).ToArray());
        for (var i = 0; i < 3; i++)
            Assert.Equal(input.AsSpan(i * 1920, 1920).ToArray().Select(x => x / 32768f),
                emitted[i].Item2);
        Assert.Equal(0, segmenter.PendingSamples);
    }

    [Fact]
    public void UnsupportedSampleRateNeverClaimsFrameIsSafe()
    {
        var segmenter = new Ps5DialogueFrameSegmenter();
        var frames = new List<long>();
        Assert.True(segmenter.Append(new short[960], 2, 48000,
            (id, _) => frames.Add(id)));
        Assert.False(segmenter.Append(new short[960], 2, 44100,
            (id, _) => frames.Add(id)));
        Assert.Equal(0, segmenter.PendingSamples);
        Assert.Equal(1, segmenter.RejectedChunks);
        Assert.Empty(frames);
    }

    [Fact]
    public void ResetRestartsFrameIdentityForNewSession()
    {
        var segmenter = new Ps5DialogueFrameSegmenter();
        segmenter.Append(new short[1920], 2, 48000, (_, _) => { });
        segmenter.Reset();
        long outputId = -1;
        segmenter.Append(new short[1920], 2, 48000, (id, _) => outputId = id);
        Assert.Equal(0, outputId);
    }
}
