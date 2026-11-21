// SPDX-License-Identifier: AGPL-3.0-only
using CouchSubs.Core.Dubbing;

namespace CouchSubs.Shell;

/// <summary>
/// Conservative attribution of three or more alternating speakers.
/// This is turn tracking, NOT neural source separation: one mixed PCM stream
/// cannot reliably identify two people speaking simultaneously.
/// </summary>
public sealed class MultiSpeakerTurnTracker
{
    private readonly Dictionary<string, long> seen = new(StringComparer.OrdinalIgnoreCase);
    private string? accepted;
    private string? pending;
    private int confirmations;
    private long lastObservationMs = -1;
    private long lastTransitionMs = -1;
    private long uncertainUntilMs;
    private int ambiguousTransitions;

    public int KnownSpeakers => seen.Count;
    public int AmbiguousTransitions => ambiguousTransitions;

    public SpeakerTurnDecision Evaluate(SpeakerIdentityResult? latest, long monotonicMs)
    {
        if (monotonicMs < 0 || (lastObservationMs >= 0 && monotonicMs < lastObservationMs))
        {
            Reset();
            return new(null, TurnEvidence.Uncertain);
        }

        // Re-reading the same cached inference at the same timestamp is never
        // a second independent observation. Avoid phantom confirmations.
        if (lastObservationMs == monotonicMs)
            return new(null, TurnEvidence.Uncertain);

        var oldObservation = lastObservationMs;
        lastObservationMs = monotonicMs;
        if (latest is not
            { Name: { Length: > 0 and <= 64 } name,
              Source: SpeakerIdentitySource.VoiceEmbedding,
              Confidence: >= 0.90f } ||
            string.IsNullOrWhiteSpace(name))
        {
            pending = null;
            confirmations = 0;
            return new(null, TurnEvidence.Uncertain);
        }

        if (!seen.ContainsKey(name) && seen.Count >= 128)
        {
            // Never grow unlimited from model noise during a long GTA session.
            return new(null, TurnEvidence.Uncertain);
        }

        seen[name] = monotonicMs;
        if (accepted is null)
        {
            accepted = name;
            lastTransitionMs = monotonicMs;
            return new(name, TurnEvidence.SingleSpeakerCandidate);
        }

        if (string.Equals(accepted, name, StringComparison.OrdinalIgnoreCase))
        {
            pending = null;
            confirmations = 0;
            if (monotonicMs < uncertainUntilMs)
                return new(null, TurnEvidence.PossibleInterruption);
            return new(name, TurnEvidence.SingleSpeakerCandidate);
        }

        // Rapid A->B->A or A->B->C is a possible interruption. A real
        // multi-source model is required before claiming simultaneous speech.
        if (lastTransitionMs >= 0 && monotonicMs - lastTransitionMs < 1500)
        {
            ambiguousTransitions++;
            uncertainUntilMs = Math.Max(uncertainUntilMs, monotonicMs + 1800);
        }

        if (!string.Equals(pending, name, StringComparison.OrdinalIgnoreCase) ||
            oldObservation < 0 || monotonicMs - oldObservation > 1500)
        {
            pending = name;
            confirmations = 1;
        }
        else
            confirmations++;

        if (confirmations < 2 || monotonicMs < uncertainUntilMs)
            return new(null, TurnEvidence.PossibleInterruption);

        accepted = name;
        lastTransitionMs = monotonicMs;
        pending = null;
        confirmations = 0;
        return new(name, TurnEvidence.SingleSpeakerCandidate);
    }

    public void Reset()
    {
        seen.Clear();
        accepted = null;
        pending = null;
        confirmations = 0;
        lastObservationMs = -1;
        lastTransitionMs = -1;
        uncertainUntilMs = 0;
        ambiguousTransitions = 0;
    }
}
