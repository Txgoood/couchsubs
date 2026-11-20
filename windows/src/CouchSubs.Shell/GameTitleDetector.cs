// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.RegularExpressions;

namespace CouchSubs.Shell;

/// <summary>
/// Deliberately conservative game-title screen recognition. A PS5 Remote Play
/// stream does not expose its game title through the existing protocol.
/// Do not guess the game from character dialogue or a single arbitrary word.
/// </summary>
public static class GameTitleDetector
{
    public static string? Detect(string? screenText)
    {
        if (string.IsNullOrWhiteSpace(screenText) || screenText.Length > 160) return null;
        var line = Regex.Replace(screenText.Trim(), @"\s+", " ").ToUpperInvariant();
        return line switch
        {
            "GRAND THEFT AUTO VI" or "GRAND THEFT AUTO 6" or "GTA VI" or "GTA 6" => "GTA 6",
            "GRAND THEFT AUTO V" or "GRAND THEFT AUTO 5" or "GTA V" or "GTA 5" => "GTA 5",
            _ => null
        };
    }
}
