// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.RegularExpressions;

namespace CouchSubs.Core.Dubbing;

internal static partial class DialogueOcr
{
    [GeneratedRegex(@"^[\s.'’""•]*(?<name>Jason|Jasen|Lucia|Andrew|Aypdrew|Amdrew|Afygrew|gAndrew|Locals?|Worker|Staff Member)\s*(?::|[•.]\s*[•.])\s*(?<body>\S.*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex KnownLabel();

    [GeneratedRegex(@"^Localr\s+(?=\p{Lu})", RegexOptions.CultureInvariant)]
    private static partial Regex LocalAlias();

    public static string CleanLabel(string line)
    {
        line = LocalAlias().Replace(line, "Local: ");
        var match = KnownLabel().Match(line);
        if (!match.Success) return line;
        var name = match.Groups["name"].Value;
        name = name.ToLowerInvariant() switch
        {
            "aypdrew" or "amdrew" or "afygrew" or "gandrew" => "Andrew",
            "jasen" => "Jason",
            _ => name
        };
        var body = match.Groups["body"].Value;
        if (body.Length > 1 && body[0] == '.' && char.IsUpper(body[1])) body = body[1..];
        return body.Length == 0 ? line : $"{name}: {body}";
    }

    public static bool IsNamedTurn(string text) => KnownLabel().IsMatch(text) ||
        DialogueNameCandidates.ExplicitSpeaker(text) is not null;

    public static bool IsSameTurn(string first, string second)
    {
        if (first.Length == 0) return false;
        if (first == second) return true;
        if (first.Contains('\n') || second.Contains('\n')) return false;
        var (firstName, firstBody) = SplitTurn(first);
        var (secondName, secondBody) = SplitTurn(second);
        if (firstName.Length > 0 && secondName.Length > 0 && firstName != secondName) return false;
        return firstBody == secondBody && firstBody.Length > 0 ||
            Math.Min(firstBody.Length, secondBody.Length) >= 14 && TextMatch.IsSimilar(firstBody, secondBody, .9);
    }

    private static (string Name, string Body) SplitTurn(string line)
    {
        var separator = line.IndexOf(':');
        return separator is > 0 and <= 28
            ? (TextMatch.Normalize(line[..separator]), TextMatch.Normalize(line[(separator + 1)..]))
            : ("", TextMatch.Normalize(line));
    }
}
