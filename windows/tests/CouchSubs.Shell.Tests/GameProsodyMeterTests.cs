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
    public void SnapshotNeverClaimsSpeechIdentityOrEmotion()
    {
        var meter = new GameProsodyMeter();
        var snap = meter.Snapshot();
        Assert.False(snap.Fresh);
        Assert.Equal(0, snap.Packets);
        Assert.Equal(0f, snap.Rms);
    }

    [Fact]
    public void RecentDynamicEnergyRejectsSteadySignalAndSilence()
    {
        var meter = new GameProsodyMeter();
        var steady = Enumerable.Repeat((short)12000, 1024).ToArray();
        for (int i = 0; i < 7; i++) meter.Push(steady, 1, 48000);
        Assert.False(meter.RecentDynamicEnergy());
        meter.Clear();
        for (int i = 0; i < 7; i++) meter.Push(new short[1024], 1, 48000);
        Assert.False(meter.RecentDynamicEnergy());
    }

    [Fact]
    public void MixedAlternatingAudioCanProduceDynamicsDescriptor()
    {
        var meter = new GameProsodyMeter();
        for (int packet = 0; packet < 10; packet++)
        {
            short amplitude = (short)(packet % 2 == 0 ? 18000 : 1800);
            short[] values = Enumerable.Range(0, 2048)
                .Select(i => (short)(i % 16 < 8 ? amplitude : -amplitude)).ToArray();
            meter.Push(values, 1, 48000);
        }
        var snapshot = meter.Snapshot();
        Assert.True(snapshot.Fresh);
        Assert.True(snapshot.EnergyVariation > 0);
        Assert.True(snapshot.ZeroCrossing > 0);
        Assert.True(snapshot.PeakRms > snapshot.Rms);
        meter.Clear();
        Assert.False(meter.Snapshot().Fresh);
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
