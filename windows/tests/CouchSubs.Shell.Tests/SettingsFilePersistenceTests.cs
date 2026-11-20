// SPDX-License-Identifier: AGPL-3.0-only
using System.IO;
using CouchSubs.Shell;
using Xunit;

namespace CouchSubs.Shell.Tests;

public sealed class SettingsFilePersistenceTests
{
    [Fact]
    public void AtomicSavePreservesGameCastsAndRecoversPreviousVersionOnCorruption()
    {
        var folder = Path.Combine(Path.GetTempPath(), "CouchSubs-settings-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(folder, "settings.json");
        try
        {
            var settings = new UserSettings
            {
                ScreenHotkey = 0x74,
                Dubbing = new DubbingSettings { ActiveGame = "GTA 6" }
            };
            SettingsFilePersistence.Save(path, settings);
            Assert.Equal("GTA 6", SettingsFilePersistence.Load(path).Dubbing.ActiveGame);

            var updated = settings with { ScreenHotkey = 0x7A };
            SettingsFilePersistence.Save(path, updated);
            Assert.True(File.Exists(path + ".bak"));
            Assert.Equal(0x7A, SettingsFilePersistence.Load(path).ScreenHotkey);

            // Simulate a partial/invalid JSON write from a power cut.
            File.WriteAllText(path, "{");
            var recovered = SettingsFilePersistence.Load(path);
            Assert.Equal(0x74, recovered.ScreenHotkey);
            Assert.Equal("GTA 6", recovered.Dubbing.ActiveGame);
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void CorruptPrimaryCanBeRepairedWithoutOverwritingHealthyBackup()
    {
        var folder = Path.Combine(Path.GetTempPath(), "CouchSubs-settings-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(folder, "settings.json");
        try
        {
            SettingsFilePersistence.Save(path, new UserSettings { ScreenHotkey = 0x70 });
            SettingsFilePersistence.Save(path, new UserSettings { ScreenHotkey = 0x71 });
            File.WriteAllText(path, "invalid-json");
            SettingsFilePersistence.Save(path, new UserSettings { ScreenHotkey = 0x72 });

            Assert.Equal(0x72, SettingsFilePersistence.Load(path).ScreenHotkey);
            File.WriteAllText(path, "invalid-again");
            Assert.Equal(0x70, SettingsFilePersistence.Load(path).ScreenHotkey);
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }
}
