// SPDX-License-Identifier: AGPL-3.0-only
using CouchSubs.Core;

namespace CouchSubs.Core.Tests;

public sealed class ScreenCaptionOwnershipTests
{
    [Fact]
    public void PositionedObjectiveKeepsItsPaintAndReceivesTheFullTranslation()
    {
        var box = new TextBox(.36, .88, .64, .92);
        var fragment = new ScreenTranslation.Item("Andrew", "Andrew’a", new TextBox(.50, .88, .64, .92), .04)
        { Paint = new ScreenTranslation.OverlayPaint("#725A43", "#8E6D51", "#FFFFFF", true) };
        var duplicate = new ScreenTranslation.Item("Return to Andrew", "Andrew’a geri dön", box, .04);
        var result = ScreenCaptionOwnership.PreferPositioned([fragment, duplicate],
            new TextLine("Return to Andrew", box), "Andrew’a geri dön", out var owns);

        Assert.True(owns);
        var shown = Assert.Single(result);
        Assert.Equal(fragment.Paint, shown.Paint);
        Assert.Equal("Andrew’a geri dön", shown.Translation);
        Assert.Equal(box, shown.Box);
    }

    [Fact]
    public void SameWordsInAnotherLocationOrUnrelatedNearbyTextDoNotHideCaption()
    {
        var box = new TextBox(.36, .88, .64, .92);
        var items = new[] {
            new ScreenTranslation.Item("Return to Andrew", "Andrew’a geri dön", new TextBox(.1, .2, .4, .24), .04),
            new ScreenTranslation.Item("Open map", "Haritayı aç", box, .04),
        };
        Assert.Same(items, ScreenCaptionOwnership.PreferPositioned(items,
            new TextLine("Return to Andrew", box), "Andrew’a geri dön", out var owns));
        Assert.False(owns);
    }
}
