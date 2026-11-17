// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;
using CouchSubs.Core.Dubbing;
using CouchSubs.Shell;
using Xunit;

namespace CouchSubs.Shell.Tests;

public sealed class EmotionContinuityTests
{
    [Fact]
    public void EmotionIsDisabledByDefaultToPreservePreviousAudio()
    {
        var settings = new DubbingSettings();
        Assert.Equal(DubbingEmotionMode.Off, settings.EmotionMode);
        Assert.Null(new EmotionContinuity().Resolve(new SpokenLine("Get back!", "Geri çekil!", "Jason"), settings.EmotionMode));
    }

    [Fact]
    public void BalancedUsesExistingEnglishTextClassifier()
    {
        var engine = new EmotionContinuity();
        Assert.Equal(SpeechEmotion.Angry, engine.Resolve(new SpokenLine("Get back!", "Çekil!", "Jason"), DubbingEmotionMode.Balanced));
        Assert.Null(engine.Resolve(new SpokenLine("We need to go.", "Gitmeliyiz.", "Jason"), DubbingEmotionMode.Balanced));
    }

    [Fact]
    public void CinematicKeepsEmotionOnEmphaticLineForSameSpeakerOnly()
    {
        var clock = new DateTimeOffset(2026, 11, 18, 0, 0, 0, TimeSpan.Zero);
        var engine = new EmotionContinuity(() => clock);
        Assert.Equal(SpeechEmotion.Sad, engine.Resolve(new SpokenLine("I'm sorry.", "Üzgünüm.", "Lucia"), DubbingEmotionMode.Cinematic));
        Assert.Equal(SpeechEmotion.Sad, engine.Resolve(new SpokenLine("Please!", "Lütfen!", "Lucia"), DubbingEmotionMode.Cinematic));
        Assert.Equal(SpeechEmotion.Angry, engine.Resolve(new SpokenLine("Move!", "Çekil!", "Jason"), DubbingEmotionMode.Cinematic));
        Assert.Null(engine.Resolve(new SpokenLine("We need to go.", "Gitmeliyiz.", "Lucia"), DubbingEmotionMode.Cinematic));
        clock = clock.AddSeconds(7);
        Assert.Equal(SpeechEmotion.Angry, engine.Resolve(new SpokenLine("Now!", "Şimdi!", "Lucia"), DubbingEmotionMode.Cinematic));
    }

    [Fact]
    public void ExistingSettingsJsonWithoutEmotionModeStillLoads()
    {
        var settings = JsonSerializer.Deserialize<DubbingSettings>("{\"Enabled\":true}");
        Assert.NotNull(settings);
        Assert.Equal(DubbingEmotionMode.Off, settings!.EmotionMode);
        Assert.True(settings.Enabled);
    }
}
