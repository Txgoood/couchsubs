// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.RegularExpressions;

namespace CouchSubs.Core.Dubbing;

internal static partial class DialogueTextFilter
{
    [GeneratedRegex(@"^\s*[-–—•]?\s*[\p{L}][\p{L}\p{M}\d ._'’-]{0,27}:\s*\S")]
    private static partial Regex SpeakerPrefix();

    [GeneratedRegex(@"^(?:(?:pres{0,2}|hol[d]?|ho d|tap|use)\b.*\b(?:talk|interact with(?: .+)?|(?:browse|open) (?:the )?shop menu)|interact with(?: .+)?|browse (?:the )?shop menu|purchase (?:a|an|the)\b.*)$")]
    private static partial Regex InterfaceCommand();

    [GeneratedRegex(@"^Return to(?: [\p{Lu}][\p{L}'’-]*(?: [\p{Lu}][\p{L}'’-]*){0,2})?[.。]?$")]
    private static partial Regex ReturnObjective();

    [GeneratedRegex(@"^\s*(?:Press|Hold|Tap)\s+(?:[A-Z0-9]|Esc|Enter|Space|Tab|F\d{1,2}|@|[○×△□])\s+to\s+\S")]
    private static partial Regex InputCommand();

    public static bool IsInterface(string raw)
    {
        if (ButtonPrompts.IsPrompt(raw) || InputCommand().IsMatch(raw)) return true;
        var text = TextMatch.Normalize(raw);
        if (text.StartsWith("objective ") || text.StartsWith("mission objective ") ||
            ReturnObjective().IsMatch(raw.Trim()) || ScreenTranslation.IsGameInterfaceText(raw)) return true;
        // Prose dialogue can start with an imperative or carry a speaker label.
        if (SubtitleStabilizer.EndsSentence(raw) || SpeakerPrefix().IsMatch(raw)) return false;
        // The icon can disappear or become letters. Recognize the action text as well.
        return InterfaceCommand().IsMatch(text);
    }

    public static bool CanSpeak(string text, TextBox? box, bool seenAgain)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Split('\n').Any(IsInterface)) return false;
        if (SpeakerPrefix().IsMatch(text)) return true;
        if (box is { } position && Math.Abs((position.Left + position.Right) / 2 - 0.5) > 0.13)
            return false;
        return SubtitleStabilizer.EndsSentence(text) ||
            seenAgain && TextMatch.Normalize(text).Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 2;
    }
}
