// SPDX-License-Identifier: AGPL-3.0-only
using CouchSubs.Core.Dubbing;
using CouchSubs.Shell;
using Xunit;

namespace CouchSubs.Shell.Tests;

public sealed class SpeechCandidateTrackerTests
{
    [Fact]
    public void QuietMusicAndSingleTransientAreNotConsideredSpeech()
    {
        var detector = new SpeechCandidateTracker();
        var now = 1000L;
        for (var i = 0; i < 10; i++)
            detector.Observe(0.1f, 0f, 0f, 50, now += 50);
        Assert.False(detector.IsCandidate(now));
        detector.Observe(0.5f, 0.1f, 0.4f, 20, now += 20);
        Assert.False(detector.IsCandidate(now));
    }

    [Fact]
    public void SustainedVoiceLikeActivityActivatesButCannotOutlastAudio()
    {
        var detector = new SpeechCandidateTracker();
        var now = 1000L;
        for (var i = 0; i < 7; i++)
            detector.Observe(i % 2 == 0 ? 0.25f : 0.04f, 0.08f, 0.1f, 50, now += 50);
        Assert.True(detector.IsCandidate(now));
        Assert.False(detector.IsCandidate(now + 451));
        detector.Reset();
        Assert.False(detector.IsCandidate(now));
    }

    [Fact]
    public void InvalidSampleDoesNotInventActivity()
    {
        var detector = new SpeechCandidateTracker();
        for (var i = 0; i < 30; i++)
            detector.Observe(float.NaN, 0.2f, 0.3f, 50, 1000 + i * 50);
        Assert.False(detector.IsCandidate(2500));
    }

    [Fact]
    public void AcousticModeRequiresEvidenceForEmotionCarry()
    {
        var time = new DateTimeOffset(2026, 11, 18, 0, 0, 0, TimeSpan.Zero);
        var engine = new EmotionContinuity(() => time);
        Assert.Equal(SpeechEmotion.Sad, engine.Resolve(
            new SpokenLine("I'm sorry.", "Üzgünüm.", "Lucia"), DubbingEmotionMode.Acoustic, false));
        Assert.Null(engine.Resolve(
            new SpokenLine("Please!", "Lütfen!", "Lucia"), DubbingEmotionMode.Acoustic, false));
        Assert.Equal(SpeechEmotion.Sad, engine.Resolve(
            new SpokenLine("Please!", "Lütfen!", "Lucia"), DubbingEmotionMode.Acoustic, true));
    }

    [Fact]
    public void MixedAudioCannotAssignAngerToNeutralText()
    {
        var engine = new EmotionContinuity();
        Assert.Null(engine.Resolve(
            new SpokenLine("A loud explosion.", "Büyük bir patlama."), DubbingEmotionMode.Acoustic, true));
    }
}
