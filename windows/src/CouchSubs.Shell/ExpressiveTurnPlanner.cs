// SPDX-License-Identifier: AGPL-3.0-only
using CouchSubs.Core.Dubbing;

namespace CouchSubs.Shell;

/// <summary>
/// Conservative text-grounded continuity for expressive Turkish delivery.
/// Operates on subtitles only; explosions/music are never analyzed as emotion.
/// It does not modify pitch, timing, character identity, or PCM.
/// </summary>
public sealed class ExpressiveTurnPlanner
{
    public const long MaximumCarryMs = 2200;
    private const int MaximumSpeakers = 128;
    private readonly Dictionary<string, (SpeechEmotion Emotion, long AtMs)> previous =
        new(StringComparer.OrdinalIgnoreCase);
    public int CarriedTurns { get; private set; }
    public int NeutralTurns { get; private set; }

    public SpeechEmotion? Resolve(SpokenLine line, SpeechEmotion? verifiedEmotion,
        DubbingEmotionMode mode, long timestampMs, bool speechPresent)
    {
        if (mode == DubbingEmotionMode.Off) return null;
        if (line.English.Length is 0 or > 1500) return null;
        // The string label alone never warrants a new character voice.
        var speaker = line.Speaker?.Trim();
        var hasSpeaker = !string.IsNullOrWhiteSpace(speaker) && speaker.Length <= 64;
        if (line.NewTurn)
        {
            if (hasSpeaker)
                previous.Remove(speaker!);
            else
                previous.Clear();
        }
        var english = line.English.Trim();
        var lexical = SpeechEmotions.Guess(english);

        // Strong lexical evidence takes precedence over stale/mixed estimates.
        if (lexical is { } explicitEmotion)
        {
            if (hasSpeaker)
                Remember(speaker!, explicitEmotion, timestampMs);
            return explicitEmotion;
        }

        // New speaker turns must not inherit the previous character's emotion.
        // A plain question mark/exclamation is not proof of distress.
        if (line.NewTurn || !hasSpeaker || !english.Contains('!') ||
            (mode is DubbingEmotionMode.Acoustic or DubbingEmotionMode.Neural && !speechPresent))
        {
            NeutralTurns++;
            return null;
        }
        if (previous.TryGetValue(speaker!, out var last) &&
            verifiedEmotion == last.Emotion &&
            timestampMs >= last.AtMs &&
            timestampMs - last.AtMs <= MaximumCarryMs)
        {
            CarriedTurns++;
            return last.Emotion;
        }
        NeutralTurns++;
        return null;
    }

    private void Remember(string speaker, SpeechEmotion emotion, long timestampMs)
    {
        if (previous.Count >= MaximumSpeakers)
        {
            foreach (var key in previous.Where(x =>
                timestampMs >= x.Value.AtMs &&
                timestampMs - x.Value.AtMs > MaximumCarryMs)
                .Select(x => x.Key).ToArray())
                previous.Remove(key);
        }
        if (previous.Count < MaximumSpeakers || previous.ContainsKey(speaker))
            previous[speaker] = (emotion, timestampMs);
    }

    public void Reset()
    {
        previous.Clear();
        CarriedTurns = 0;
        NeutralTurns = 0;
    }
}
