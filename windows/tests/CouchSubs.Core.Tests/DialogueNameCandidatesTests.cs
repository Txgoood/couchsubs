// SPDX-License-Identifier: AGPL-3.0-only
using CouchSubs.Core.Dubbing;
using Xunit;

namespace CouchSubs.Core.Tests;

public sealed class DialogueNameCandidatesTests
{
    [Fact]
    public void AddressingLuciaDoesNotClaimLuciaIsSpeaking()
    {
        Assert.Null(DialogueNameCandidates.ExplicitSpeaker("Hey Lucia, get in the car!"));
        Assert.Contains("Lucia", DialogueNameCandidates.Mentioned("Hey Lucia, get in the car!"));
    }

    [Fact]
    public void ExplicitSpeakerPrefixCanBeNamed()
    {
        Assert.Equal("LUCIA", DialogueNameCandidates.ExplicitSpeaker("LUCIA: Get in!"));
        Assert.Null(DialogueNameCandidates.ExplicitSpeaker("The police: are coming"));
    }

    [Fact]
    public void NoiseAndCommonWordsAreNotSpeakers()
    {
        Assert.Empty(DialogueNameCandidates.Mentioned("Hey, you! Look out, police!"));
        Assert.Null(DialogueNameCandidates.ExplicitSpeaker("LOOK: at the road"));
    }

    [Fact]
    public void CandidateNameDoesNotChangeVoiceAssignmentsUntilUserAccepts()
    {
        var cast = new VoiceCast();
        Assert.True(cast.SuggestName("Lucia"));
        Assert.Empty(cast.Assignments);
        Assert.Contains("LUCIA", cast.SuggestedNames.Keys);
        cast.AcceptSuggestion("Lucia");
        Assert.True(cast.Assignments.ContainsKey("LUCIA"));
        Assert.DoesNotContain("LUCIA", cast.SuggestedNames.Keys);
    }

    [Fact]
    public void RenamingPreservesTurkishVoiceAndGroupWithoutMergingCharacters()
    {
        var cast = new VoiceCast();
        cast.Assign(DubbingVoice.Female2, "Ses 2");
        cast.SetGroup("Ses 2", CharacterGroup.Female);
        cast.Rename("Ses 2", "Lucia");
        Assert.Equal(DubbingVoice.Female2, cast.Assignments["LUCIA"]);
        Assert.Equal(CharacterGroup.Female, cast.Groups["LUCIA"]);
        Assert.False(cast.Assignments.ContainsKey("SES 2"));

        cast.Assign(DubbingVoice.Male2, "Jason");
        Assert.Throws<InvalidOperationException>(() => cast.Rename("Lucia", "Jason"));
        Assert.Equal(DubbingVoice.Female2, cast.Assignments["LUCIA"]);
    }

    [Fact]
    public void CandidateMetadataSurvivesCopyAndIsIndependentPerGame()
    {
        var cast = new VoiceCast();
        cast.SuggestName("Lucia");
        var other = cast.Copy();
        other.DismissSuggestion("Lucia");
        Assert.Contains("LUCIA", cast.SuggestedNames.Keys);
        Assert.Empty(other.SuggestedNames);
    }
}
