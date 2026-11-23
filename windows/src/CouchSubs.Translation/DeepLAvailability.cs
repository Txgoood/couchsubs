// SPDX-License-Identifier: AGPL-3.0-only

using CouchSubs.Core;

namespace CouchSubs.Translation;

/// <summary>Shared by translators using one credential; dialogue context remains private to each lane.</summary>
public sealed class DeepLAvailability(TimeProvider? timeProvider = null)
{
    private readonly TimeProvider time = timeProvider ?? TimeProvider.System;
    private readonly object gate = new();
    private OnlineKeyState? error;
    private DateTimeOffset retryAt;

    public OnlineKeyState? LastError { get { lock (gate) return error; } }

    internal bool UseOnline(out bool checkQuota)
    {
        lock (gate)
        {
            checkQuota = false;
            if (error is OnlineKeyState.Invalid) return false;
            if (error is not OnlineKeyState.QuotaExceeded) return time.GetUtcNow() >= retryAt;
            if (time.GetUtcNow() >= retryAt)
            {
                checkQuota = true;
                retryAt = time.GetUtcNow().AddHours(1);
            }
            return false;
        }
    }

    internal void ReportFailure(OnlineKeyState reason)
    {
        lock (gate)
        {
            if (error is OnlineKeyState.Invalid ||
                error is OnlineKeyState.QuotaExceeded && reason == OnlineKeyState.Unreachable) return;
            error = reason;
            retryAt = reason == OnlineKeyState.QuotaExceeded
                ? time.GetUtcNow().AddHours(1) : time.GetUtcNow().AddMinutes(1);
        }
    }

    internal void ReportSuccess()
    {
        lock (gate)
        {
            if (error == OnlineKeyState.Unreachable)
            {
                error = null;
                retryAt = default;
            }
        }
    }

    public void ObserveUsage(OnlineKeyCheck check, bool initial = false)
    {
        // An unavailable status lookup does not override the last translation result or quota state.
        if (check.State == OnlineKeyState.Unreachable) return;
        lock (gate)
        {
            if (initial && error is not null) return;
            error = check.State == OnlineKeyState.Valid ? null : check.State;
            retryAt = error == OnlineKeyState.QuotaExceeded ? time.GetUtcNow().AddHours(1) : default;
        }
    }
}
