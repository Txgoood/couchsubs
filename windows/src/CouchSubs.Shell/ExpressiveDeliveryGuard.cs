// SPDX-License-Identifier: AGPL-3.0-only
using CouchSubs.Core.Dubbing;

namespace CouchSubs.Shell;

/// <summary>
/// Conservative expressive TTS policy. Game audio is mixed and therefore
/// cannot independently confirm anger, sadness, or excitement. This gate
/// only inspects the same English subtitle text used for translation.
/// Never modifies the original soundtrack or the selected character voice.
/// </summary>
public sealed class ExpressiveDeliveryGuard
{
    private readonly Dictionary<string, (SpeechEmotion Emotion, long AtMs)> last =
        new(StringComparer.OrdinalIgnoreCase);
    public int SuppressedAmbiguous { get; private set; }
    public const long ContinuityWindowMs = 3500;
    private const int MaxSpeakers = 256;

    public SpeechEmotion? Refine(SpokenLine line, SpeechEmotion? candidate,
        DubbingEmotionMode mode, long nowMs, bool speechConfirmed)
    {
        if (mode == DubbingEmotionMode.Off || candidate is null) return null;
        if (line.English.Length is 0 or > 4000) return null;

        var sentence = line.English.Trim();
        var explicitCue = SpeechEmotions.Guess(sentence);
        var hasPunctuation = sentence.Contains('!') || sentence.Contains('?');
        var speaker = line.Speaker?.Trim();

        // Strong lexical evidence wins, including short urgent commands.
        // Music/explosions must never substitute for lexical evidence.
        if (explicitCue == candidate)
        {
            if (!string.IsNullOrWhiteSpace(speaker) && speaker.Length <= 64)
                Remember(speaker, candidate.Value, nowMs);
            return candidate;
        }

        // No explicit lexical cue: an isolated exclamation mark is NOT anger.
        if (candidate == SpeechEmotion.Angry || candidate == SpeechEmotion.Surprised)
        {
            SuppressedAmbiguous++;
            return null;
        }

        // Only permit inherited emotion for the same named character
        // inside a short window. Acoustic modes additionally need speech VAD.
        if (hasPunctuation && !string.IsNullOrWhiteSpace(speaker) &&
            last.TryGetValue(speaker, out var previous) &&
            previous.Emotion == candidate && nowMs >= previous.AtMs &&
            nowMs - previous.AtMs <= ContinuityWindowMs &&
            (mode is not (DubbingEmotionMode.Acoustic or DubbingEmotionMode.Neural) || speechConfirmed))
            return candidate;

        SuppressedAmbiguous++;
        return null;
    }

    private void Remember(string speaker, SpeechEmotion emotion, long atMs)
    {
        if (last.Count >= MaxSpeakers)
        {
            foreach (var key in last.Where(pair => atMs >= pair.Value.AtMs &&
                     atMs - pair.Value.AtMs > ContinuityWindowMs)
                     .Select(pair => pair.Key).ToArray())
                last.Remove(key);
        }
        if (last.Count < MaxSpeakers || last.ContainsKey(speaker))
            last[speaker] = (emotion, atMs);
    }

    public void Reset()
    {
        last.Clear();
        SuppressedAmbiguous = 0;
    }
}
