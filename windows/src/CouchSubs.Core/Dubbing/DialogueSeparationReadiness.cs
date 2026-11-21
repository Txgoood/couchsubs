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
    bool TestedOnTargetHardware);

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
        if (model.SampleRate != LiveSampleRate || model.Channels != LiveChannels)
            return offline with { Reason = "Model requires resampling/channel adaptation." };
        if (model.InputWindowMilliseconds > LiveMaximumAgeMs ||
            model.AlgorithmicLookAheadMilliseconds > LiveMaximumAgeMs ||
            model.ObservedInferenceMilliseconds <= 0 ||
            model.ObservedInferenceMilliseconds > LiveMaximumAgeMs)
            return offline with { Reason = "Window, look-ahead or measured inference exceeds live budget." };
        if (!model.ValidatedOnGameAudio || !model.TestedOnTargetHardware)
            return offline with { Reason = "Target game and real device testing missing." };

        return new(true, true,
            "Preflight eligible only; synchronous audio callbacks remain forbidden.");
    }
}
