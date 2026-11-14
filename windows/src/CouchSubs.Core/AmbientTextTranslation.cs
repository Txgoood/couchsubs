// SPDX-License-Identifier: AGPL-3.0-only

namespace CouchSubs.Core;

/// <summary>
/// Konuşma altyazılarından bağımsız, ekrandaki statik menü/nesne yazıları.
/// Bu sınıf seslendirme kuyruğuna asla dokunmaz. Konuşma devam ederken de çalışabilir.
/// </summary>
public static class AmbientTextTranslation
{
    public const int MaxGroupsPerScan = 12;

    /// <summary>OCR kutusunun altyazı bölgesiyle anlamlı şekilde çakışması: çift çeviriyi engeller.</summary>
    public static bool IsDialogueRegion(TextBox box, CropRegion subtitleArea)
    {
        var left = Math.Max(box.Left, subtitleArea.Left);
        var top = Math.Max(box.Top, subtitleArea.Top);
        var right = Math.Min(box.Right, subtitleArea.Right);
        var bottom = Math.Min(box.Bottom, subtitleArea.Bottom);
        if (right <= left || bottom <= top) return false;
        var overlap = (right - left) * (bottom - top);
        var area = Math.Max(0.000001, (box.Right - box.Left) * (box.Bottom - box.Top));
        return overlap / area > 0.55;
    }

    public static IReadOnlyList<ScreenTranslation.Group> MenuGroups(Reading reading, CropRegion subtitleArea) =>
        ScreenTranslation.Groups(reading)
            .Where(group => !IsDialogueRegion(group.Box, subtitleArea))
            .Take(MaxGroupsPerScan)
            .ToArray();

    /// <summary>
    /// Tam ekran OCR yalnız periyodik çağrılır. Çeviri her yazı grubu için ayrı yapılır,
    /// altyazı OCR bölgesi filtrelenir. İsim/sayı/değişmeyen yazılar gösterilmez.
    /// </summary>
    public static async Task<IReadOnlyList<ScreenTranslation.Item>> TranslateAsync(
        Frame frame, ITextReader reader, ITextTranslator translator, CropRegion subtitleArea,
        CancellationToken cancellationToken = default)
    {
        var reading = await Task.Run(
            () => reader.ReadLines(frame.Height < 900 ? frame.Scaled(2) : frame, false),
            cancellationToken).ConfigureAwait(false);
        var items = new List<ScreenTranslation.Item>();
        foreach (var group in MenuGroups(reading, subtitleArea))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = new List<string>();
            var changed = false;
            foreach (var paragraph in group.Paragraphs)
            {
                var translated = (await translator.TranslateAsync(paragraph, cancellationToken)
                    .ConfigureAwait(false)).Trim();
                if (translated.Length == 0) translated = paragraph;
                if (!TextMatch.IsSimilar(TextMatch.Normalize(paragraph), TextMatch.Normalize(translated), 0.90))
                    changed = true;
                target.Add(translated);
            }
            if (changed)
                items.Add(new ScreenTranslation.Item(
                    string.Join('\n', group.Paragraphs), string.Join('\n', target),
                    group.Box, group.LineHeight));
        }
        return items;
    }

    public static string SnapshotKey(IReadOnlyList<ScreenTranslation.Item> items) =>
        string.Join("|", items.Select(item =>
            $"{item.Box.Left:F2},{item.Box.Top:F2}:{item.Source}"));
}
