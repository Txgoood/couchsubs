// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.RegularExpressions;

namespace CouchSubs.Core.Dubbing;

/// <summary>
/// Conservative discovery of character names mentioned IN dialogue.
/// Vocatives and subtitle speaker prefixes are clues, not voice biometrics.
/// Do not attribute the current speaker from a name being addressed.
/// </summary>
public static partial class DialogueNameCandidates
{
    [GeneratedRegex(@"^\s*(?<name>[\p{Lu}][\p{L}\p{M}.'-]{1,30}(?:\s+[\p{Lu}][\p{L}\p{M}.'-]{1,20})?)\s*:\s+\S")]
    private static partial Regex SpeakerPrefix();

    [GeneratedRegex(@"(?:^|[.!?]\s+)(?:Hey|Listen|Look|Wait|Come on|Please|Yo|Oh)\s*,?\s+(?<name>[A-Z][a-zA-Z'-]{1,24})\s*[,!?]", RegexOptions.IgnoreCase)]
    private static partial Regex AddressedName();

    [GeneratedRegex(@"\b(?<name>[A-Z][a-zA-Z'-]{1,24})\s*,\s+(?:get|go|come|stop|look|listen|wait|please|help|run|move|don't|do\b)", RegexOptions.IgnoreCase)]
    private static partial Regex CommandAddress();

    private static readonly HashSet<string> Skip = new(StringComparer.OrdinalIgnoreCase)
    {
        "Hey", "Listen", "Look", "Wait", "Please", "Come", "Stop", "Help",
        "Everyone", "Anybody", "Nobody", "Somebody", "Police", "Officer",
        "Guys", "Girls", "Man", "Woman", "Sir", "Madam", "God", "There",
        "You", "We", "They", "Okay", "Yeah", "No", "Yes", "Now", "Hello"
    };

    public static IReadOnlyList<string> Mentioned(string original)
    {
        if (string.IsNullOrWhiteSpace(original) || original.Length > 3000) return [];
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in original.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var regex in new[] { AddressedName(), CommandAddress() })
                foreach (Match match in regex.Matches(line))
                {
                    var name = match.Groups["name"].Value.Trim();
                    if (!Skip.Contains(name) && name.Length is >= 2 and <= 32)
                        found.Add(name);
                }
        }
        return found.Take(6).ToArray();
    }

    /// <summary>Only a bona fide prefix is direct speaker evidence.</summary>
    public static string? ExplicitSpeaker(string original)
    {
        if (string.IsNullOrWhiteSpace(original)) return null;
        var match = SpeakerPrefix().Match(original);
        if (!match.Success) return null;
        var name = match.Groups["name"].Value.Trim();
        // Only uppercase speaker labels, or known game character names.
        if (!name.Where(char.IsLetter).All(char.IsUpper) &&
            NameGender.Guess(name) is null) return null;
        return Skip.Contains(name) ? null : name;
    }
}
