// SPDX-License-Identifier: AGPL-3.0-only
using CouchSubs.Core.Dubbing;
using Xunit;

namespace CouchSubs.Core.Tests;

public sealed class SeparatedAudioFrameRouterTests
{
    private static readonly float[] Game = [0.5f, -0.3f, 0.4f, -0.2f];
    private static readonly float[] Stem = [0.2f, 0.2f, 0.2f, 0.2f];

    [Fact]
    public void MissingModelNeverChangesTheOriginalSound()
    {
        var router = new SeparatedAudioFrameRouter();
        var output = new float[Game.Length];
        Assert.False(router.Process(0, Game, [], output, 0, 1000, true));
        Assert.Equal(Game, output);
        Assert.Equal(1, router.FallbackFrames);
    }

    [Fact]
    public void HighConfidenceSynchronizedFrameMayApplyBoundedReduction()
    {
        var router = new SeparatedAudioFrameRouter();
        var output = new float[Game.Length];
        Assert.True(router.Process(5, Game, Stem, output, 0.98f, 20, true));
        Assert.Equal(1, router.AcceptedFrames);
        for (var i = 0; i < Game.Length; i++)
            Assert.Equal(Game[i] - Stem[i] * 0.25f, output[i], 5);
    }

    [Fact]
    public void StaleOrRepeatedFramesAreIgnoredEvenIfModelSaysConfident()
    {
        var router = new SeparatedAudioFrameRouter();
        var output = new float[Game.Length];
        Assert.False(router.Process(8, Game, [], output, 0, 100, true));
        Assert.False(router.Process(7, Game, Stem, output, 1, 0, true));
        Assert.False(router.Process(8, Game, Stem, output, 1, 0, true));
        Assert.Equal(2, router.OutOfOrderFrames);
        Assert.Equal(Game, output);
    }

    [Fact]
    public void ResetAllowsNewSessionStartingAtFrameZero()
    {
        var router = new SeparatedAudioFrameRouter();
        var output = new float[Game.Length];
        Assert.True(router.Process(200, Game, Stem, output, 1, 0, true));
        router.Reset();
        Assert.Equal(0, router.AcceptedFrames);
        Assert.True(router.Process(0, Game, Stem, output, 1, 0, true));
    }

    [Fact]
    public void NoDubKeepsFullSoundEvenWithValidStem()
    {
        var router = new SeparatedAudioFrameRouter();
        var output = new float[Game.Length];
        Assert.False(router.Process(0, Game, Stem, output, 1, 0, false));
        Assert.Equal(Game, output);
    }
}
