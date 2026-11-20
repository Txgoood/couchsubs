// SPDX-License-Identifier: AGPL-3.0-only
using CouchSubs.Core.Dubbing;

namespace CouchSubs.Shell;

/// <summary>
/// Safeguard for speaker changes in unlabelled game dialogue.
/// A rapid identity switch is NOT proof of simultaneous voices: it can be
/// ordinary turn taking. Treat it as a possible interruption and abstain from
/// automatic attribution for a short hold-off window. The subtitle/translation
/// pipeline is never paused. No raw game PCM or text is retained.
/// </summary>
public sealed class SpeakerTurnGuard
{
    private string? previousSpeaker;
    private long previousSeenMs;
    private long holdUntilMs;
    private int possibleInterruptions;
    private int abstentions;

    public int PossibleInterruptions => possibleInterruptions;
    public int Abstentions => abstentions;

    public SpeakerTurnDecision Evaluate(SpeakerIdentityResult? latest, long monotonicMs)
    {
        if (latest is not
            { Name: { Length: > 0 } name,
              Source: SpeakerIdentitySource.VoiceEmbedding, Confidence: >= 0.83f })
        {
            abstentions++;
            return new(null, TurnEvidence.Uncertain);
        }

        if (previousSpeaker is not null &&
            !string.Equals(previousSpeaker, name, StringComparison.OrdinalIgnoreCase) &&
            monotonicMs >= previousSeenMs &&
            monotonicMs - previousSeenMs < 1500)
        {
            // A confident new identity within 1.5s of another might be a
            // genuine interruption or just a quick hand-off. Don't guess.
            possibleInterruptions++;
            holdUntilMs = Math.Max(holdUntilMs, monotonicMs + 1800);
        }

        previousSpeaker = name;
        previousSeenMs = monotonicMs;

        if (monotonicMs < holdUntilMs)
        {
            abstentions++;
            return new(null, TurnEvidence.PossibleInterruption);
        }
        return new(name, TurnEvidence.SingleSpeakerCandidate);
    }

    public void Reset()
    {
        previousSpeaker = null;
        previousSeenMs = 0;
        holdUntilMs = 0;
        possibleInterruptions = 0;
        abstentions = 0;
    }
}

public enum TurnEvidence { Uncertain, SingleSpeakerCandidate, PossibleInterruption }
public readonly record struct SpeakerTurnDecision(string? Speaker, TurnEvidence Evidence);
