// SPDX-License-Identifier: AGPL-3.0-only
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CouchSubs.Core.Dubbing;

/// <summary>
/// Opt-in local game-scoped acoustic embeddings. No PCM is persisted.
/// All disk IO occurs in the isolated child, never on the audio callback.
/// </summary>
public sealed record SpeakerMemorySnapshot(Dictionary<string, float[]> Vectors, int NextAnonymous);

public sealed class GameSpeakerMemory(string directory)
{
    private sealed record Envelope(int Format, string Game, SpeakerMemorySnapshot Memory);
    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CouchSubs", "speaker-memory");
    public static string NormalizeGame(string? game)
    {
        var name = game?.Trim();
        if (string.IsNullOrWhiteSpace(name)) return "Genel";
        return name.Length > 120 ? name[..120] : name;
    }

    private string FileFor(string? game)
    {
        var safe = NormalizeGame(game).ToUpperInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(safe)));
        return Path.Combine(directory, hash + ".json");
    }

    public bool Load(string? game, HybridSpeakerRegistry registry)
    {
        try
        {
            var path = FileFor(game);
            if (!File.Exists(path) || new FileInfo(path).Length > 4_000_000) return false;
            var value = JsonSerializer.Deserialize<Envelope>(File.ReadAllText(path));
            return value is { Format: 1 } &&
                   string.Equals(value.Game, NormalizeGame(game), StringComparison.OrdinalIgnoreCase) &&
                   registry.Restore(value.Memory);
        }
        catch (Exception) { return false; }
    }

    public bool Save(string? game, HybridSpeakerRegistry registry)
    {
        string? temp = null;
        try
        {
            var snapshot = registry.Snapshot();
            if (snapshot.Vectors.Count > 256) return false;
            Directory.CreateDirectory(directory);
            var path = FileFor(game);
            temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(
                new Envelope(1, NormalizeGame(game), snapshot)));
            File.Move(temp, path, true);
            return true;
        }
        catch (Exception) { return false; }
        finally
        {
            if (temp is not null)
            {
                try { if (File.Exists(temp)) File.Delete(temp); }
                catch (Exception) { }
            }
        }
    }

    public void Delete(string? game)
    {
        try { File.Delete(FileFor(game)); }
        catch (Exception) { }
    }
}
