// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.RegularExpressions;

namespace CouchSubs.Core.Dubbing;

internal static partial class DialogueOcr
{
    [GeneratedRegex(@"^[\s.'’•]*(?<name>Jason|Lucia|Andrew|Locals?|Worker|Staff Member)\s*:\s*(?<body>\S.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex KnownLabel();

    [GeneratedRegex(@"^Localr\s+(?=\p{Lu})", RegexOptions.CultureInvariant)]
    private static partial Regex LocalAlias();

    public static string CleanLabel(string line)
    {
        line = LocalAlias().Replace(line, "Local: ");
        var match = KnownLabel().Match(line);
        if (!match.Success) return line;
        var name = match.Groups["name"].Value;
        var body = match.Groups["body"].Value;
        if (body.Length > 1 && body[0] == '.' && char.IsUpper(body[1])) body = body[1..];
        return body.Length == 0 ? line : $"{name}: {body}";
    }

    public static bool IsNamedTurn(string text) => KnownLabel().IsMatch(text) ||
        DialogueNameCandidates.ExplicitSpeaker(text) is not null;
}
