// SPDX-License-Identifier: AGPL-3.0-only

namespace CouchSubs.Core;

/// <summary>
/// "Ekranı çevir": karenin tamamı bir kez okunur, yazılar yerlerine göre gruplara ayrılır (menü, görev paneli, bilgi
/// kutusu, altyazı...) ve her grup ayrı çevrilir. TV her çeviriyi kendi İngilizce yazısının üstüne, o yazının
/// boyutunda koyar. Sürekli çalışmaz; kullanıcı tuşa basınca bir kez yapılır (Mac: CouchSubsCore/ScreenTranslation.swift).
/// </summary>
public static class ScreenTranslation
{
    /// <summary>Ekrandaki bir yazı grubu.</summary>
    /// <param name="Paragraphs">Grubun ayrı ayrı çevrilecek parçaları (başlık, cümle, menü satırı...).</param>
    /// <param name="Box">Grubun karedeki yeri, kenarlara oranla.</param>
    /// <param name="LineHeight">Satırlarının tipik yüksekliği, karenin yüksekliğine oranla; TV yazı boyunu buna göre seçer.</param>
    public sealed record Group(IReadOnlyList<string> Paragraphs, TextBox Box, double LineHeight);

    /// <param name="Translation">Parçaların çevirisi, her parça ayrı satırda.</param>
    public sealed record Item(string Source, string Translation, TextBox Box, double LineHeight)
    {
        public OverlayPaint? Paint { get; init; }
        /// <summary>Birden çok parça var: TV satır satır, sola yaslı gösterir.</summary>
        public bool Paragraphs => Translation.Contains('\n');
    }

    public sealed record OverlayPaint(string Left, string Right, string Foreground, bool Bold);

    private static readonly IReadOnlyDictionary<string, string> TurkishMenuLabels =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["select"] = "Seç",
            ["back"] = "Geri",
            ["confirm"] = "Onayla",
            ["cancel"] = "İptal",
            ["exit"] = "Çık",
            ["scroll"] = "Kaydır",
            ["restore all"] = "Tümünü sıfırla",
            ["restore default"] = "Varsayılana dön",
            ["photo studio"] = "Fotoğraf stüdyosu",
            ["display"] = "Görüntü",
            ["ui"] = "Arayüz",
            ["camera"] = "Kamera",
            ["on"] = "Açık",
            ["off"] = "Kapalı",
        };

    public static bool SameScreen(Frame first, Frame second)
    {
        if (ReferenceEquals(first, second)) return true;
        if (first.Width != second.Width || first.Height != second.Height) return false;
        var before = RegionCropper.Signature(first);
        var after = RegionCropper.Signature(second);
        var changed = 0;
        for (var index = 0; index < before.Length; index++)
            if (Math.Abs(before[index] - after[index]) >= 30 && ++changed >= before.Length / 7)
                return false;
        return true;
    }

    public static async Task<IReadOnlyList<Item>> TranslateOverlayAsync(
        Frame frame, ITextReader reader, ITextTranslator translator,
        CropRegion? exclude = null, Action<IReadOnlyList<Item>>? progress = null,
        CancellationToken cancellationToken = default, string? subtitle = null, Func<bool>? isCurrent = null,
        bool englishToTurkish = false)
    {
        var reading = await Task.Run(() => reader.ReadScreenLines(frame), cancellationToken).ConfigureAwait(false);
        var lines = OverlayLines(reading).Where(line => exclude is null || line.IsControllerHint ||
            !AmbientTextTranslation.IsDialogueRegion(line.Box!.Value, exclude) ||
            subtitle is not null && !IsSubtitleLine(line.Text, subtitle));
        var items = new List<Item>();
        foreach (var group in OverlayParagraphs(lines))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (isCurrent?.Invoke() == false) break;
            string target;
            if (englishToTurkish && TurkishMenuLabels.TryGetValue(group.Text, out var label))
                target = IsUppercase(group.Text) ? label.ToUpper(System.Globalization.CultureInfo.GetCultureInfo("tr-TR")) : label;
            else
                target = (await translator.TranslateAsync(group.Text, cancellationToken).ConfigureAwait(false)).Trim();
            if (isCurrent?.Invoke() == false) break;
            if (target.Length == 0 || TextMatch.Normalize(group.Text) == TextMatch.Normalize(target))
                continue;
            items.Add(new Item(group.Text, target, group.Box, group.Height)
                { Paint = PaintFor(frame, new TextLine(group.Text, group.Box)) });
            progress?.Invoke([.. items]);
        }
        return items;
    }

    private static bool IsSubtitleLine(string text, string subtitle)
    {
        var line = TextMatch.Normalize(text);
        var source = TextMatch.Normalize(subtitle);
        return line.Length > 0 && (line == source ||
            line.Length >= 8 && (" " + source + " ").Contains(" " + line + " ", StringComparison.Ordinal));
    }

    private static IEnumerable<(string Text, TextBox Box, double Height)> OverlayParagraphs(IEnumerable<TextLine> lines)
    {
        var groups = new List<List<TextLine>>();
        foreach (var line in lines)
        {
            var box = line.Box!.Value;
            var index = groups.FindLastIndex(group =>
            {
                var previous = group[^1];
                var upper = previous.Box!.Value;
                return !line.IsControllerHint && !previous.IsControllerHint &&
                    box.Top > upper.Top + (upper.Bottom - upper.Top) * .65 &&
                    Math.Abs(box.Left - upper.Left) < (upper.Bottom - upper.Top) * .9 &&
                    Belongs(box, upper) && !SubtitleStabilizer.EndsSentence(previous.Text) &&
                    WordCount(previous.Text) >= 4 && char.IsLower(line.Text[0]);
            });
            if (index < 0) groups.Add([line]);
            else groups[index].Add(line);
        }

        return groups.Select(group =>
        {
            var first = group[0].Box!.Value;
            return (string.Join(' ', group.Select(line => line.Text)),
                group.Select(line => line.Box!.Value).Aggregate((a, b) => a.Union(b)), first.Bottom - first.Top);
        });
    }

    internal static IEnumerable<TextLine> OverlayLines(Reading reading)
    {
        foreach (var line in reading.Lines.SelectMany(SplitColumns).OrderBy(line => line.Box?.Top))
        {
            if (line.Box is not { } box || box.Right <= box.Left || box.Bottom <= box.Top) continue;
            var prompt = ButtonPrompts.IsPrompt(line.Text) || IsShortAction(line.Text) ||
                line.Words.Any(w => IsShortAction(w.Text));
            var words = line.Words;
            if (!words.Any(word => ButtonPrompts.IsMenuButton(word.Text, prompt)) &&
                words.Count(word => IsShortAction(word.Text)) < 2)
            {
                var cleaned = OcrNoise.Clean(line.Text);
                if (IsWords(cleaned)) yield return line with { Text = cleaned, IsControllerHint = prompt };
                continue;
            }
            var part = new List<TextLine>();
            for (var i = 0; i <= words.Count; i++)
            {
                var button = i < words.Count && ButtonPrompts.IsMenuButton(words[i].Text, prompt);
                var startsAction = i < words.Count && prompt && IsShortAction(words[i].Text) &&
                    part.Count == 1 && IsShortAction(part[0].Text);
                if (i < words.Count && !button && !startsAction)
                {
                    part.Add(words[i]);
                    continue;
                }
                var cleaned = OcrNoise.Clean(string.Join(' ', part.Select(word => word.Text)));
                if (IsWords(cleaned) && part.All(word => word.Box is not null))
                    yield return new TextLine(cleaned, part.Select(word => word.Box!.Value).Aggregate((a, b) => a.Union(b)))
                        { IsControllerHint = true };
                part.Clear();
                if (startsAction) part.Add(words[i]);
            }
        }
    }

    private static IEnumerable<TextLine> SplitColumns(TextLine line)
    {
        if (line.Box is not { } box || line.Words.Count < 2 || line.Words.Any(word => word.Box is null))
        {
            yield return line;
            yield break;
        }

        var words = line.Words;
        var height = box.Bottom - box.Top;
        var start = 0;
        for (var index = 1; index <= words.Count; index++)
        {
            if (index < words.Count)
            {
                var previous = words[index - 1];
                var current = words[index];
                var gap = current.Box!.Value.Left - previous.Box!.Value.Right;
                var threshold = IsUppercase(previous.Text) && IsUppercase(current.Text) ? .8 : 1.35;
                if (gap <= height * threshold) continue;
            }
            if (start == 0 && index == words.Count)
            {
                yield return line;
                yield break;
            }
            var part = words.Skip(start).Take(index - start).ToArray();
            yield return new TextLine(string.Join(' ', part.Select(word => word.Text)),
                part.Select(word => word.Box!.Value).Aggregate((a, b) => a.Union(b))) { Words = part };
            start = index;
        }
    }

    private static bool IsUppercase(string text) => text.Any(char.IsLetter) && text.Where(char.IsLetter).All(char.IsUpper);

    private static bool IsShortAction(string value) =>
        value.Equals("select", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("back", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("cancel", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("confirm", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("exit", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("scroll", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("restore all", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("restore default", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("photo studio", StringComparison.OrdinalIgnoreCase);

    private static OverlayPaint PaintFor(Frame frame, TextLine line)
    {
        var box = line.Box!.Value;
        var rowHeight = box.Bottom - box.Top;
        var left = BackgroundAt(frame, box.Left, box.Top, box.Bottom, rowHeight);
        var right = BackgroundAt(frame, box.Right, box.Top, box.Bottom, rowHeight);
        bool light = ((left.R + right.R) * 3 + (left.G + right.G) * 6 + left.B + right.B) / 20 > 150;
        return new OverlayPaint(Hex(left), Hex(right), light ? "#181820" : "#FFFFFF", IsUppercase(line.Text));
    }

    private static (int R, int G, int B) BackgroundAt(Frame frame, double x, double top, double bottom, double height)
    {
        var values = new List<(int R, int G, int B)>(18);
        foreach (var y in new[] { top - height * .25, bottom + height * .25 })
        for (var i = -4; i <= 4; i++)
        {
            var px = (int)(Math.Clamp(x + i * height * .15, 0, 1) * (frame.Width - 1));
            var py = (int)(Math.Clamp(y, 0, 1) * (frame.Height - 1));
            var index = py * frame.Stride + px * 4;
            values.Add((frame.Pixels[index + 2], frame.Pixels[index + 1], frame.Pixels[index]));
        }
        return values.OrderBy(color => color.R * 3 + color.G * 6 + color.B).ElementAt(values.Count / 2);
    }

    private static string Hex((int R, int G, int B) c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    /// <summary>Kalabalık ekranda TV'yi kutuyla doldurmamak ve çeviriyi uzatmamak için en çok bu kadar grup çevrilir.</summary>
    public const int MaxGroups = 30;

    /// <summary>
    /// Kareyi okuyup gruplarını sırayla çevirir. Her grup çevrilince o ana kadarki bütün sonuçlarla
    /// <paramref name="progress"/> çağrılır; böylece TV çeviriler hazırlandıkça dolar. Çevirisi aslıyla aynı olan
    /// (ad, sayı, zaten Türkçe yazı) grup gösterilmez. Çevirmen kullanılamıyorsa <see cref="TranslatorException"/> fırlatır.
    /// </summary>
    public static async Task<IReadOnlyList<Item>> TranslateAsync(
        Frame frame, ITextReader reader, ITextTranslator translator,
        Action<IReadOnlyList<Item>>? progress = null, CancellationToken cancellationToken = default)
    {
        // Küçük karede (720p) köşe yazıları birkaç piksel boyundadır; büyütmek yazı tanımanın onları da bulmasını sağlar.
        var reading = await Task.Run(() => reader.ReadLines(frame.Height < 900 ? frame.Scaled(2) : frame, false), cancellationToken)
            .ConfigureAwait(false);
        var items = new List<Item>();
        foreach (var group in Groups(reading))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var translated = new List<string>(group.Paragraphs.Count);
            var changed = false;
            foreach (var paragraph in group.Paragraphs)
            {
                var text = (await translator.TranslateAsync(paragraph, cancellationToken).ConfigureAwait(false)).Trim();
                if (text.Length == 0) text = paragraph;
                changed |= !TextMatch.IsSimilar(TextMatch.Normalize(paragraph), TextMatch.Normalize(text), 0.90);
                translated.Add(text);
            }
            if (!changed) continue;
            items.Add(new Item(string.Join('\n', group.Paragraphs), string.Join('\n', translated), group.Box, group.LineHeight));
            progress?.Invoke([.. items]);
        }
        return items;
    }

    /// <summary>
    /// Satırları gruplara toplar. Kumanda ipuçları ve yazı tanıma çöpleri atılır; harfi olmayan ya da tek harfli
    /// satırlar ("23m", "9", "+") göstergedir, çevrilmez. Bir satır, üstündeki satırın hemen altındaysa, yatayda onunla
    /// üst üste biniyorsa ve yazı boyu benziyorsa o gruba katılır. Gruplar yukarıdan aşağı, en çok <see cref="MaxGroups"/>.
    /// </summary>
    public static IReadOnlyList<Group> Groups(Reading reading)
    {
        var lines = reading.WithoutButtonPrompts().WithoutOcrNoise().Lines
            .Where(line => line.Box is not null && IsWords(line.Text))
            .OrderBy(line => line.Box!.Value.Top)
            .ToList();
        var groups = new List<List<TextLine>>();
        foreach (var line in lines)
        {
            var index = groups.FindLastIndex(group => Belongs(line.Box!.Value, group[^1].Box!.Value));
            if (index >= 0) groups[index].Add(line);
            else groups.Add([line]);
        }
        return [.. groups.Take(MaxGroups).Select(group =>
        {
            var heights = group.Select(line => line.Box!.Value.Bottom - line.Box!.Value.Top).OrderBy(value => value).ToList();
            var box = group.Select(line => line.Box!.Value).Aggregate((a, b) => a.Union(b));
            return new Group(Paragraphs(group), box, heights[heights.Count / 2]);
        })];
    }

    /// <summary>Alt satır üsttekinin hemen altında (arada satır yüksekliğinin %80'inden az), yatayda onunla üst üste ve yazı boyu benzer.</summary>
    private static bool Belongs(TextBox lower, TextBox upper)
    {
        double a = upper.Bottom - upper.Top, b = lower.Bottom - lower.Top;
        if (Math.Max(a, b) > Math.Min(a, b) * 1.6) return false;
        if (lower.Top - upper.Bottom >= Math.Max(a, b) * 0.8 || lower.Top < upper.Top) return false;
        var overlap = Math.Min(lower.Right, upper.Right) - Math.Max(lower.Left, upper.Left);
        var narrower = Math.Min(lower.Right - lower.Left, upper.Right - upper.Left);
        return overlap > 0 && overlap >= narrower * 0.5;
    }

    /// <summary>
    /// Grubun satırlarını ayrı çevrilecek parçalara böler. Önceki satır cümleyi bitirdiyse ya da kısa bir başlık/menü
    /// satırıysa (en çok üç sözcük, noktalamasız) ve yeni satır büyük harfle başlıyorsa yeni parça başlar; yoksa satır
    /// öncekinin devamıdır (cümle iki satıra sarılmış).
    /// </summary>
    internal static IReadOnlyList<string> Paragraphs(IReadOnlyList<TextLine> lines)
    {
        var result = new List<string>();
        foreach (var line in lines.Select(line => line.Text.Trim()))
        {
            if (result.Count > 0 && !StartsNew(result[^1], line)) result[^1] += " " + line;
            else result.Add(line);
        }
        return result;
    }

    private static bool StartsNew(string previous, string line) =>
        SubtitleStabilizer.EndsSentence(previous) ||
        (char.IsUpper(line[0]) && WordCount(previous) <= 3 && !previous.EndsWith(','));

    /// <summary>En az iki harfi ve iki harfli bir sözcüğü olan satır yazıdır; geri kalanı sayı ve simgedir.</summary>
    internal static bool IsWords(string text) =>
        text.Count(char.IsLetter) >= 2 &&
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Any(word => word.Count(char.IsLetter) >= 2);

    private static int WordCount(string text) => text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    /// <summary>Çevirilerin TV'de kalma süresi (ms): yazı çoksa daha uzun; tuşa yeniden basınca hemen kalkar.</summary>
    public static int DisplayMilliseconds(IReadOnlyList<Item> items) =>
        Math.Clamp(8000 + (items.Sum(item => item.Translation.Length) * 50), 12000, 60000);
}
