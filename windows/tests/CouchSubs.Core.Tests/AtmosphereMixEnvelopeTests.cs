// SPDX-License-Identifier: AGPL-3.0-only
using CouchSubs.Core.Dubbing;
using Xunit;

namespace CouchSubs.Core.Tests;

public sealed class AtmosphereMixEnvelopeTests
{
    [Fact]
    public void AmbienceRemainsFullWhenTurkishVoiceIsSilent()
    {
        var mix = new AtmosphereMixEnvelope(48000);
        for (int i = 0; i < 4000; i++)
        {
            var result = mix.Step(0.4f, -0.3f, 0f, 0f);
            Assert.Equal(0.4f, result.Left, 5);
            Assert.Equal(-0.3f, result.Right, 5);
        }
    }

    [Fact]
    public void GameAtmosphereNeverDucksBelowConfiguredFloorDuringSpeech()
    {
        var mix = new AtmosphereMixEnvelope(48000);
        for (int i = 0; i < 6000; i++)
            mix.Step(0.2f, -0.2f, 0.3f, 0.3f, keepGame: 0.82f);
        Assert.InRange(mix.CurrentGameGain, 0.819f, 0.83f);
        Assert.True(mix.CurrentVoiceGain > 0.8f);
    }

    [Fact]
    public void ReleaseIsSmoothAndReturnsToNormalGameAudio()
    {
        var mix = new AtmosphereMixEnvelope(48000);
        for (int i = 0; i < 2500; i++) mix.Step(0.2f, -0.2f, 0.2f, 0.2f);
        var before = mix.CurrentGameGain;
        mix.Step(0.2f, -0.2f, 0f, 0f);
        Assert.True(mix.CurrentGameGain > before);
        Assert.True(mix.CurrentGameGain - before < 0.002f);
        for (int i = 0; i < 12000; i++) mix.Step(0.2f, -0.2f, 0f, 0f);
        Assert.Equal(1f, mix.CurrentGameGain, 3);
    }

    [Fact]
    public void StereoDirectionIsPreservedWithoutMonoCollapse()
    {
        var mix = new AtmosphereMixEnvelope(48000);
        var result = mix.Step(0.4f, -0.2f, 0f, 0f);
        Assert.Equal(0.4f, result.Left, 5);
        Assert.Equal(-0.2f, result.Right, 5);
    }

    [Fact]
    public void LimiterClampsPeaksWithoutReversingStereo()
    {
        var mix = new AtmosphereMixEnvelope(48000);
        for (int i = 0; i < 1000; i++) mix.Step(0.8f, -0.8f, 0.9f, 0.9f, keepGame: 1f);
        var result = mix.Step(0.8f, -0.8f, 0.9f, 0.9f, keepGame: 1f);
        Assert.InRange(result.Left, -1f, 1f);
        Assert.InRange(result.Right, -1f, 1f);
        Assert.True(result.Left > result.Right);
    }

    [Fact]
    public void InvalidDecoderSamplesNeverPoisonTheStereoOutput()
    {
        var mix = new AtmosphereMixEnvelope();
        var damaged = mix.Step(float.NaN, 0.3f, float.PositiveInfinity, 0.1f);
        Assert.True(float.IsFinite(damaged.Left));
        Assert.True(float.IsFinite(damaged.Right));
        Assert.Equal(1, mix.InvalidInputFrames);

        var recovered = mix.Step(0.2f, -0.4f, 0f, 0f);
        Assert.True(float.IsFinite(recovered.Left));
        Assert.True(float.IsFinite(recovered.Right));
        Assert.Equal(0.2f, recovered.Left, 4);
        Assert.Equal(-0.4f, recovered.Right, 4);
        mix.Reset();
        Assert.Equal(0, mix.InvalidInputFrames);
    }

    [Fact]
    public void LimiterDiagnosticsCountOnlyActualPeakReduction()
    {
        var mix = new AtmosphereMixEnvelope();
        mix.Step(0.1f, -0.1f, 0f, 0f);
        Assert.Equal(0, mix.LimitedFrames);
        for (var i = 0; i < 1000; i++)
            mix.Step(0.95f, 0.95f, 1f, 1f, 1f, 1f);
        Assert.True(mix.LimitedFrames > 0);
        mix.Reset();
        Assert.Equal(0, mix.LimitedFrames);
    }

    [Fact]
    public void GainRangeAndResetAreSafe()
    {
        var mix = new AtmosphereMixEnvelope();
        for (int i = 0; i < 3000; i++) mix.Step(0.3f, 0.3f, 0.1f, 0.1f, -2f, 5f);
        Assert.True(float.IsFinite(mix.CurrentGameGain));
        mix.Reset();
        Assert.Equal(1f, mix.CurrentGameGain);
        Assert.Equal(0f, mix.CurrentVoiceGain);
    }
}
