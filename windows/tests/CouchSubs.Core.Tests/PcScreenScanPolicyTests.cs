// SPDX-License-Identifier: AGPL-3.0-only
using CouchSubs.Core;

namespace CouchSubs.Core.Tests;

public sealed class PcScreenScanPolicyTests
{
    private static Frame Filled(byte value)
    {
        var frame = Frame.Blank(192, 108);
        Array.Fill(frame.Pixels, value);
        return frame;
    }

    [Fact]
    public void StaticScreenIsReusedWithPeriodicDiscovery()
    {
        var policy = new PcScreenScanPolicy();
        policy.Observe(Filled(20));
        Assert.True(policy.ShouldScan(0, false));
        policy.MarkScan(0);
        policy.Observe(Filled(20));
        Assert.False(policy.ShouldScan(500, false));
        Assert.True(policy.ShouldScan(6500, false));
        policy.RetryAfterCancellation();
        Assert.True(policy.ShouldScan(600, false));
    }

    [Fact]
    public void MotionThrottlesScreenOcrButSettledMenuScansImmediately()
    {
        var policy = new PcScreenScanPolicy();
        policy.Observe(Filled(20));
        policy.MarkScan(0);
        policy.Observe(Filled(80));
        Assert.True(policy.ShouldScan(500, false));
        policy.MarkScan(500);
        policy.Observe(Filled(150));
        Assert.True(policy.Moving);
        Assert.Equal(1000, policy.FullCaptureInterval);
        Assert.False(policy.ShouldScan(1500, true));
        Assert.True(policy.ShouldScan(3100, false));
        policy.MarkScan(3100);
        policy.Observe(Filled(150));
        Assert.False(policy.Moving);
        Assert.Equal(500, policy.FullCaptureInterval);
        Assert.True(policy.ShouldScan(3600, false));
    }

    [Fact]
    public void SmallMenuChangeOrKnownRegionTriggersRead()
    {
        var policy = new PcScreenScanPolicy();
        policy.Observe(Filled(20));
        policy.MarkScan(0);
        var changed = Filled(20);
        for (var y = 0; y < 12; y++)
            Array.Fill(changed.Pixels, (byte)210, y * changed.Stride, 40 * 4);
        policy.Observe(changed);
        Assert.True(policy.ShouldScan(500, false));
        policy.MarkScan(500);
        Assert.True(policy.ShouldScan(600, true));
        policy.Reset();
        policy.Observe(changed);
        Assert.True(policy.ShouldScan(700, false));
    }
}
