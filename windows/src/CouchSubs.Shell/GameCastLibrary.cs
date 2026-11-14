// SPDX-License-Identifier: AGPL-3.0-only

using CouchSubs.Core.Dubbing;

namespace CouchSubs.Shell;

/// <summary>
/// Oyun değişiminde eski karakterleri korur. Ses dosyaları çoğaltılmaz:
/// her karakter yalnızca mevcut 6 temel sesten birine işaret eder.
/// </summary>
public static class GameCastLibrary
{
    public const string DefaultGame = "Genel";

    public static string Normalize(string? game)
    {
        var title = (game ?? "").Trim();
        if (title.Length == 0) return DefaultGame;
        return title.Length > 80 ? title[..80] : title;
    }

    public static IReadOnlyList<string> Games(DubbingSettings settings)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            DefaultGame,
            Normalize(settings.ActiveGame)
        };
        foreach (var title in settings.GameCasts.Keys)
            names.Add(Normalize(title));
        return names.OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    public static DubbingSettings Switch(DubbingSettings settings, string? game)
    {
        var target = Normalize(game);
        var current = Normalize(settings.ActiveGame);
        if (string.Equals(target, current, StringComparison.OrdinalIgnoreCase)) return settings;
        var saved = new Dictionary<string, VoiceCast>(settings.GameCasts, StringComparer.OrdinalIgnoreCase)
        {
            [current] = settings.Cast.Copy()
        };
        var next = saved.TryGetValue(target, out var cast) ? cast.Copy() : new VoiceCast
        {
            Narrator = settings.Cast.Narrator,
            Partner = settings.Cast.Partner,
        };
        return settings with { ActiveGame = target, Cast = next, GameCasts = saved };
    }

    public static DubbingSettings SaveCurrent(DubbingSettings settings)
    {
        var saved = new Dictionary<string, VoiceCast>(settings.GameCasts, StringComparer.OrdinalIgnoreCase)
        {
            [Normalize(settings.ActiveGame)] = settings.Cast.Copy()
        };
        return settings with { GameCasts = saved };
    }
}
