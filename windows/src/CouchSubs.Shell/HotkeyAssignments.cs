// SPDX-License-Identifier: AGPL-3.0-only
namespace CouchSubs.Shell;

/// <summary>
/// Seven customizable global hotkeys, persisted in UserSettings.
/// F1..F11 and NumPad0..9 are accepted; Windows reserves F12.
/// </summary>
public sealed record HotkeyAssignments(
    int Screen, int FreeArea, int Pause, int Extra1, int Extra2, int Extra3, int Snapshot)
{
    public static HotkeyAssignments From(UserSettings s)
    {
        int[] keys = [s.ScreenHotkey, s.FreeAreaHotkey, s.PauseHotkey,
            s.ExtraRegion1Hotkey, s.ExtraRegion2Hotkey, s.ExtraRegion3Hotkey, s.SnapshotHotkey];
        var used = keys.Where(IsSupportedKey).ToHashSet();
        for (var i = 0; i < keys.Length; i++)
            if (keys[i] == 0x7B)
            {
                keys[i] = Enumerable.Range(0x70, 10).Reverse().First(key => !used.Contains(key));
                used.Add(keys[i]);
            }
        return new(keys[0], keys[1], keys[2], keys[3], keys[4], keys[5], keys[6]);
    }

    public static bool IsSupportedKey(int key) => key is >= 0x70 and <= 0x7A or >= 0x60 and <= 0x69;

    public static string KeyLabel(int key) => key >= 0x70 ? $"F{key - 0x6F}" : $"Num {key - 0x60}";

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
