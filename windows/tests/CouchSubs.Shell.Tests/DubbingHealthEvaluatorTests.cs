// SPDX-License-Identifier: AGPL-3.0-only
using CouchSubs.Shell;

namespace CouchSubs.Shell.Tests;

public sealed class DubbingHealthEvaluatorTests
{
    private static DubbingHealthInputs Normal => new(
        true, true, true, 0, 3, 400, 100, 450,
        2, 0, 4, 100, 0, null);

    [Fact]
    public void HealthySessionWithLowLatencyIsHealthy() =>
        Assert.Equal(DubbingHealthLevel.Healthy, DubbingHealthEvaluator.Evaluate(Normal).Level);

    [Fact]
    public void InactiveAndUnobservedSessionsNeverRaiseFalseAlarm()
    {
        Assert.Equal(DubbingHealthLevel.Idle,
            DubbingHealthEvaluator.Evaluate(Normal with { SessionRunning = false }).Level);
        Assert.Equal(DubbingHealthLevel.InsufficientData,
            DubbingHealthEvaluator.Evaluate(Normal with { SynthesisCalls = 0 }).Level);
    }

    [Fact]
    public void MissingModelOrReportedErrorAreVisibleWithoutAutomaticRestart()
    {
        Assert.Equal(DubbingHealthLevel.Error,
            DubbingHealthEvaluator.Evaluate(Normal with { ModelInstalled = false }).Level);
        Assert.Equal(DubbingHealthLevel.Error,
            DubbingHealthEvaluator.Evaluate(Normal with { DubbingError = "test" }).Level);
    }

    [Fact]
    public void BacklogAndTtsDelayAreWarnings()
    {
        Assert.Equal(DubbingHealthLevel.Warning,
            DubbingHealthEvaluator.Evaluate(Normal with { PendingLines = 3 }).Level);
        Assert.Equal(DubbingHealthLevel.Warning,
            DubbingHealthEvaluator.Evaluate(Normal with { LastSynthesisMs = 4200 }).Level);
    }

    [Fact]
    public void VadFailuresDoNotMasqueradeAsSpeechEmotion()
    {
        Assert.Equal(DubbingHealthLevel.Warning,
            DubbingHealthEvaluator.Evaluate(Normal with
            { SpeechInferenceCalls = 100, SpeechInferenceErrors = 25 }).Level);
        Assert.Equal(DubbingHealthLevel.Healthy,
            DubbingHealthEvaluator.Evaluate(Normal with
            { SpeechInferenceCalls = 0, SpeechInferenceErrors = 0 }).Level);
    }
}
