// SPDX-License-Identifier: AGPL-3.0-only
using CouchSubs.Core.Dubbing;
using CouchSubs.Shell;

namespace CouchSubs.Shell.Tests;

public sealed class LongSessionAudioMonitorTests
{
    [Fact]
    public void FirstReadingAndFastRefreshAreNotDeclaredHealthyOrFaulty()
    {
        var m = new LongSessionAudioMonitor();
        Assert.False(m.Observe(new GameAudioBufferStats(0, 0, 0, 0), 1000).ValidWindow);
        Assert.False(m.Observe(new GameAudioBufferStats(0, 0, 5, 100), 1500).ValidWindow);
        var result = m.Observe(new GameAudioBufferStats(0, 0, 10, 200), 6200);
        Assert.True(result.ValidWindow);
        Assert.False(result.BacklogWarning);
        Assert.Equal(10, result.DroppedSamples);
    }

    [Fact]
    public void BacklogWarningDoesNotDependOnExpectedSilenceUnderruns()
    {
        var m = new LongSessionAudioMonitor();
        m.Observe(new GameAudioBufferStats(0, 0, 0, 0), 0);
        var silence = m.Observe(new GameAudioBufferStats(0, 0, 0, 80000), 5000);
        Assert.True(silence.ValidWindow);
        Assert.False(silence.BacklogWarning);
        var overflow = m.Observe(new GameAudioBufferStats(0, 0, 12000, 80000), 10000);
        Assert.True(overflow.BacklogWarning);
        Assert.Equal(1, overflow.WarningWindows);
    }

    [Fact]
    public void SleepResumeAndNewSessionNeverCreateSpuriousWarnings()
    {
        var m = new LongSessionAudioMonitor();
        m.Observe(new GameAudioBufferStats(0, 0, 500, 100), 1000);
        Assert.False(m.Observe(new GameAudioBufferStats(0, 0, 30000, 10000), 90000).ValidWindow);
        m.Reset();
        Assert.False(m.Observe(new GameAudioBufferStats(0, 0, 0, 0), 0).ValidWindow);
        Assert.Equal(0, m.Observe(new GameAudioBufferStats(0, 0, 0, 0), 5000).WarningWindows);
    }

    [Fact]
    public void BufferIsBoundedDuringManyFramesOfSyntheticAudio()
    {
        var ring = new GameAudioBuffer(9600);
        var frame = Enumerable.Repeat(0.1f, 960).ToArray();
        for (var i = 0; i < 10000; i++)
        {
            ring.Write(frame);
            if (i % 3 == 0) ring.Read(new float[960]);
        }
        Assert.InRange(ring.BufferedSamples, 0, ring.CapacitySamples);
        Assert.True(ring.Statistics.DroppedSamples > 0);
    }
}
