// SPDX-License-Identifier: AGPL-3.0-only

using CouchSubs.Core.Dubbing;
using System.Text.Json;

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


    // Only voice mapping metadata is archived. Shared voice-model downloads and
    // transient PCM/ONNX samples are not copied into each game profile.
    public const int BackupSchema = 1;
    public sealed record GameBackup(int Schema, string Game, VoiceCast Cast, DateTimeOffset ExportedAtUtc);

    public static string ExportJson(DubbingSettings settings, string game)
    {
        game = Normalize(game);
        var source = string.Equals(settings.ActiveGame, game, StringComparison.OrdinalIgnoreCase)
            ? settings.Cast : settings.GameCasts.TryGetValue(game, out var stored) ? stored
            : throw new InvalidOperationException("Oyun kütüphanesi bulunamadı.");
        return JsonSerializer.Serialize(new GameBackup(BackupSchema, game, source.Copy(),
            DateTimeOffset.UtcNow), new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>Import a versioned JSON backup. Existing games are never replaced implicitly.</summary>
    public static DubbingSettings ImportJson(DubbingSettings settings, string json, bool overwrite)
    {
        if (json.Length > 2_000_000) throw new InvalidDataException("Yedek dosyası çok büyük.");
        GameBackup backup;
        try { backup = JsonSerializer.Deserialize<GameBackup>(json)
                 ?? throw new InvalidDataException("Boş yedek."); }
        catch (JsonException error) { throw new InvalidDataException("Geçersiz kütüphane JSON dosyası.", error); }

        if (backup.Schema != BackupSchema || backup.Game is null ||
            backup.Game.Length is 0 or > 80 || backup.Game != Normalize(backup.Game) ||
            backup.Cast is null)
            throw new InvalidDataException("Desteklenmeyen veya bozuk kütüphane yedeği.");

        var cast = backup.Cast;
        if (cast.Assignments is null || cast.Groups is null ||
            cast.Assignments.Count > 5000 || cast.Groups.Count > 5000 ||
            !Enum.IsDefined(cast.Narrator) || !Enum.IsDefined(cast.Partner) ||
            cast.Assignments.Any(pair => string.IsNullOrWhiteSpace(pair.Key) ||
                pair.Key.Length > 64 || !Enum.IsDefined(pair.Value)) ||
            cast.Groups.Any(pair => string.IsNullOrWhiteSpace(pair.Key) ||
                pair.Key.Length > 64 || !Enum.IsDefined(pair.Value)))
            throw new InvalidDataException("Yedek karakter listesi veya ses değerleri geçersiz.");

        var name = backup.Game;
        var current = string.Equals(settings.ActiveGame, name, StringComparison.OrdinalIgnoreCase);
        var present = current || settings.GameCasts.ContainsKey(name);
        if (present && !overwrite)
            throw new InvalidOperationException("Bu oyunun kütüphanesi zaten mevcut; üzerine yazma onayı gerekir.");

        var copies = new Dictionary<string, VoiceCast>(settings.GameCasts, StringComparer.OrdinalIgnoreCase)
        {
            [name] = cast.Copy()
        };
        if (current)
            return settings with { Cast = cast.Copy(), GameCasts = copies };
        return settings with { GameCasts = copies };
    }

    /// <summary>Delete one game's mapping, never delete global voices or translation models.</summary>
    public static DubbingSettings Delete(DubbingSettings settings, string game)
    {
        game = Normalize(game);
        if (string.Equals(game, DefaultGame, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Genel profili silinemez.");
        var active = string.Equals(settings.ActiveGame, game, StringComparison.OrdinalIgnoreCase);
        if (!active && !settings.GameCasts.ContainsKey(game))
            throw new InvalidOperationException("Silinecek oyun bulunamadı.");

        var copies = new Dictionary<string, VoiceCast>(settings.GameCasts, StringComparer.OrdinalIgnoreCase);
        copies.Remove(game);
        if (!active) return settings with { GameCasts = copies };

        var fallback = copies.TryGetValue(DefaultGame, out var generic)
            ? generic.Copy() : new VoiceCast();
        // Do not call Switch: Switch intentionally saves the outgoing game.
        return settings with { ActiveGame = DefaultGame, Cast = fallback, GameCasts = copies };
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
            MaleNpcVoice = settings.Cast.MaleNpcVoice,
            FemaleNpcVoice = settings.Cast.FemaleNpcVoice,
            UnknownNpcVoice = settings.Cast.UnknownNpcVoice,
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
