// SPDX-License-Identifier: AGPL-3.0-only

using CouchSubs.Core;
using CouchSubs.Core.LgTv;
using CouchSubs.RemotePlay;

namespace CouchSubs.Shell.Tests;

public sealed class TranslationMemoryTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), $"couchsubs-memory-{Guid.NewGuid():N}");
    private string FilePath => Path.Combine(folder, "memory.json");
    private static readonly TranslationMemoryScope Gta = new("GTA VI", "en", "tr");

    [Fact]
    public async Task SavedCorrectionsSurviveOpeningTheFileAgainAndStayScoped()
    {
        var memory = new FileTranslationMemory(FilePath);
        await Task.WhenAll(memory.SaveAsync(new(Gta, "Hold here.\r\nRun!", "Burada bekle.\nKoş!")),
            memory.SaveAsync(new(Gta, "Back", "Geri dön")));
        var reopened = new FileTranslationMemory(FilePath);
        Assert.Equal(2, reopened.Saved(Gta).Count);
        Assert.True(reopened.TryGet(Gta with { Game = "gta vi" }, "Hold here.\nRun!", out var text));
        Assert.Equal("Burada bekle.\nKoş!", text);
        Assert.False(reopened.TryGet(Gta with { Game = "Other game" }, "Back", out _));
        Assert.False(reopened.TryGet(Gta with { TargetLanguage = "es" }, "Back", out _));
        Assert.False(reopened.TryGet(Gta, "back", out _));

        await reopened.RemoveAsync(Gta, "Back");
        Assert.False(new FileTranslationMemory(FilePath).TryGet(Gta, "Back", out _));
    }

    [Fact]
    public async Task BrokenFileDoesNotBlockTranslationOrOverwriteStoredData()
    {
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(FilePath, "{broken");
        var memory = new FileTranslationMemory(FilePath);
        Assert.False(memory.TryGet(Gta, "Back", out _));
        await Assert.ThrowsAsync<System.Text.Json.JsonException>(() => memory.SaveAsync(new(Gta, "Back", "Geri")));
        Assert.Equal("{broken", await File.ReadAllTextAsync(FilePath));
    }

    [Fact]
    public async Task ScreenHistoryCanOverrideAnExistingMenuLabelWithoutAnyTranslationModel()
    {
        var services = new FakeServices { TranslationMemory = new FileTranslationMemory(FilePath) };
        services.Translation.Installed = false;
        services.ReaderLines = new Reading([new TextLine("Back", new TextBox(.05, .05, .22, .12))]);
        using var controller = MakePaired(services);
        await controller.StartAsync();
        services.Streams[0].Emit(Frame.Blank(64, 64));
        Assert.Equal(ScreenOutcome.Shown, await controller.TranslateScreenAsync());
        var entry = Assert.Single(controller.RecentTranslations(Gta));
        Assert.Equal("Geri", entry.Translation);
        Assert.Empty(controller.SavedTranslations(Gta));

        await controller.SaveTranslationAsync(entry with { Translation = "Geri dön" });
        Assert.Equal(ScreenOutcome.Cleared, await controller.TranslateScreenAsync());
        Assert.Equal(ScreenOutcome.Shown, await controller.TranslateScreenAsync());
        Assert.Equal("Geri dön", Assert.Single(controller.RecentTranslations(Gta)).Translation);
    }

    [Fact]
    public async Task SubtitleUsesSavedCorrectionWithNoModelInstalled()
    {
        var memory = new FileTranslationMemory(FilePath);
        await memory.SaveAsync(new(Gta, "Stay here.", "Burada kal."));
        var services = new FakeServices { TranslationMemory = memory };
        services.Translation.Installed = false;
        services.ReaderLines = new Reading([new TextLine("Stay here.", new TextBox(.25, .5, .75, .7))]);
        using var controller = MakePaired(services);
        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        controller.InspectionSubtitle += (_, translation) => received.TrySetResult(translation);
        await controller.StartAsync();
        services.Streams[0].Emit(Frame.Blank(64, 64));

        Assert.Equal("Burada kal.", await received.Task.WaitAsync(TimeSpan.FromSeconds(3)));
    }

    private static AppController MakePaired(FakeServices services)
    {
        var store = new MemoryStore();
        store.Save(StoreKeys.AccountId, new byte[8]);
        store.Save(StoreKeys.Console, new RegisteredConsole("192.168.1.253", "PS5", new byte[16], new byte[16]));
        store.Save(StoreKeys.Tv, new LgTv("LG TV", "127.0.0.1"));
        var settings = new MemorySettings();
        settings.Save(new UserSettings { Dubbing = new() { ActiveGame = Gta.Game, AutoGameLibrary = false } });
        return AppControllerTests.Make(services, store, settings);
    }

    public void Dispose()
    {
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }
}
