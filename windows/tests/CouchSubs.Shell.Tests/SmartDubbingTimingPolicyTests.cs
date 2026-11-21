// SPDX-License-Identifier: AGPL-3.0-only
using CouchSubs.Core.Dubbing;
using CouchSubs.Shell;

namespace CouchSubs.Shell.Tests;

public sealed class SmartDubbingTimingPolicyTests
{
    [Fact]
    public void DuplicateOcrFrameIsIgnoredButNewDialogueIsKept()
    {
        var p = new SmartDubbingTimingPolicy();
        var first = new[] { new SpokenLine("Hello!", "Merhaba!", "Lucia") };
        Assert.True(p.Accept(first, 10000));
        Assert.False(p.Accept(first, 10400));
        Assert.Equal(1, p.SuppressedDuplicates);
        Assert.True(p.Accept([new SpokenLine("Run!", "Kaç!", "Lucia")], 10500));
        Assert.True(p.Accept(first, 12000));
    }

    [Fact]
    public void LateTtsIsSkippedRatherThanReadingOldSubtitle()
    {
        var p = new SmartDubbingTimingPolicy();
        Assert.True(p.ShouldPlay(10000, 12000));
        Assert.False(p.ShouldPlay(10000, 13600));
        Assert.Equal(1, p.DiscardedLateLines);
    }

    [Fact]
    public void ImpossibleTimingFailsClosedAndResetClearsDeduplication()
    {
        var p = new SmartDubbingTimingPolicy();
        Assert.False(p.ShouldPlay(-1, 30));
        Assert.False(p.ShouldPlay(101, 100));
        var line = new[] { new SpokenLine("Wait!", "Bekle!") };
        Assert.True(p.Accept(line, 1000));
        Assert.False(p.Accept(line, 1001));
        p.Reset();
        Assert.True(p.Accept(line, 1002));
        Assert.Equal(0, p.SuppressedDuplicates);
    }
}
