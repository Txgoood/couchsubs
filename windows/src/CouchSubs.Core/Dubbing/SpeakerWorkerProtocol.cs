// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;

namespace CouchSubs.Core.Dubbing;

/// <summary>
/// Strict, bounded control-plane contract for a future OUT-OF-PROCESS
/// speaker embedding worker. No native model is loaded by this component.
/// Never send raw PCM or persistent voiceprints through this status channel.
/// </summary>
public static class SpeakerWorkerProtocol
{
    public const int ProtocolVersion = 1;
    public const int MaxMessageBytes = 1024;
    public static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan ResponseTimeout = TimeSpan.FromMilliseconds(750);

    public static bool TryReadStatus(ReadOnlySpan<byte> utf8, out SpeakerWorkerStatus status)
    {
        status = default;
        if (utf8.Length is 0 or > MaxMessageBytes) return false;
        try
        {
            using var document = JsonDocument.Parse(utf8.ToArray(), new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 4
            });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("protocol", out var version) ||
                version.ValueKind != JsonValueKind.Number ||
                !version.TryGetInt32(out var number) || number != ProtocolVersion ||
                !root.TryGetProperty("ready", out var ready) ||
                (ready.ValueKind != JsonValueKind.True && ready.ValueKind != JsonValueKind.False) ||
                !root.TryGetProperty("modelLoaded", out var model) ||
                (model.ValueKind != JsonValueKind.True && model.ValueKind != JsonValueKind.False))
                return false;
            // A loaded model is not sufficient evidence that inference is safe.
            var isReady = ready.GetBoolean();
            var loaded = model.GetBoolean();
            status = new SpeakerWorkerStatus(isReady && loaded, loaded);
            return true;
        }
        catch (JsonException) { return false; }
    }
}

public readonly record struct SpeakerWorkerStatus(bool Ready, bool ModelLoaded);

/// <summary>
/// An unhealthy child worker must never stop subtitles, dubbing, or game sound.
/// Fail closed after repeated errors; reset only on an explicit new session.
/// </summary>
public sealed class SpeakerWorkerCircuitBreaker
{
    private int failures;
    public bool IsOpen => failures >= 3;
    public bool CanSend => !IsOpen;
    public void ReportFailure() { if (failures < 3) failures++; }
    public void ReportSuccess() { if (!IsOpen) failures = 0; }
    public void ResetForNewSession() => failures = 0;
}
