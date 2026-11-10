// SPDX-License-Identifier: AGPL-3.0-only

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace CouchSubs.App.Views;

/// <summary>
/// Fullscreen Turkish subtitle canvas on the second Windows display.
/// The PS5 plays on the AOC directly through HDMI; only the translated text is displayed here.
/// </summary>
public sealed class SecondMonitorSubtitleWindow : Window
{
    private static SecondMonitorSubtitleWindow? current;
    private readonly TextBlock subtitle;
    private readonly DispatcherTimer clearTimer;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public int Flags;
    }

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, ref NativeRect bounds, IntPtr data);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);

    private static readonly IntPtr TopMost = new(-1);
    private const uint NoActivate = 0x0010;
    private const uint ShowWindow = 0x0040;

    private static NativeRect TargetScreen()
    {
        var monitors = new List<(NativeRect Area, bool Primary)>();

        bool Collect(IntPtr monitor, IntPtr hdc, ref NativeRect bounds, IntPtr data)
        {
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (GetMonitorInfo(monitor, ref info))
                monitors.Add((info.Monitor, (info.Flags & 1) != 0));
            return true;
        }

        MonitorEnumProc callback = Collect;
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
        // The AOC uses PS5 HDMI, so the Dell is normally the non-primary Windows display.
        var secondary = monitors.FirstOrDefault(m => !m.Primary);
        if (secondary.Area.Width > 0) return secondary.Area;
        if (monitors.Count > 0) return monitors[0].Area;
        return new NativeRect
        {
            Left = 0, Top = 0,
            Right = (int)SystemParameters.PrimaryScreenWidth,
            Bottom = (int)SystemParameters.PrimaryScreenHeight
        };
    }

    public static void Open()
    {
        if (current is not null) return;
        current = new SecondMonitorSubtitleWindow();
        current.Show();
    }

    public static void Display(string translated, int milliseconds)
    {
        Open();
        if (current is not { } window) return;
        window.subtitle.Text = translated;
        window.clearTimer.Stop();
        window.clearTimer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(milliseconds, 2000, 15000));
        window.clearTimer.Start();
    }

    public static void CloseOutput()
    {
        current?.Close();
        current = null;
    }

    private SecondMonitorSubtitleWindow()
    {
        var rect = TargetScreen();
        Title = "CouchSubs TR — Dell Altyazı";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        ShowActivated = false;
        ShowInTaskbar = false;
        Topmost = true;
        Background = Brushes.Black;
        Left = rect.Left;
        Top = rect.Top;
        Width = rect.Width;
        Height = rect.Height;

        subtitle = new TextBlock
        {
            Text = "Türkçe altyazı bekleniyor…",
            Foreground = Brushes.White,
            FontFamily = new FontFamily("Segoe UI"),
            FontWeight = FontWeights.SemiBold,
            FontSize = 44,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(65),
            MaxWidth = Math.Max(400, rect.Width * 0.88)
        };
        Content = subtitle;

        // SetWindowPos uses physical screen pixels so mismatched MSI/Dell DPI scaling is safe.
        SourceInitialized += (_, _) =>
            SetWindowPos(new WindowInteropHelper(this).Handle, TopMost,
                rect.Left, rect.Top, rect.Width, rect.Height, NoActivate | ShowWindow);

        clearTimer = new DispatcherTimer();
        clearTimer.Tick += (_, _) =>
        {
            clearTimer.Stop();
            subtitle.Text = "";
        };
        Closed += (_, _) =>
        {
            clearTimer.Stop();
            if (ReferenceEquals(current, this)) current = null;
        };
    }
}
