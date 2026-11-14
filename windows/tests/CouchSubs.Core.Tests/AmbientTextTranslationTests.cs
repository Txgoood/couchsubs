// SPDX-License-Identifier: AGPL-3.0-only

using CouchSubs.Core;

namespace CouchSubs.Core.Tests;

public class AmbientTextTranslationTests
{
    [Fact]
    public void DialogueAreaIsExcludedButInventoryIsKept()
    {
        var reading = new Reading([
            new TextLine("Inventory", new TextBox(0.10, 0.08, 0.34, 0.14)),
            new TextLine("Refrigerator", new TextBox(0.10, 0.25, 0.40, 0.31)),
            new TextLine("We have to leave immediately.", new TextBox(0.28, 0.84, 0.72, 0.90)),
        ]);
        var groups = AmbientTextTranslation.MenuGroups(reading, new CropRegion());
        Assert.Equal(2, groups.Count);
        Assert.DoesNotContain(groups, x => x.Paragraphs.Any(p => p.Contains("leave")));
    }

    [Fact]
    public async Task MenuTranslationsNeverNeedASpeechPipeline()
    {
        var reading = new Reading([
            new TextLine("Inventory", new TextBox(0.08, 0.08, 0.26, 0.15)),
            new TextLine("Open refrigerator", new TextBox(0.20, 0.37, 0.54, 0.42)),
            new TextLine("I'm talking now!", new TextBox(0.25, 0.85, 0.75, 0.92)),
        ]);
        var items = await AmbientTextTranslation.TranslateAsync(Frame.Blank(64, 64),
            new LinesReader(reading), new FakeTranslator(), new CropRegion());
        Assert.Equal(2, items.Count);
        Assert.Contains(items, x => x.Translation.Contains("TR: Inventory"));
        Assert.DoesNotContain(items, x => x.Source.Contains("talking"));
    }

    [Fact]
    public void DialogueOverlapThresholdDoesNotExcludeUnrelatedHud()
    {
        var subtitles = new CropRegion();
        Assert.True(AmbientTextTranslation.IsDialogueRegion(
            new TextBox(0.20, 0.80, 0.80, 0.88), subtitles));
        Assert.False(AmbientTextTranslation.IsDialogueRegion(
            new TextBox(0.02, 0.85, 0.12, 0.91), subtitles));
    }

    [Fact]
    public void GroupCountIsBoundedEvenForLargeMap()
    {
        var reading = new Reading(Enumerable.Range(0, 40)
            .Select(i => new TextLine($"Location {i}", new TextBox(0.02, i * 0.01, 0.18, i * 0.01 + 0.007))));
        Assert.InRange(AmbientTextTranslation.MenuGroups(reading, new CropRegion()).Count, 1, 12);
    }
}
