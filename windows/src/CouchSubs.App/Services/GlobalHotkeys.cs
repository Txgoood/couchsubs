// SPDX-License-Identifier: AGPL-3.0-only

using System.Runtime.InteropServices;
using System.Windows.Interop;
using CouchSubs.Shell;

namespace CouchSubs.App.Services;

/// <summary>Genel kısayolların yaptığı iş.</summary>
public enum HotkeyAction
{
    /// <summary>Num 1: 1. ek bölgeyi bir kez çevir.</summary>
    ExtraRegion1,
    /// <summary>Num 2: 2. ek bölgeyi bir kez çevir.</summary>
    ExtraRegion2,
    /// <summary>Num 3: 3. ek bölgeyi bir kez çevir.</summary>
    ExtraRegion3,
    /// <summary>F12 (configurable): freeze screen and choose free area.</summary>
    FreeArea,
    /// <summary>Num 8: ayar ekranındaki görüntüyü oyunun şu anki görüntüsüyle yenile.</summary>
    Snapshot,
    /// <summary>Num 0: canlı altyazı çevirisini duraklat / sürdür.</summary>
    PauseSubtitles,
    /// <summary>F11 (configurable): one-shot full screen translation/toggle.</summary>
    Screen,
}

/// <summary>
/// Genel kısayollar (default Num 0, 1, 2, 3, 8 and F11/F12): kullanıcı kumandayla oynarken bilgisayardaki pencere arka planda olsa da çalışır.
/// Kısayollar yalnız <see cref="Enabled"/> iken tutulur; böylece çeviri kapalıyken sayı tuşları diğer uygulamalarda
/// (Excel gibi) normal çalışır. Tuş başka bir uygulamada kullanılıyorsa sessizce atlanır.
/// </summary>
public sealed partial class GlobalHotkeys : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const uint ModNoRepeat = 0x4000;
    private const int FirstId = 0x4353;

    /// <summary>Action order is fixed; assigned keys are independently configurable.</summary>
    private static readonly (HotkeyAction Action, uint VirtualKey)[] Keys =
    [
        (HotkeyAction.ExtraRegion1, 0x61),
        (HotkeyAction.ExtraRegion2, 0x62),
        (HotkeyAction.ExtraRegion3, 0x63),
        (HotkeyAction.FreeArea, 0x7B),
        (HotkeyAction.Snapshot, 0x68),
        (HotkeyAction.PauseSubtitles, 0x60),
        (HotkeyAction.Screen, 0x7A),
    ];

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint virtualKey);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKey(IntPtr window, int id);

    private readonly IntPtr window;
    private readonly HwndSource? source;
    private readonly Action<HotkeyAction> pressed;
    private readonly List<int> registered = [];
    private bool enabled;
    private readonly uint[] configuredKeys = Keys.Select(k => k.VirtualKey).ToArray();

    /// <summary>Apply seven non-conflicting shortcuts, and register immediately when running.</summary>
    public bool Configure(UserSettings settings)
    {
        var mapping = HotkeyAssignments.From(settings);
        if (!mapping.IsValid) return false;
        // Same order as Keys: region1, region2, region3, free, snapshot, pause, screen.
        uint[] updated =
        [
            (uint)mapping.Extra1, (uint)mapping.Extra2, (uint)mapping.Extra3,
            (uint)mapping.FreeArea, (uint)mapping.Snapshot, (uint)mapping.Pause,
            (uint)mapping.Screen
        ];
        if (configuredKeys.SequenceEqual(updated)) return true;
        bool wasEnabled = enabled;
        if (wasEnabled) Unregister();
        Array.Copy(updated, configuredKeys, updated.Length);
        if (wasEnabled) Register();
        return true;
    }

    /// <param name="window">Pencerenin tutamağı (HWND).</param>
    /// <param name="pressed">Kısayola basılınca çağrılır.</param>
    public GlobalHotkeys(IntPtr window, Action<HotkeyAction> pressed)
    {
        this.window = window;
        this.pressed = pressed;
        source = HwndSource.FromHwnd(window);
        source?.AddHook(Hook);
    }

    /// <summary>Kısayollar tutuluyor mu.</summary>
    public bool Enabled
    {
        get => enabled;
        set
        {
            if (value == enabled || source is null) return;
            enabled = value;
            if (value) Register();
            else Unregister();
        }
    }

    private void Register()
    {
        for (var index = 0; index < Keys.Length; index++)
        {
            var id = FirstId + index;
            if (RegisterHotKey(window, id, ModNoRepeat, configuredKeys[index])) registered.Add(id);
        }
    }

    private void Unregister()
    {
        foreach (var id in registered) UnregisterHotKey(window, id);
        registered.Clear();
    }

    private IntPtr Hook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != WmHotkey) return IntPtr.Zero;
        var id = wParam.ToInt32();
        if (!registered.Contains(id)) return IntPtr.Zero;
        handled = true;
        pressed(Keys[id - FirstId].Action);
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        source?.RemoveHook(Hook);
        Unregister();
        enabled = false;
    }
}
