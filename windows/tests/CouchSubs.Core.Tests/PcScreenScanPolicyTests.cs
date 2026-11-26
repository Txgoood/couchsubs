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
        policy.Observe(Filled(20), 0);
        Assert.True(policy.ShouldScan(0, false));
        policy.MarkScan(0);
        policy.Observe(Filled(20), 500);
        Assert.False(policy.ShouldScan(500, false));
        Assert.True(policy.ShouldScan(6500, false));
        policy.RetryAfterCancellation();
        Assert.True(policy.ShouldScan(600, false));
    }

    [Fact]
    public void MotionThrottlesScreenOcrButSettledMenuScansImmediately()
    {
        var policy = new PcScreenScanPolicy();
        policy.Observe(Filled(20), 0);
        policy.MarkScan(0);
        policy.Observe(Filled(80), 500);
        Assert.True(policy.ShouldScan(500, false));
        policy.MarkScan(500);
        policy.Observe(Filled(150), 1500);
        Assert.True(policy.Moving);
        Assert.Equal(160, policy.FullCaptureInterval);
        Assert.False(policy.ShouldScan(1500, true));
        Assert.True(policy.ShouldScan(3100, false));
        policy.MarkScan(3100);
        policy.Observe(Filled(150), 3600);
        Assert.False(policy.Moving);
        Assert.Equal(500, policy.FullCaptureInterval);
        Assert.True(policy.ShouldScan(3600, false));
    }

    [Fact]
    public void SmallMenuChangeOrKnownRegionTriggersRead()
    {
        var policy = new PcScreenScanPolicy();
        policy.Observe(Filled(20), 0);
        policy.MarkScan(0);
        var changed = Filled(20);
        for (var y = 0; y < 12; y++)
            Array.Fill(changed.Pixels, (byte)210, y * changed.Stride, 40 * 4);
        policy.Observe(changed, 500);
        Assert.True(policy.ShouldScan(500, false));
        policy.MarkScan(500);
        Assert.True(policy.ShouldScan(600, true));
        policy.Reset();
        policy.Observe(changed, 700);
        Assert.True(policy.ShouldScan(700, false));
    }

    [Fact]
    public void SharpTransitionFollowedByStableMenuRunsOneFastScan()
    {
        var policy = new PcScreenScanPolicy();
        policy.Observe(Filled(20), 0);
        policy.MarkScan(0);
        policy.Observe(Filled(80), 500);
        policy.MarkScan(500);
        var menu = Filled(220);
        Assert.False(policy.Observe(menu, 1000));
        Assert.Equal(160, policy.FullCaptureInterval);
        Assert.False(policy.ShouldScan(1000, false));

        Assert.True(policy.Observe(menu, 1160));
        Assert.True(policy.PriorityScanPending);
        Assert.True(policy.ShouldScan(1160, false));
        policy.MarkScan(1160);
        Assert.Equal(500, policy.FullCaptureInterval);
        Assert.False(policy.PriorityScanPending);
        policy.Observe(menu, 1660);
        Assert.False(policy.ShouldScan(1660, false));
    }

    [Fact]
    public void SustainedLargeMotionDoesNotScanSoonerOrProbeMoreThanOncePerCooldown()
    {
        var policy = new PcScreenScanPolicy();
        var dark = Filled(20);
        var light = Filled(210);
        var captures = 0;
        var probes = 0;
        var scans = new List<long>();
        long lastProbe = -6500;
        for (long now = 0; now < 60_000; now += policy.FullCaptureInterval)
        {
            Assert.False(policy.Observe(captures++ % 2 == 0 ? dark : light, now));
            if (policy.ShouldScan(now, false))
            {
                scans.Add(now);
                policy.MarkScan(now);
            }
            if (policy.FullCaptureInterval != 160) continue;
            Assert.True(now - lastProbe >= 6500);
            lastProbe = now;
            probes++;
        }

        Assert.Equal([0L, 500L], scans.Take(2));
        Assert.All(scans.Skip(2).Zip(scans.Skip(1)), pair => Assert.True(pair.First - pair.Second >= 2600));
        Assert.InRange(probes, 1, 10);
        Assert.InRange(captures, 60, 72);
    }
}
