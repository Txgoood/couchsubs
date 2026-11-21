// SPDX-License-Identifier: AGPL-3.0-only
using CouchSubs.Core.Dubbing;
using CouchSubs.Shell;

namespace CouchSubs.Shell.Tests;

public sealed class ExpressiveTurnPlannerTests
{
    [Fact]
    public void LexicalAngerAndSadnessKeepOwnCharacterContext()
    {
        var plan = new ExpressiveTurnPlanner();
        Assert.Equal(SpeechEmotion.Sad, plan.Resolve(
            new SpokenLine("I'm sorry.", "Üzgünüm.", "Lucia"),
            SpeechEmotion.Sad, DubbingEmotionMode.Cinematic, 1000, true));
        Assert.Equal(SpeechEmotion.Angry, plan.Resolve(
            new SpokenLine("Get back!", "Çekil!", "Jason"),
            SpeechEmotion.Angry, DubbingEmotionMode.Cinematic, 1100, true));
        Assert.Equal(SpeechEmotion.Sad, plan.Resolve(
            new SpokenLine("Please!", "Lütfen!", "Lucia"),
            SpeechEmotion.Sad, DubbingEmotionMode.Cinematic, 1500, true));
        Assert.Equal(1, plan.CarriedTurns);
    }

    [Fact]
    public void LoudGameSoundAndQuestionMarksDoNotCreateAnger()
    {
        var plan = new ExpressiveTurnPlanner();
        Assert.Null(plan.Resolve(new SpokenLine("The door!", "Kapı!", "Lucia"),
            SpeechEmotion.Angry, DubbingEmotionMode.Acoustic, 1000, false));
        Assert.Null(plan.Resolve(new SpokenLine("The door?", "Kapı?", "Lucia"),
            SpeechEmotion.Surprised, DubbingEmotionMode.Cinematic, 1200, true));
    }

    [Fact]
    public void NoCarryAcrossNewAnonymousTurnOrLongGap()
    {
        var plan = new ExpressiveTurnPlanner();
        plan.Resolve(new SpokenLine("I'm sorry.", "Üzgünüm.", "Lucia"),
            SpeechEmotion.Sad, DubbingEmotionMode.Cinematic, 1000, true);
        Assert.Null(plan.Resolve(new SpokenLine("Please!", "Lütfen!", "Lucia", NewTurn: true),
            SpeechEmotion.Sad, DubbingEmotionMode.Cinematic, 1500, true));
        Assert.Null(plan.Resolve(new SpokenLine("Please!", "Lütfen!", "Jason"),
            SpeechEmotion.Sad, DubbingEmotionMode.Cinematic, 1600, true));
        Assert.Null(plan.Resolve(new SpokenLine("Please!", "Lütfen!", "Lucia"),
            SpeechEmotion.Sad, DubbingEmotionMode.Cinematic, 3500, true));
    }

    [Fact]
    public void ResetAndOffModeRestoreNeutralDelivery()
    {
        var plan = new ExpressiveTurnPlanner();
        plan.Resolve(new SpokenLine("I'm sorry.", "Üzgünüm.", "Lucia"),
            SpeechEmotion.Sad, DubbingEmotionMode.Cinematic, 1000, true);
        plan.Reset();
        Assert.Null(plan.Resolve(new SpokenLine("Please!", "Lütfen!", "Lucia"),
            SpeechEmotion.Sad, DubbingEmotionMode.Cinematic, 1050, true));
        Assert.Null(plan.Resolve(new SpokenLine("Get back!", "Çekil!", "Jason"),
            SpeechEmotion.Angry, DubbingEmotionMode.Off, 2000, true));
        Assert.Equal(0, plan.CarriedTurns);
    }
}
