// SPDX-License-Identifier: AGPL-3.0-only
using CouchSubs.Core.Dubbing;
using Xunit;

namespace CouchSubs.Core.Tests;

public sealed class SpatialAtmosphereMixTests
{
    [Fact]
    public void BalancedSpatialModePreservesUnmixedStereoDirection()
    {
        var mixer = new AtmosphereMixEnvelope(48000);
        var originalDifference = 0.65f - (-0.15f);
        (float Left, float Right) output = default;
        // Warm up the envelope while a centered Turkish voice is present.
        for (int i = 0; i < 48000; i++)
            output = mixer.Step(0.65f, -0.15f, 0.08f, 0.08f,
                keepGame: 0.90f, speechVolume: 0.85f,
                mode: AtmosphereMixMode.SpatialBalanced);
        Assert.Equal(originalDifference, output.Left - output.Right, 3);
        Assert.InRange(mixer.CurrentGameGain, 0.899f, 0.901f);
    }

    [Fact]
    public void LegacyMixerRetainsPreviousFullChannelDuckingBehavior()
    {
        var legacy = new AtmosphereMixEnvelope(48000);
        (float Left, float Right) output = default;
        for (int i = 0; i < 48000; i++)
            output = legacy.Step(0.65f, -0.15f, 0.08f, 0.08f,
                0.82f, 0.85f, AtmosphereMixMode.Legacy);
        Assert.Equal((0.65f - (-0.15f)) * 0.82f,
            output.Left - output.Right, 3);
    }

    [Fact]
    public void SafeOriginalDoesNotAttenuateStereoGameSignalWhileSpeaking()
    {
        var safe = new AtmosphereMixEnvelope(48000);
        for (int i = 0; i < 48000; i++)
            safe.Step(0.55f, 0.15f, 0.05f, 0.05f,
                0.35f, 0.8f, AtmosphereMixMode.SafeOriginal);
        Assert.Equal(1f, safe.CurrentGameGain);
    }

    [Fact]
    public void WithoutSpeechOriginalStereoIsUnchanged()
    {
        var spatial = new AtmosphereMixEnvelope(48000);
        var result = spatial.Step(0.3f, -0.6f, 0f, 0f,
            0.9f, 0.85f, AtmosphereMixMode.SpatialBalanced);
        Assert.Equal(0.3f, result.Left, 4);
        Assert.Equal(-0.6f, result.Right, 4);
    }

    [Fact]
    public void AllModesKeepFiniteOutputAndDoNotClip()
    {
        foreach (var mode in Enum.GetValues<AtmosphereMixMode>())
        {
            var mixer = new AtmosphereMixEnvelope();
            for (int i = 0; i < 3000; i++)
            {
                var (left, right) = mixer.Step(0.98f, -0.98f, 0.9f, 0.9f,
                    0.35f, 1f, mode);
                Assert.InRange(left, -1f, 1f);
                Assert.InRange(right, -1f, 1f);
            }
        }
    }
}
