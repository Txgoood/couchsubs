// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;
using CouchSubs.Shell;
using Xunit;

namespace CouchSubs.Shell.Tests;

public sealed class AllShortcutSettingsTests
{
    [Fact]
    public void SevenDefaultShortcutsStayDistinctAcrossUpgrades()
    {
        var mapping = HotkeyAssignments.From(new UserSettings());
        Assert.True(mapping.IsValid);
        Assert.Equal(new[] { 0x7A, 0x79, 0x60, 0x61, 0x62, 0x63, 0x68 }, mapping.AllKeys());
    }

    [Fact]
    public void EveryActionCanBeRemappedAndSaved()
    {
        var mapping = new HotkeyAssignments(0x70, 0x71, 0x72, 0x73, 0x74, 0x75, 0x76);
        Assert.True(mapping.IsValid);
        var saved = mapping.ApplyTo(new UserSettings { AutoWorldText = true });
        var reloaded = JsonSerializer.Deserialize<UserSettings>(JsonSerializer.Serialize(saved))!;
        Assert.Equal(mapping, HotkeyAssignments.From(reloaded));
        Assert.True(reloaded.AutoWorldText);
    }

    [Fact]
    public void DuplicateOrUnsupportedKeysAreRejected()
    {
        Assert.False(new HotkeyAssignments(0x70, 0x70, 0x72, 0x73, 0x74, 0x75, 0x76).IsValid);
        Assert.False(new HotkeyAssignments(0x70, 0x71, 0x72, 0x73, 0x74, 0x75, 0x41).IsValid);
        Assert.True(HotkeyAssignments.IsSupportedKey(0x60));
        Assert.True(HotkeyAssignments.IsSupportedKey(0x69));
        Assert.False(HotkeyAssignments.IsSupportedKey(0x7B));
        Assert.False(HotkeyAssignments.IsSupportedKey(0x41));
    }

    [Fact]
    public void OldJsonReplacesReservedF12WithoutChangingOtherKeys()
    {
        var settings = JsonSerializer.Deserialize<UserSettings>(
            "{\"ScreenHotkey\":122,\"FreeAreaHotkey\":123,\"AutoWorldText\":true}")!;
        Assert.True(settings.AutoWorldText);
        var mapping = HotkeyAssignments.From(settings);
        Assert.True(mapping.IsValid);
        Assert.Equal(0x79, mapping.FreeArea);
        Assert.Equal(0x60, settings.PauseHotkey);
        Assert.Equal(0x61, settings.ExtraRegion1Hotkey);
        Assert.Equal(0x62, settings.ExtraRegion2Hotkey);
        Assert.Equal(0x63, settings.ExtraRegion3Hotkey);
        Assert.Equal(0x68, settings.SnapshotHotkey);
    }

    [Fact]
    public void ReservedF12MigratesToAnUnassignedFunctionKey()
    {
        var mapping = HotkeyAssignments.From(new UserSettings { FreeAreaHotkey = 0x7B, ScreenHotkey = 0x79 });
        Assert.Equal(0x79, mapping.Screen);
        Assert.Equal(0x78, mapping.FreeArea);
        Assert.True(mapping.IsValid);
    }
}
