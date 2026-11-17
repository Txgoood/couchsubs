// SPDX-License-Identifier: AGPL-3.0-only
using CouchSubs.Core.Dubbing;

namespace CouchSubs.Shell;

/// <summary>
/// Fast, local, text-only emotion routing. It never captures raw voice or calls a network model.
/// Character identity is kept separate: emotion changes delivery, not the assigned voice.
/// </summary>
public sealed class EmotionContinuity(Func<DateTimeOffset>? clock = null)
{
    private readonly object gate = new();
    private readonly Dictionary<string, (SpeechEmotion Emotion, DateTimeOffset At)> memory =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<DateTimeOffset> now = clock ?? (() => DateTimeOffset.UtcNow);

    public SpeechEmotion? Resolve(SpokenLine line, DubbingEmotionMode mode)
    {
        if (mode == DubbingEmotionMode.Off) return null;
        var guess = SpeechEmotions.Guess(line.English);
        // Cinematic mode uses extra punctuation cues, but never guesses a speaker's identity.
        if (mode == DubbingEmotionMode.Cinematic && guess is null)
        {
            if (line.English.TrimEnd().EndsWith('!')) guess = SpeechEmotion.Angry;
            else if (line.English.TrimEnd().EndsWith('?')) guess = SpeechEmotion.Surprised;
        }
        var speaker = line.Speaker?.Trim();
        if (string.IsNullOrEmpty(speaker)) return guess;
        lock (gate)
        {
            var time = now();
            // Bound memory in long sessions. Old entries cannot affect current dialogue.
            if (memory.Count > 500)
                foreach (var key in memory.Where(pair => time - pair.Value.At > TimeSpan.FromSeconds(8))
                                          .Select(pair => pair.Key).ToArray())
                    memory.Remove(key);
            if (guess is { } tagged)
            {
                memory[speaker] = (tagged, time);
                return tagged;
            }
            // Only cinematic mode carries emotion to the next emphatic line within a short turn.
            // Plain neutral sentences must stay neutral to avoid repetitive theatrical voices.
            if (mode == DubbingEmotionMode.Cinematic &&
                line.English.Contains('!') && memory.TryGetValue(speaker, out var prior) &&
                time - prior.At <= TimeSpan.FromSeconds(6))
                return prior.Emotion;
        }
        return null;
    }
}
