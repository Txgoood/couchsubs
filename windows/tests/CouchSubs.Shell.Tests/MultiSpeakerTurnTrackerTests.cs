// SPDX-License-Identifier: AGPL-3.0-only
using CouchSubs.Core.Dubbing;
using CouchSubs.Shell;

namespace CouchSubs.Shell.Tests;

public sealed class MultiSpeakerTurnTrackerTests
{
    private static SpeakerIdentityResult Voice(string name, float confidence = 0.95f)
        => new(name, SpeakerIdentitySource.VoiceEmbedding, confidence);

    [Fact]
    public void ThreeSeparatedSpeakersAreTrackedWithoutMerging()
    {
        var tracker = new MultiSpeakerTurnTracker();
        Assert.Equal("Ses 1", tracker.Evaluate(Voice("Ses 1"), 10000).Speaker);
        Assert.Null(tracker.Evaluate(Voice("Ses 2"), 13000).Speaker);
        Assert.Equal("Ses 2", tracker.Evaluate(Voice("Ses 2"), 13500).Speaker);
        Assert.Null(tracker.Evaluate(Voice("Ses 3"), 17000).Speaker);
        Assert.Equal("Ses 3", tracker.Evaluate(Voice("Ses 3"), 17400).Speaker);
        Assert.Equal(3, tracker.KnownSpeakers);
    }

    [Fact]
    public void RapidInterruptionsNeverAssignTheWrongSpeaker()
    {
        var tracker = new MultiSpeakerTurnTracker();
        tracker.Evaluate(Voice("Lucia"), 10000);
        Assert.Null(tracker.Evaluate(Voice("Jason"), 10200).Speaker);
        Assert.Null(tracker.Evaluate(Voice("Guard"), 10400).Speaker);
        Assert.True(tracker.AmbiguousTransitions >= 2);
    }

    [Fact]
    public void UnknownLowConfidenceAndOldTimestampFailClosed()
    {
        var tracker = new MultiSpeakerTurnTracker();
        Assert.Null(tracker.Evaluate(Voice("Lucia", .4f), 10000).Speaker);
        Assert.Equal("Lucia", tracker.Evaluate(Voice("Lucia"), 10200).Speaker);
        Assert.Null(tracker.Evaluate(Voice("Jason"), 10200).Speaker);
        Assert.Null(tracker.Evaluate(Voice("Jason"), 9000).Speaker);
        Assert.Equal(0, tracker.KnownSpeakers);
    }

    [Fact]
    public void ThreeSpeakerRegistryIsScopedToOneSession()
    {
        var tracker = new MultiSpeakerTurnTracker();
        tracker.Evaluate(Voice("A"), 10000);
        tracker.Evaluate(Voice("B"), 13000);
        tracker.Evaluate(Voice("C"), 16000);
        Assert.Equal(3, tracker.KnownSpeakers);
        tracker.Reset();
        Assert.Equal(0, tracker.KnownSpeakers);
    }
}
