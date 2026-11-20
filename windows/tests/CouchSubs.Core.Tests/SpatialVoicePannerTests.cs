// SPDX-License-Identifier: AGPL-3.0-only
using CouchSubs.Core.Dubbing;
using Xunit;

namespace CouchSubs.Core.Tests;

public sealed class SpatialVoicePannerTests
{
    [Theory]
    [InlineData(float.NaN, 1f)]
    [InlineData(0.8f, float.NaN)]
    [InlineData(0.9f, 0.7f)]
    [InlineData(2f, 1f)]
    [InlineData(-2f, 1f)]
    public void UncertainOrInvalidPositionRemainsCentered(float pan, float confidence)
    {
        var gains = SpatialVoicePanner.Gains(pan, confidence);
        Assert.Equal(1f, gains.Left);
        Assert.Equal(1f, gains.Right);
    }

    [Fact]
    public void RightHandSpeakerReducesLeftTurkishChannelOnly()
    {
        var gains = SpatialVoicePanner.Gains(0.8f, 0.96f);
        Assert.InRange(gains.Left, 0.67f, 0.68f);
        Assert.Equal(1f, gains.Right);
    }

    [Fact]
    public void LeftHandSpeakerReducesRightTurkishChannelOnly()
    {
        var gains = SpatialVoicePanner.Gains(-0.8f, 0.96f);
        Assert.Equal(1f, gains.Left);
        Assert.InRange(gains.Right, 0.67f, 0.68f);
    }

    [Fact]
    public void ValidCenterLocationDoesNotRebalanceStereo()
    {
        var gains = SpatialVoicePanner.Gains(0f, 1f);
        Assert.Equal(1f, gains.Left);
        Assert.Equal(1f, gains.Right);
    }
}
