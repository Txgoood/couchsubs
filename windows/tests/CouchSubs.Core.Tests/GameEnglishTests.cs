// SPDX-License-Identifier: AGPL-3.0-only

using CouchSubs.Core;

namespace CouchSubs.Core.Tests;

/// <summary>Oyun ifadeleri çeviriden önce düz İngilizceye çevrilir (Mac: GameEnglishTests).</summary>
public class GameEnglishTests
{
    [Theory]
    [InlineData("Could you do me a solid and pick up the car?", "Could you do me a favor and pick up the car?")]
    [InlineData("We need him alive.", "We must keep him alive.")]
    [InlineData("Do not let the money go to your head.", "Do not let the money make you arrogant.")]
    public void ColloquialDialogueKeepsItsMeaning(string source, string plain) =>
        Assert.Equal(plain, GameEnglish.Plain(source));

    [Theory]
    [InlineData("Hold your fire! They're friendlies!", "Hold your fire! They're allies!")]
    [InlineData("I got your six.", "I've got your back.")]
    [InlineData("Check your six!", "Watch behind you!")]
    [InlineData("Heads up! Incoming!", "Watch out! Incoming!")]
    [InlineData("Took you long enough.", "You're late.")]
    [InlineData("Careful, we've got company.", "Careful, enemies are coming.")]
    [InlineData("We're sitting ducks out here.", "We're easy targets out here.")]
    [InlineData("Copy that. Moving in.", "Copy that. Going in.")]
    public void GameIdiomsBecomePlainEnglish(string source, string plain) => Assert.Equal(plain, GameEnglish.Plain(source));

    [Theory]
    [InlineData("The friendliest town in the north.")]
    [InlineData("Six of them, behind the gate.")]
    [InlineData("Stand down!")]
    public void OtherTextIsUnchanged(string source) => Assert.Equal(source, GameEnglish.Plain(source));
}
