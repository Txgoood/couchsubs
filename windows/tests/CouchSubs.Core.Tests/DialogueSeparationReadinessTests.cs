// SPDX-License-Identifier: AGPL-3.0-only
using CouchSubs.Core.Dubbing;

namespace CouchSubs.Core.Tests;

public sealed class DialogueSeparationReadinessTests
{
    private static DialogueSeparatorCapabilities Candidate => new(
        "DBS-TIGER-DnR", 44100, 1, 6000, 0, 0, true, false, true, false);

    [Fact]
    public void SixSecondMonoOfflineResearchNeverPassesPs5LiveReadiness()
    {
        var result = DialogueSeparationReadiness.Assess(Candidate);
        Assert.True(result.OfflineEvaluationAllowed);
        Assert.False(result.LiveGameAudioAllowed);
    }

    [Fact]
    public void ModelWithoutClearLicenseCannotBeBundled()
    {
        var result = DialogueSeparationReadiness.Assess(Candidate with { Redistributable = false });
        Assert.False(result.OfflineEvaluationAllowed);
    }

    [Fact]
    public void NinetyMillisecondBudgetRequiresMeasuredHardwareAndStereo()
    {
        var baseModel = Candidate with
        {
            SampleRate = 48000, Channels = 2, InputWindowMilliseconds = 20,
            AlgorithmicLookAheadMilliseconds = 10, ObservedInferenceMilliseconds = 35,
            ValidatedOnGameAudio = true, TestedOnTargetHardware = true,
            MeasuredTransportMilliseconds = 4
        };
        Assert.True(DialogueSeparationReadiness.Assess(baseModel).LiveGameAudioAllowed);
        Assert.False(DialogueSeparationReadiness.Assess(baseModel with
        { ObservedInferenceMilliseconds = 91 }).LiveGameAudioAllowed);
        Assert.False(DialogueSeparationReadiness.Assess(baseModel with
        { InputWindowMilliseconds = 120 }).LiveGameAudioAllowed);
        Assert.False(DialogueSeparationReadiness.Assess(baseModel with
        { TestedOnTargetHardware = false }).LiveGameAudioAllowed);
        Assert.False(DialogueSeparationReadiness.Assess(baseModel with
        { MeasuredTransportMilliseconds = 0 }).LiveGameAudioAllowed);
        // A 48k mono model is only live-eligible with separate verified
        // stereo reconstruction and the full 90ms end-to-end budget.
        Assert.False(DialogueSeparationReadiness.Assess(baseModel with
        { Channels = 1 }).LiveGameAudioAllowed);
        Assert.True(DialogueSeparationReadiness.Assess(baseModel with
        { Channels = 1, StereoRestorationVerified = true }).LiveGameAudioAllowed);
        Assert.False(DialogueSeparationReadiness.Assess(baseModel with
        { InputWindowMilliseconds = 43, AlgorithmicLookAheadMilliseconds = 20,
          ObservedInferenceMilliseconds = 20, MeasuredTransportMilliseconds = 8 }).LiveGameAudioAllowed);
    }

    [Fact]
    public void MissingDialogueStemIsNotSpeechSeparation()
    {
        Assert.False(DialogueSeparationReadiness.Assess(Candidate with
        { HasDialogueStem = false }).OfflineEvaluationAllowed);
    }
}
