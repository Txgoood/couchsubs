// SPDX-License-Identifier: AGPL-3.0-only
using CouchSubs.Core.Dubbing;
using Xunit;

namespace CouchSubs.Core.Tests;

public sealed class Ps5DubbingTogglePolicyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HeadsetSessionAlwaysCapturesAudioEvenBeforeTurkishTtsIsEnabled(bool mixerActive)
    {
        Assert.True(Ps5DubbingTogglePolicy.RequiresAudioCapture(true, mixerActive, false, false));
    }

    [Fact]
    public void OtherOutputsStillUseAudioOnlyWhenRequired()
    {
        Assert.False(Ps5DubbingTogglePolicy.RequiresAudioCapture(false, false, false, false));
        Assert.True(Ps5DubbingTogglePolicy.RequiresAudioCapture(false, true, false, false));
        Assert.True(Ps5DubbingTogglePolicy.RequiresAudioCapture(false, false, true, false));
        Assert.True(Ps5DubbingTogglePolicy.RequiresAudioCapture(false, false, false, true));
    }
}
