// SPDX-License-Identifier: AGPL-3.0-only
using CouchSubs.Core.Dubbing;
using Xunit;

namespace CouchSubs.Core.Tests;

public sealed class AcousticVoiceAssignmentTests
{
    [Fact]
    public void AutomaticSpeakersReceiveDistinctPersistentVoices()
    {
        var cast = new VoiceCast();
        var voices = new List<DubbingVoice>();
        for (var i = 1; i <= 6; i++)
        {
            var line = new SpokenLine("Hello!", "Merhaba!", "Ses " + i);
            var voice = cast.VoiceFor(line, out var assigned);
            Assert.True(assigned);
            voices.Add(voice);
            Assert.Equal(voice, cast.VoiceFor(line, out assigned));
            Assert.False(assigned);
        }
        Assert.Equal(6, voices.Distinct().Count());
        Assert.Equal(6, cast.Assignments.Count);
    }

    [Fact]
    public void NamedCharactersContinueUsingKnownGenderVoice()
    {
        var cast = new VoiceCast();
        Assert.Equal(DubbingVoice.Female1, cast.VoiceFor(
            new SpokenLine("Come on!", "Hadi!", "Lucia"), out _));
        Assert.Equal(DubbingVoice.Male1, cast.VoiceFor(
            new SpokenLine("Wait!", "Bekle!", "Jason"), out _));
    }
}
