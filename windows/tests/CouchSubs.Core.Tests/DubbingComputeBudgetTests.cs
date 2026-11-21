// SPDX-License-Identifier: AGPL-3.0-only
using CouchSubs.Core.Dubbing;

namespace CouchSubs.Core.Tests;

public sealed class DubbingComputeBudgetTests
{
    [Theory]
    [InlineData(32, null, 4)]
    [InlineData(4, null, 1)]
    [InlineData(2, "5", 2)]
    [InlineData(32, "6", 6)]
    [InlineData(32, "0", 4)]
    [InlineData(32, "NaN", 4)]
    public void BudgetPreservesCpuHeadroomAndValidatesOverrides(int processors,
        string? overrideValue, int expected) =>
        Assert.Equal(expected, DubbingComputeBudget.CpuThreads(processors, overrideValue));

    [Fact]
    public void OnnxUsesExistingDefaultUntilExplicitlyTuned()
    {
        Assert.Equal(0, DubbingComputeBudget.OnnxThreads(32, null));
        Assert.Equal(2, DubbingComputeBudget.OnnxThreads(32, "2"));
        Assert.Equal(0, DubbingComputeBudget.OnnxThreads(32, "garbage"));
    }

    [Fact]
    public void GpuMustNeverBeClaimedWithoutWorkingPackagedProvider()
    {
        Assert.False(DubbingComputeBudget.UseGpu(false, true, true, true));
        Assert.False(DubbingComputeBudget.UseGpu(true, false, true, true));
        Assert.False(DubbingComputeBudget.UseGpu(true, true, false, true));
        Assert.False(DubbingComputeBudget.UseGpu(true, true, true, false));
        Assert.True(DubbingComputeBudget.UseGpu(true, true, true, true));
    }

    [Fact]
    public void LatencyTrackerCountsWithoutRecordingTextOrPcm()
    {
        var tracker = new DubbingLatencyTracker();
        tracker.Record(100);
        tracker.Record(1700);
        tracker.Record(-1);
        var stats = tracker.Snapshot();
        Assert.Equal(2, stats.Calls);
        Assert.Equal(900d, stats.AverageMs);
        Assert.Equal(1700, stats.PeakMs);
        Assert.Equal(1, stats.SlowCalls);
        tracker.Reset();
        Assert.Equal(0, tracker.Snapshot().Calls);
    }
}
