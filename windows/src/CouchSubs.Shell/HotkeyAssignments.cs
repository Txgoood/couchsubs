// SPDX-License-Identifier: AGPL-3.0-only
namespace CouchSubs.Shell;

/// <summary>
/// Seven customizable global hotkeys, persisted in UserSettings.
/// F1..F12 and NumPad0..9 are accepted; every action must have a unique key.
/// No existing user setting is reset when this feature is installed.
/// </summary>
public sealed record HotkeyAssignments(
    int Screen, int FreeArea, int Pause, int Extra1, int Extra2, int Extra3, int Snapshot)
{
    public static HotkeyAssignments From(UserSettings s) => new(
        s.ScreenHotkey, s.FreeAreaHotkey, s.PauseHotkey,
        s.ExtraRegion1Hotkey, s.ExtraRegion2Hotkey, s.ExtraRegion3Hotkey, s.SnapshotHotkey);

    public static bool IsSupportedKey(int key) => key is >= 0x70 and <= 0x7B or >= 0x60 and <= 0x69;

    public bool IsValid =>
        AllKeys().All(IsSupportedKey) && AllKeys().Distinct().Count() == 7;

    public IReadOnlyList<int> AllKeys() =>
        [Screen, FreeArea, Pause, Extra1, Extra2, Extra3, Snapshot];

    public UserSettings ApplyTo(UserSettings settings) => settings with
    {
        ScreenHotkey = Screen,
        FreeAreaHotkey = FreeArea,
        PauseHotkey = Pause,
        ExtraRegion1Hotkey = Extra1,
        ExtraRegion2Hotkey = Extra2,
        ExtraRegion3Hotkey = Extra3,
        SnapshotHotkey = Snapshot
    };
}
