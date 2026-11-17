// SPDX-License-Identifier: AGPL-3.0-only
using CouchSubs.Core.Dubbing;
using CouchSubs.Shell;
using Xunit;

namespace CouchSubs.Shell.Tests;

public sealed class GameProsodyMeterTests
{
    [Fact]
    public void EmptyAndInvalidAudioDoNotBecomeStrongEnergy()
    {
        var meter = new GameProsodyMeter();
        meter.Push([], 2, 48000);
        meter.Push([100, 100], 0, 48000);
        Assert.False(meter.RecentStrongEnergy());
    }

    [Fact]
    public void SustainedHighEnergyIsDetectedAndResettable()
    {
        var meter = new GameProsodyMeter();
        var pcm = Enumerable.Repeat((short)12000, 1000).ToArray();
        for (var i = 0; i < 4; i++) meter.Push(pcm, 2, 48000);
        Assert.True(meter.RecentStrongEnergy());
        meter.Clear();
        Assert.False(meter.RecentStrongEnergy());
    }

    [Fact]
    public void SilenceDoesNotTriggerAcousticMode()
    {
        var meter = new GameProsodyMeter();
        for (var i = 0; i < 10; i++) meter.Push(new short[1000], 2, 48000);
        Assert.False(meter.RecentStrongEnergy());
        var engine = new EmotionContinuity();
        Assert.Null(engine.Resolve(new SpokenLine("We should leave.", "Gitmeliyiz."), DubbingEmotionMode.Acoustic));
    }

    [Fact]
    public void AcousticSettingSurvivesSerialization()
    {
        var settings = new DubbingSettings { EmotionMode = DubbingEmotionMode.Acoustic };
        var json = System.Text.Json.JsonSerializer.Serialize(settings);
        var loaded = System.Text.Json.JsonSerializer.Deserialize<DubbingSettings>(json);
        Assert.Equal(DubbingEmotionMode.Acoustic, loaded?.EmotionMode);
    }
}
