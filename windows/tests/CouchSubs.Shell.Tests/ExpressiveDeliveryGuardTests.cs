// SPDX-License-Identifier: AGPL-3.0-only
using CouchSubs.Core.Dubbing;
using CouchSubs.Shell;

namespace CouchSubs.Shell.Tests;

public sealed class ExpressiveDeliveryGuardTests
{
    [Fact]
    public void StrongEnglishEmotionCueKeepsTheExistingCharacterVoiceAndTag()
    {
        var guard = new ExpressiveDeliveryGuard();
        var line = new SpokenLine("Get back!", "Geri çekil!", "Jason");
        Assert.Equal(SpeechEmotion.Angry,
            guard.Refine(line, SpeechEmotion.Angry, DubbingEmotionMode.Balanced, 1000, false));
    }

    [Fact]
    public void ExclamationAloneDoesNotImplyAngerOrSurprise()
    {
        var guard = new ExpressiveDeliveryGuard();
        var line = new SpokenLine("The door!", "Kapı!", "Lucia");
        Assert.Null(guard.Refine(line, SpeechEmotion.Angry, DubbingEmotionMode.Cinematic, 1000, true));
        Assert.Equal(1, guard.SuppressedAmbiguous);
    }

    [Fact]
    public void OnlySameSpeakerCanCarryEmotionInConservativeWindow()
    {
        var guard = new ExpressiveDeliveryGuard();
        guard.Refine(new SpokenLine("I'm sorry.", "Üzgünüm.", "Lucia"),
            SpeechEmotion.Sad, DubbingEmotionMode.Cinematic, 1000, false);
        Assert.Equal(SpeechEmotion.Sad,
            guard.Refine(new SpokenLine("Please!", "Lütfen!", "Lucia"),
                SpeechEmotion.Sad, DubbingEmotionMode.Cinematic, 1500, false));
        Assert.Null(guard.Refine(new SpokenLine("Please!", "Lütfen!", "Jason"),
            SpeechEmotion.Sad, DubbingEmotionMode.Cinematic, 1500, false));
        Assert.Null(guard.Refine(new SpokenLine("Please!", "Lütfen!", "Lucia"),
            SpeechEmotion.Sad, DubbingEmotionMode.Cinematic, 6500, false));
    }

    [Fact]
    public void AcousticModeNeverTreatsMusicAsProofOfEmotionalSpeech()
    {
        var guard = new ExpressiveDeliveryGuard();
        guard.Refine(new SpokenLine("I'm sorry.", "Üzgünüm.", "Lucia"),
            SpeechEmotion.Sad, DubbingEmotionMode.Acoustic, 1000, false);
        Assert.Null(guard.Refine(new SpokenLine("Please!", "Lütfen!", "Lucia"),
            SpeechEmotion.Sad, DubbingEmotionMode.Acoustic, 1500, false));
        Assert.Equal(SpeechEmotion.Sad,
            guard.Refine(new SpokenLine("Please!", "Lütfen!", "Lucia"),
                SpeechEmotion.Sad, DubbingEmotionMode.Acoustic, 1600, true));
    }

    [Fact]
    public void ResetAndDisabledModeClearExpressiveOutput()
    {
        var guard = new ExpressiveDeliveryGuard();
        Assert.Null(guard.Refine(new SpokenLine("Get back!", "Geri çekil!"),
            SpeechEmotion.Angry, DubbingEmotionMode.Off, 1000, true));
        guard.Reset();
        Assert.Equal(0, guard.SuppressedAmbiguous);
    }
}
