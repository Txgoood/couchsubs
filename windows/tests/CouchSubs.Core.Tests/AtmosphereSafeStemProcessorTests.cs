// SPDX-License-Identifier: AGPL-3.0-only
using CouchSubs.Core.Dubbing;

namespace CouchSubs.Core.Tests;

public sealed class AtmosphereSafeStemProcessorTests
{
    [Fact]
    public void WithoutTrustworthyModelResultOriginalStereoIsIdentical()
    {
        var safe = new AtmosphereSafeStemProcessor();
        var game = new float[] { .5f, -.3f, .4f, -.2f };
        var output = new float[game.Length];
        Assert.False(safe.Process(game, [], output, 0, 100, true));
        Assert.Equal(game, output);
        Assert.False(safe.Process(game, new float[] { .2f, .2f, .2f, .2f }, output, .95f, 1, false));
        Assert.Equal(game, output);
        Assert.Equal(2, safe.Fallback);
    }

    [Fact]
    public void VerifiedStemPreservesStereoDifferenceAndNeverSuppressesMoreThanQuarter()
    {
        var safe = new AtmosphereSafeStemProcessor(sampleRate: 1000);
        var game = Enumerable.Repeat(new float[] { .5f, -.3f }, 40)
            .SelectMany(x => x).ToArray();
        var stem = Enumerable.Repeat(.2f, game.Length).ToArray();
        var output = new float[game.Length];
        Assert.True(safe.Process(game, stem, output, .98f, 20, true));
        for (var i = 0; i < game.Length; i += 2)
        {
            Assert.Equal(game[i] - game[i + 1], output[i] - output[i + 1], 5);
            Assert.InRange(game[i] - output[i], 0f, .05001f);
        }
        Assert.True(output[0] > output[^2]); // first sample gently ramps, unlike a hard step.
    }

    [Fact]
    public void HallucinatedOrStaleStemNeverModifiesTheGame()
    {
        var safe = new AtmosphereSafeStemProcessor();
        var game = new float[] { .4f, .1f, -.2f, -.3f };
        var output = new float[4];
        Assert.False(safe.Process(game, new float[] { 1f, 1f, 1f, 1f }, output, 1, 0, true));
        Assert.Equal(game, output);
        Assert.False(safe.Process(game, new float[] { .2f, .2f, .2f, .2f }, output, 1, 91, true));
        Assert.Equal(game, output);
    }

    [Fact]
    public void ResetRestartsGentleAttack()
    {
        var safe = new AtmosphereSafeStemProcessor(sampleRate: 1000);
        var game = new float[] { .4f, .2f };
        var stem = new float[] { .1f, .1f };
        var output = new float[2];
        Assert.True(safe.Process(game, stem, output, .98f, 10, true));
        var first = output[0];
        safe.Reset();
        Assert.True(safe.Process(game, stem, output, .98f, 10, true));
        Assert.Equal(first, output[0], 5);
    }
}
