// SPDX-License-Identifier: AGPL-3.0-only
namespace CouchSubs.Core.Dubbing;

/// <summary>
/// Explicit readiness checks for a *real* dialogue/music/effects separator.
/// Do not feed offline, mono, or multi-second chunks to the 48kHz stereo
/// 90ms live PS5 mixer. A model license is not evidence of low latency.
/// </summary>
public readonly record struct DialogueSeparatorCapabilities(
    string ModelId, int SampleRate, int Channels,
    int InputWindowMilliseconds, int AlgorithmicLookAheadMilliseconds,
    int ObservedInferenceMilliseconds, bool HasDialogueStem,
    bool ValidatedOnGameAudio, bool Redistributable,
    bool TestedOnTargetHardware, bool StereoRestorationVerified = false,
    int MeasuredTransportMilliseconds = 0);

public readonly record struct DialogueSeparatorReadiness(
    bool OfflineEvaluationAllowed, bool LiveGameAudioAllowed, string Reason);

public static class DialogueSeparationReadiness
{
    public const int LiveSampleRate = 48000;
    public const int LiveChannels = 2;
    public const int LiveMaximumAgeMs = 90;

    public static DialogueSeparatorReadiness Assess(in DialogueSeparatorCapabilities model)
    {
        if (string.IsNullOrWhiteSpace(model.ModelId) || !model.Redistributable ||
            !model.HasDialogueStem || model.SampleRate <= 0 ||
            model.Channels is < 1 or > 2 || model.InputWindowMilliseconds <= 0)
            return new(false, false, "Model metadata, licensing or speech stem is missing.");

        var offline = new DialogueSeparatorReadiness(true, false,
            "Offline evaluation only; original PS5 PCM stays unchanged.");
        if (model.SampleRate != LiveSampleRate)
            return offline with { Reason = "Model requires resampling." };
        if (model.Channels == 1 && !model.StereoRestorationVerified)
            return offline with { Reason = "Mono analysis has not proved safe stereo reconstruction." };
        if (model.InputWindowMilliseconds > LiveMaximumAgeMs ||
            model.AlgorithmicLookAheadMilliseconds < 0 ||
            model.ObservedInferenceMilliseconds <= 0 ||
            model.MeasuredTransportMilliseconds <= 0)
            return offline with { Reason = "Verified positive inference/transport timings are required." };
        // Age is a TOTAL budget, not four independent 90ms allowances.
        // 48k Inworld 2048-point STFT ~43ms + 20ms lookahead => 63ms,
        // leaving only 27ms for inference, transport and scheduler jitter.
        var endToEnd = (long)model.InputWindowMilliseconds +
            model.AlgorithmicLookAheadMilliseconds +
            model.ObservedInferenceMilliseconds +
            model.MeasuredTransportMilliseconds;
        if (endToEnd > LiveMaximumAgeMs)
            return offline with { Reason = "Total window + lookahead + inference + transport exceeds 90ms." };
        if (!model.ValidatedOnGameAudio || !model.TestedOnTargetHardware)
            return offline with { Reason = "Target game and real device testing missing." };

        return new(true, true,
            "Preflight eligible only; synchronous audio callbacks remain forbidden.");
    }
}
