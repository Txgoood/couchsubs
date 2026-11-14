// SPDX-License-Identifier: AGPL-3.0-only

using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using CouchSubs.Core;
using CouchSubs.Shell;

namespace CouchSubs.App.Services;

/// <summary>
/// Win32 EnumDisplayMonitors üzerinden bağlı ekranları tespit eder. Windows Forms referansı yok:
/// WPF arayüzüne UserControl / Application isim çakışması getirmez.
/// </summary>
internal sealed class WindowsGameScreenCapture : IGameScreenCapture
{
    private const uint PrimaryFlag = 1;
    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string Device;
    }

    private delegate bool EnumMonitor(nint hMonitor, nint hdc, nint bounds, nint data);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(nint hdc, nint rect, EnumMonitor callback, nint data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

    private sealed record FoundDisplay(string Id, Rectangle Bounds, bool Primary);

    private static List<FoundDisplay> Enumerate()
    {
        var screens = new List<FoundDisplay>();
        EnumMonitor callback = (monitor, _, _, _) =>
        {
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>(), Device = "" };
            if (!GetMonitorInfo(monitor, ref info)) return true;
            var rect = info.Monitor;
            var width = rect.Right - rect.Left;
            var height = rect.Bottom - rect.Top;
            if (width > 0 && height > 0)
                screens.Add(new FoundDisplay(info.Device,
                    new Rectangle(rect.Left, rect.Top, width, height), (info.Flags & PrimaryFlag) != 0));
            return true;
        };
        if (!EnumDisplayMonitors(0, 0, callback, 0) || screens.Count == 0)
            throw new InvalidOperationException("Windows ekranları tespit edilemedi.");
        return screens;
    }

    internal static Rectangle BoundsFor(string id)
    {
        var screens = Enumerate();
        var screen = screens.FirstOrDefault(s => s.Id == id)
            ?? screens.FirstOrDefault(s => s.Primary) ?? screens[0];
        return screen.Bounds;
    }

    public IReadOnlyList<GameDisplay> Displays() =>
        Enumerate().Select((screen, index) => new GameDisplay(
            screen.Id,
            $"{(screen.Primary ? "Birincil ekran" : "Bağlı monitör")} {index + 1} — {screen.Bounds.Width}×{screen.Bounds.Height} ({screen.Id})",
            screen.Primary)).ToArray();

    public Frame Capture(string displayId)
    {
        var bounds = BoundsFor(displayId);
        // GDI ekran alımı; oyunlar kenarlıksız tam ekranda yakalanabilir.
        // Exclusive tam ekran/korumalı görüntüde ekran siyah çıkabilir.
        using var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
            graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bitmap.Size, CopyPixelOperation.SourceCopy);
        var locked = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try { return Frame.Copy(locked.Scan0, bitmap.Width, bitmap.Height, locked.Stride); }
        finally { bitmap.UnlockBits(locked); }
    }
}
