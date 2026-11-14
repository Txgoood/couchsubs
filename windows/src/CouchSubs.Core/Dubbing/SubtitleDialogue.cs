// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.RegularExpressions;

namespace CouchSubs.Core.Dubbing;

/// <summary>
/// Oyun altyazısındaki açık konuşmacı etiketlerini ve "- konuşma" satırlarını okur.
/// İsim bulunmadığında karakter/cinsiyet uydurmaz. Çeviri çok satırlı eşleşmiyorsa
/// metin atlamamak için tamamını tek anlatıcı cümlesi olarak okur.
/// </summary>
public static partial class SubtitleDialogue
{
    [GeneratedRegex(@"^\s*(?<name>[\p{L}][\p{L}\p{M}0-9 ._'-]{0,27})\s*:\s+(?<text>.+)$")]
    private static partial Regex NamePrefix();

    [GeneratedRegex(@"^\s*[-–—]\s*(?<text>.+)$")]
    private static partial Regex DialogueDash();

    public static IReadOnlyList<SpokenLine> Parse(string original, string translated)
    {
        if (string.IsNullOrWhiteSpace(translated)) return [];
        var sourceLines = Lines(original);
        var trLines = Lines(translated);
        if (sourceLines.Length != trLines.Length || trLines.Length == 0)
            return [new SpokenLine(original, translated.Trim())];

        var output = new List<SpokenLine>(trLines.Length);
        for (var i = 0; i < sourceLines.Length; i++)
        {
            var source = sourceLines[i];
            var tr = trLines[i];
            string? speaker = null;
            var sourceNamed = NamePrefix().Match(source);
            if (sourceNamed.Success && LooksLikeSpeaker(sourceNamed.Groups["name"].Value))
            {
                speaker = sourceNamed.Groups["name"].Value.Trim();
                source = sourceNamed.Groups["text"].Value;
                var translationNamed = NamePrefix().Match(tr);
                if (translationNamed.Success && 
                    string.Equals(translationNamed.Groups["name"].Value.Trim(), speaker, StringComparison.OrdinalIgnoreCase))
                    tr = translationNamed.Groups["text"].Value;
            }
            var sourceDash = DialogueDash().Match(source);
            var trDash = DialogueDash().Match(tr);
            if (sourceDash.Success) source = sourceDash.Groups["text"].Value;
            if (trDash.Success) tr = trDash.Groups["text"].Value;

            // Tire her replikte varsa ilkinden itibaren anlatıcı/partner dönüşümlü gider.
            output.Add(new SpokenLine(source.Trim(), tr.Trim(), speaker, speaker is null && sourceDash.Success));
        }
        return output.Where(x => x.Turkish.Length != 0).ToArray();
    }

    private static bool LooksLikeSpeaker(string name)
    {
        name = name.Trim();
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length > 3 || words.Length == 0) return false;
        // Bilinen karakter isimlerini normal yazımla da tanı.
        if (NameGender.Guess(name) is not null) return true;
        // Oyunların etiket biçimi: tamamı büyük harf (örn. "UNKNOWN SOLDIER:").
        return name.Length > 1 && name.Any(char.IsLetter) &&
               name.Where(char.IsLetter).All(char.IsUpper);
    }

    private static string[] Lines(string text) =>
        text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
