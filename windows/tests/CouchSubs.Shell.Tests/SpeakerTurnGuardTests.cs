// SPDX-License-Identifier: AGPL-3.0-only
using CouchSubs.Core.Dubbing;
using CouchSubs.Shell;
using Xunit;

namespace CouchSubs.Shell.Tests;

public sealed class SpeakerTurnGuardTests
{
    private static SpeakerIdentityResult Voice(string name, float confidence = 0.95f) =>
        new(name, SpeakerIdentitySource.VoiceEmbedding, confidence);

    [Fact]
    public void StableSingleSpeakerIsNotBlocked()
    {
        var guard = new SpeakerTurnGuard();
        var first = guard.Evaluate(Voice("Ses 1"), 10000);
        Assert.Equal("Ses 1", first.Speaker);
        Assert.Equal(TurnEvidence.SingleSpeakerCandidate, first.Evidence);
        var second = guard.Evaluate(Voice("Ses 1"), 10700);
        Assert.Equal("Ses 1", second.Speaker);
        Assert.Equal(0, guard.PossibleInterruptions);
    }

    [Fact]
    public void FastTurnChangeDoesNotMisattributeAmbiguousSubtitle()
    {
        var guard = new SpeakerTurnGuard();
        Assert.Equal("Ses 1", guard.Evaluate(Voice("Ses 1"), 10000).Speaker);
        var possible = guard.Evaluate(Voice("Ses 2"), 10750);
        Assert.Null(possible.Speaker);
        Assert.Equal(TurnEvidence.PossibleInterruption, possible.Evidence);
        Assert.Equal(1, guard.PossibleInterruptions);
        Assert.Null(guard.Evaluate(Voice("Ses 2"), 11600).Speaker);
        Assert.Equal("Ses 2", guard.Evaluate(Voice("Ses 2"), 12600).Speaker);
    }

    [Fact]
    public void OrdinarySeparatedTurnsAreStillAttributed()
    {
        var guard = new SpeakerTurnGuard();
        guard.Evaluate(Voice("Ses 1"), 10000);
        var second = guard.Evaluate(Voice("Ses 2"), 15000);
        Assert.Equal("Ses 2", second.Speaker);
        Assert.Equal(0, guard.PossibleInterruptions);
    }

    [Fact]
    public void ModelUncertaintyNeverInventsAnIdentity()
    {
        var guard = new SpeakerTurnGuard();
        Assert.Null(guard.Evaluate(null, 10000).Speaker);
        Assert.Null(guard.Evaluate(Voice("Ses 1", 0.1f), 10050).Speaker);
        Assert.Equal(2, guard.Abstentions);
        guard.Reset();
        Assert.Equal(0, guard.PossibleInterruptions);
        Assert.Equal(0, guard.Abstentions);
    }

    [Fact]
    public void NamedSubtitlesCannotBeOverwrittenByGuessing()
    {
        // The controller only fills an EMPTY SpokenLine.Speaker; named subtitle
        // labels are respected independently of this ambiguity gate.
        var explicitName = "Lucia";
        var guard = new SpeakerTurnGuard();
        guard.Evaluate(Voice("Ses 1"), 10000);
        var ambiguous = guard.Evaluate(Voice("Ses 2"), 10500);
        var selected = string.IsNullOrWhiteSpace(explicitName)
            ? ambiguous.Speaker : explicitName;
        Assert.Equal("Lucia", selected);
    }

    [Fact]
    public void BackwardCompatibilityDefaultsOff()
    {
        var settings = new DubbingSettings();
        Assert.False(settings.InterruptionGuard);
        Assert.True(settings.AutoSpeakerTracking);
    }
}
