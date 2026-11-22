// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;
using CouchSubs.Shell;
using Xunit;

namespace CouchSubs.Shell.Tests;

public sealed class ShortcutSettingsTests
{
    [Fact]
    public void DefaultShortcutsAreF11AndF10()
    {
        var settings = new UserSettings();
        Assert.Equal(0x7A, settings.ScreenHotkey);
        Assert.Equal(0x79, settings.FreeAreaHotkey);
    }

    [Fact]
    public void ShortcutsSurviveSettingsSerialization()
    {
        var settings = new UserSettings
        {
            ScreenHotkey = 0x69,
            FreeAreaHotkey = 0x65,
        };
        var restored = JsonSerializer.Deserialize<UserSettings>(JsonSerializer.Serialize(settings));
        Assert.NotNull(restored);
        Assert.Equal(0x69, restored.ScreenHotkey);
        Assert.Equal(0x65, restored.FreeAreaHotkey);
    }

    [Fact]
    public void LegacySettingsPreserveOtherFeaturesAndGetNewDefaults()
    {
        var restored = JsonSerializer.Deserialize<UserSettings>("{\"AutoWorldText\":true}");
        Assert.NotNull(restored);
        Assert.True(restored.AutoWorldText);
        Assert.Equal(0x7A, restored.ScreenHotkey);
        Assert.Equal(0x79, restored.FreeAreaHotkey);
    }
}
