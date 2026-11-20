// SPDX-License-Identifier: AGPL-3.0-only
using CouchSubs.Core.Dubbing;
using CouchSubs.Shell;
using Xunit;

namespace CouchSubs.Shell.Tests;

public sealed class GameCastLibraryBackupTests
{
    [Fact]
    public void ExportDeleteImportRoundTripRestoresCharacterVoices()
    {
        var settings = GameCastLibrary.Switch(new DubbingSettings(), "GTA 6");
        var cast = settings.Cast.Copy();
        cast.Assign(DubbingVoice.Female2, "Lucia");
        cast.SetGroup("Lucia", CharacterGroup.Female);
        cast.Assign(DubbingVoice.Male3, "Jason");
        settings = GameCastLibrary.SaveCurrent(settings with { Cast = cast });
        var json = GameCastLibrary.ExportJson(settings, "GTA 6");
        Assert.Contains("\"GTA 6\"", json);

        var deleted = GameCastLibrary.Delete(settings, "GTA 6");
        Assert.Equal("Genel", deleted.ActiveGame);
        Assert.DoesNotContain("GTA 6", GameCastLibrary.Games(deleted));

        var restored = GameCastLibrary.ImportJson(deleted, json, overwrite: false);
        Assert.Contains("GTA 6", GameCastLibrary.Games(restored));
        var selected = GameCastLibrary.Switch(restored, "GTA 6");
        Assert.Equal(DubbingVoice.Female2, selected.Cast.Assignments["LUCIA"]);
        Assert.Equal(DubbingVoice.Male3, selected.Cast.Assignments["JASON"]);
        Assert.Equal(CharacterGroup.Female, selected.Cast.GroupFor("Lucia"));
    }

    [Fact]
    public void DeletingOneGameDoesNotDeleteAnother()
    {
        var settings = GameCastLibrary.Switch(new DubbingSettings(), "GTA 6");
        var lucia = settings.Cast.Copy();
        lucia.Assign(DubbingVoice.Female1, "Lucia");
        settings = settings with { Cast = lucia };
        settings = GameCastLibrary.Switch(settings, "GTA 5");
        var michael = settings.Cast.Copy();
        michael.Assign(DubbingVoice.Male3, "Michael");
        settings = settings with { Cast = michael };
        settings = GameCastLibrary.Delete(settings, "GTA 6");
        Assert.Equal("GTA 5", settings.ActiveGame);
        Assert.Equal(DubbingVoice.Male3, settings.Cast.Assignments["MICHAEL"]);
        Assert.DoesNotContain("GTA 6", GameCastLibrary.Games(settings));
    }

    [Fact]
    public void ExistingBackupNeverOverwritesWithoutConfirmation()
    {
        var settings = GameCastLibrary.Switch(new DubbingSettings(), "GTA 6");
        var json = GameCastLibrary.ExportJson(settings, "GTA 6");
        Assert.Throws<InvalidOperationException>(() =>
            GameCastLibrary.ImportJson(settings, json, overwrite: false));
        Assert.NotNull(GameCastLibrary.ImportJson(settings, json, overwrite: true));
    }

    [Fact]
    public void InvalidDataIsRejectedAndGeneralCannotBeDeleted()
    {
        Assert.Throws<InvalidDataException>(() =>
            GameCastLibrary.ImportJson(new DubbingSettings(), "{bad json", false));
        Assert.Throws<InvalidOperationException>(() =>
            GameCastLibrary.Delete(new DubbingSettings(), "Genel"));
    }

    [Theory]
    [InlineData("GTA VI", "GTA 6")]
    [InlineData("Grand Theft Auto VI", "GTA 6")]
    [InlineData("Grand Theft Auto V", "GTA 5")]
    [InlineData("GTA 5", "GTA 5")]
    [InlineData("We should play GTA 6 tomorrow.", null)]
    [InlineData("Lucia: Let's go!", null)]
    [InlineData("Grand Theft Auto VII", null)]
    public void TitleDetectionDoesNotGuessFromDialogue(string seen, string? expected)
    {
        Assert.Equal(expected, GameTitleDetector.Detect(seen));
    }
}
