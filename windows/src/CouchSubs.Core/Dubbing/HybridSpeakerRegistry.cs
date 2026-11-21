// SPDX-License-Identifier: AGPL-3.0-only
namespace CouchSubs.Core.Dubbing;

/// <summary>
/// Evidence used to choose a speaker identity without trusting sound effects.
/// A speech embedding must come from a real pre-trained speaker model; Silero VAD
/// probabilities, RMS, or pitch alone are NEVER treated as identity embeddings.
/// </summary>
public sealed record SpeakerEvidence(
    string? SubtitleName,
    float[]? VoiceEmbedding,
    float EmbeddingQuality,
    bool SpeechConfirmed,
    bool Recent);

public enum SpeakerIdentitySource { SubtitleName, VoiceEmbedding, Uncertain }

public sealed record SpeakerIdentityResult(string? Name, SpeakerIdentitySource Source, float Confidence);

/// <summary>
/// Hybrid speaker identity registry, scoped to a single game.
/// Proper names win; audio-only speakers receive anonymous labels until named.
/// Two independent matching observations are required before enrolling a new voice.
/// No raw audio is retained, only normalized model embeddings.
/// </summary>
public sealed class HybridSpeakerRegistry
{
    private readonly Dictionary<string, float[]> known = new(StringComparer.OrdinalIgnoreCase);
    private float[]? pending;
    private int pendingCount;
    private int nextAnonymous = 1;
    private const float MatchThreshold = 0.83f;
    private const float MatchMargin = 0.06f;
    private const float NewSpeakerThreshold = 0.90f;

    public SpeakerIdentityResult Resolve(SpeakerEvidence evidence)
    {
        var name = evidence.SubtitleName?.Trim();
        if (!string.IsNullOrEmpty(name))
        {
            if (evidence.Recent && evidence.SpeechConfirmed &&
                TryNormalize(evidence.VoiceEmbedding, out var namedVector) &&
                evidence.EmbeddingQuality >= 0.8f)
                known[name] = namedVector;
            pending = null;
            pendingCount = 0;
            return new SpeakerIdentityResult(name, SpeakerIdentitySource.SubtitleName, 1f);
        }
        if (!evidence.Recent || !evidence.SpeechConfirmed ||
            evidence.EmbeddingQuality < 0.8f ||
            !TryNormalize(evidence.VoiceEmbedding, out var vector))
            return new SpeakerIdentityResult(null, SpeakerIdentitySource.Uncertain, 0);

        string? winner = null;
        var best = -1f;
        var second = -1f;
        foreach (var (key, stored) in known)
        {
            if (stored.Length != vector.Length) continue;
            var score = Dot(stored, vector);
            if (score > best)
            {
                second = best;
                best = score;
                winner = key;
            }
            else second = Math.Max(second, score);
        }
        if (winner is not null && best >= MatchThreshold &&
            (second < 0 || best - second >= MatchMargin))
        {
            pending = null;
            pendingCount = 0;
            return new SpeakerIdentityResult(winner, SpeakerIdentitySource.VoiceEmbedding, best);
        }

        // An ambiguous close match may be an existing character: never create a duplicate.
        if (best > 0.62f) return new SpeakerIdentityResult(null, SpeakerIdentitySource.Uncertain, 0);
        if (evidence.EmbeddingQuality < NewSpeakerThreshold)
            return new SpeakerIdentityResult(null, SpeakerIdentitySource.Uncertain, 0);

        if (pending is null || pending.Length != vector.Length || Dot(pending, vector) < MatchThreshold)
        {
            pending = vector;
            pendingCount = 1;
            return new SpeakerIdentityResult(null, SpeakerIdentitySource.Uncertain, 0);
        }
        pendingCount++;
        if (pendingCount < 2) return new SpeakerIdentityResult(null, SpeakerIdentitySource.Uncertain, 0);
        var anonymous = "Ses " + nextAnonymous++;
        known[anonymous] = vector;
        pending = null;
        pendingCount = 0;
        return new SpeakerIdentityResult(anonymous, SpeakerIdentitySource.VoiceEmbedding, 0.9f);
    }


    /// <summary>Validated, normalized vectors only. Never includes raw game audio.</summary>
    public SpeakerMemorySnapshot Snapshot() => new(
        known.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(),
            StringComparer.OrdinalIgnoreCase), nextAnonymous);

    /// <summary>Loads one game's embeddings; rejects malformed/unbounded data.</summary>
    public bool Restore(SpeakerMemorySnapshot snapshot)
    {
        if (snapshot.Vectors is null || snapshot.Vectors.Count > 256 ||
            snapshot.NextAnonymous is < 1 or > 1000000) return false;
        var validated = new Dictionary<string, float[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, vector) in snapshot.Vectors)
        {
            if (string.IsNullOrWhiteSpace(key) || key.Length > 64 ||
                !TryNormalize(vector, out var normalized) ||
                validated.ContainsKey(key)) return false;
            validated.Add(key, normalized);
        }
        known.Clear();
        foreach (var (key, vector) in validated) known.Add(key, vector);
        nextAnonymous = snapshot.NextAnonymous;
        pending = null;
        pendingCount = 0;
        return true;
    }

    public bool Rename(string oldName, string newName)
    {
        if (string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName) ||
            newName.Length > 64 || !known.TryGetValue(oldName, out var vector) ||
            (known.ContainsKey(newName) &&
             !string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase)))
            return false;
        known.Remove(oldName);
        known[newName] = vector;
        return true;
    }

    public bool Forget(string name) => known.Remove(name);

    public void Reset()
    {
        known.Clear();
        pending = null;
        pendingCount = 0;
        nextAnonymous = 1;
    }

    private static bool TryNormalize(float[]? values, out float[] normalized)
    {
        normalized = [];
        if (values is null || values.Length < 16 || values.Length > 4096) return false;
        double power = 0;
        foreach (var value in values)
        {
            if (!float.IsFinite(value)) return false;
            power += (double)value * value;
        }
        if (power <= 1e-10) return false;
        var inverse = (float)(1d / Math.Sqrt(power));
        normalized = values.Select(x => x * inverse).ToArray();
        return true;
    }

    private static float Dot(float[] a, float[] b)
    {
        double sum = 0;
        for (int i = 0; i < a.Length; i++) sum += (double)a[i] * b[i];
        return (float)sum;
    }
}
