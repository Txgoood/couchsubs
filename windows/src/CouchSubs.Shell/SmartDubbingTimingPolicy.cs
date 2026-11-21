// SPDX-License-Identifier: AGPL-3.0-only
using CouchSubs.Core.Dubbing;

namespace CouchSubs.Shell;

/// <summary>
/// Deterministic, opt-in latency guard. Only metadata and a short-lived hash
/// are retained. Never pauses the PS5 stream or writes dialogue to disk.
/// </summary>
public sealed class SmartDubbingTimingPolicy
{
    public const long DuplicateWindowMs = 900;
    public const long MaxDialogueAgeMs = 3500;
    private int? lastFingerprint;
    private long lastAcceptedMs = -1;
    public int SuppressedDuplicates { get; private set; }
    public int DiscardedLateLines { get; private set; }

    public bool Accept(IReadOnlyList<SpokenLine> lines, long nowMs)
    {
        if (lines.Count == 0) return false;
        var hash = new HashCode();
        foreach (var line in lines)
        {
            hash.Add(line.English.Trim(), StringComparer.OrdinalIgnoreCase);
            hash.Add(line.Turkish.Trim(), StringComparer.OrdinalIgnoreCase);
            hash.Add(line.Speaker?.Trim(), StringComparer.OrdinalIgnoreCase);
        }
        var fingerprint = hash.ToHashCode();
        if (lastFingerprint == fingerprint && lastAcceptedMs >= 0 &&
            nowMs >= lastAcceptedMs && nowMs - lastAcceptedMs <= DuplicateWindowMs)
        {
            SuppressedDuplicates++;
            return false;
        }
        lastFingerprint = fingerprint;
        lastAcceptedMs = nowMs;
        return true;
    }

    public bool ShouldPlay(long submittedMs, long nowMs)
    {
        if (submittedMs < 0 || nowMs < submittedMs ||
            nowMs - submittedMs > MaxDialogueAgeMs)
        {
            DiscardedLateLines++;
            return false;
        }
        return true;
    }

    public void Reset()
    {
        lastFingerprint = null;
        lastAcceptedMs = -1;
        SuppressedDuplicates = 0;
        DiscardedLateLines = 0;
    }
}
