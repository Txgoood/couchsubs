// SPDX-License-Identifier: AGPL-3.0-only

namespace CouchSubs.Core;

public static class ScreenCaptionOwnership
{
    public static IReadOnlyList<ScreenTranslation.Item> PreferPositioned(IReadOnlyList<ScreenTranslation.Item> items,
        TextLine caption, string translation, out bool ownsCaption)
    {
        ownsCaption = false;
        if (caption.Box is not { } target) return items;
        var source = TextMatch.Normalize(caption.Text);
        var translated = TextMatch.Normalize(translation);
        List<ScreenTranslation.Item>? merged = null;
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var box = item.Box;
            var x = Math.Min(box.Right, target.Right) - Math.Max(box.Left, target.Left);
            var y = Math.Min(box.Bottom, target.Bottom) - Math.Max(box.Top, target.Top);
            if (x < (box.Right - box.Left) * .5 || y < (box.Bottom - box.Top) * .4) continue;
            var itemSource = TextMatch.Normalize(item.Source);
            var itemTranslation = TextMatch.Normalize(item.Translation);
            var sameSource = TextMatch.IsSimilar(itemSource, source);
            var fragment = itemSource.Length >= 5 && itemTranslation.Length >= 5 &&
                ContainsWords(source, itemSource) && ContainsWords(translated, itemTranslation);
            if (!sameSource && !fragment && !TextMatch.IsSimilar(itemTranslation, translated)) continue;
            merged ??= [.. items];
            if (!ownsCaption)
            {
                // Preserve the positioned layer's paint. Use the fuller caption
                // when the screen scan saw only the name inside the objective.
                if (!sameSource && fragment && itemTranslation.Length < translated.Length)
                    merged[i] = item with { Translation = translation, Box = box.Union(target) };
                ownsCaption = true;
            }
            else
            {
                merged.RemoveAt(i - (items.Count - merged.Count));
            }
        }
        return merged is not null ? merged : items;
    }

    private static bool ContainsWords(string full, string fragment) =>
        (" " + full + " ").Contains(" " + fragment + " ", StringComparison.Ordinal);
}
