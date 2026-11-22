// SPDX-License-Identifier: AGPL-3.0-only
using CouchSubs.Core.Dubbing;
using Xunit;

namespace CouchSubs.Core.Tests;

public sealed class DialogueStemSafetyGateTests
{
    private static readonly float[] Game = [0.5f, -0.3f, 0.4f, -0.2f];
    private static readonly float[] Dialogue = [0.2f, 0.2f, 0.2f, 0.2f];

    [Theory]
    [InlineData(0.50f, 10, true)]
    [InlineData(0.99f, 91, true)]
    [InlineData(0.99f, -1, true)]
    [InlineData(0.99f, 10, false)]
    [InlineData(float.NaN, 10, true)]
    [InlineData(1.01f, 10, true)]
    public void LowConfidenceStaleOrInactiveStemPassesThroughUnchanged(
        float confidence, int age, bool active)
    {
        var output = new float[Game.Length];
        Assert.False(DialogueStemSafetyGate.TryApply(
            Game, Dialogue, output, confidence, age, active));
        Assert.Equal(Game, output);
    }

    [Fact]
    public void MissingModelStemNeverChangesTheOriginal()
    {
        var output = new float[Game.Length];
        Assert.False(DialogueStemSafetyGate.TryApply(
            Game, [], output, 1f, 0, true));
        Assert.Equal(Game, output);
    }

    [Fact]
    public void HighConfidenceTimeAlignedStemIsSuppressedOnlyConservatively()
    {
        var output = new float[Game.Length];
        Assert.True(DialogueStemSafetyGate.TryApply(
            Game, Dialogue, output, 0.98f, 20, true));
        for (var i = 0; i < Game.Length; i++)
            Assert.Equal(Game[i] - Dialogue[i] * 0.25f, output[i], 5);
    }

    [Fact]
    public void InvalidOrHallucinatedStemCannotCorruptGameAudio()
    {
        var output = new float[Game.Length];
        Assert.False(DialogueStemSafetyGate.TryApply(
            Game, [float.NaN, 0, 0, 0], output, 1f, 0, true));
        Assert.Equal(Game, output);

        Assert.False(DialogueStemSafetyGate.TryApply(
            Game, [1f, 1f, 1f, 1f], output, 1f, 0, true));
        Assert.Equal(Game, output);
    }

    [Fact]
    public void ClippingOneChannelPreservesBothOriginalSamples()
    {
        float[] game = [0.99f, -0.1f];
        float[] stem = [-0.4f, -0.4f];
        var output = new float[2];
        Assert.True(DialogueStemSafetyGate.TryApply(game, stem, output, 1f, 0, true));
        Assert.Equal(game, output);
    }

    [Fact]
    public void InvalidDestinationSizeIsRejected()
    {
        Assert.Throws<ArgumentException>(() =>
            DialogueStemSafetyGate.TryApply(Game, Dialogue, new float[2],
                1f, 0, true));
    }
}
