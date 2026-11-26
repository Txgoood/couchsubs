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
    public void GtaLeadsKeepDistinctReservedVoicesAndNpcsStayInTheirGroups()
    {
        var cast = new VoiceCast { ReserveGtaLeadVoices = true };
        var andrew = cast.VoiceFor(new SpokenLine("", "", "Andrew"), out _);
        var firstWoman = cast.VoiceFor(new SpokenLine("", "", "Woman 1"), out _);
        var secondWoman = cast.VoiceFor(new SpokenLine("", "", "Woman 2"), out _);
        Assert.Equal(DubbingVoice.Female1, cast.VoiceFor(
            new SpokenLine("Come on!", "Hadi!", "Lucia Caminos"), out _));
        Assert.Equal(DubbingVoice.Male1, cast.VoiceFor(
            new SpokenLine("Wait!", "Bekle!", "Jason"), out _));
        Assert.Equal(VoiceGender.Male, andrew.Gender());
        Assert.NotEqual(DubbingVoice.Male1, andrew.Reference());
        Assert.Equal(VoiceGender.Female, firstWoman.Gender());
        Assert.NotEqual(firstWoman, secondWoman);
        Assert.NotEqual(DubbingVoice.Female1, firstWoman.Reference());
    }

    [Fact]
    public void ExistingChoiceSurvivesUntilReassignedByUser()
    {
        var cast = new VoiceCast { ReserveGtaLeadVoices = true };
        cast.Assign(DubbingVoice.Male1, "Andrew");
        var npc = new SpokenLine("", "", "Andrew");
        Assert.Equal(DubbingVoice.Male1, cast.VoiceFor(npc, out _));
        cast.ReassignAutomatically("Andrew");
        Assert.NotEqual(DubbingVoice.Male1, cast.VoiceFor(npc, out _).Reference());
    }
}
