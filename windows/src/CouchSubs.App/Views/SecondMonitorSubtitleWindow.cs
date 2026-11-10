// SPDX-License-Identifier: AGPL-3.0-only

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace CouchSubs.App.Views;

/// <summary>
/// A dedicated fullscreen Turkish subtitle canvas on the second Windows display.
/// The PS5 stays connected directly to the AOC via HDMI; no TV application is required.
/// </summary>
public sealed class SecondMonitorSubtitleWindow : Window
{
    private static SecondMonitorSubtitleWindow? current;
    private readonly TextBlock subtitle;
    private readonly DispatcherTimer clearTimer;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    private static readonly IntPtr TopMost = new(-1);
    private const uint NoActivate = 0x0010;
    private const uint ShowWindow = 0x0040;

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
        // AOC is driven by the PS5, so Dell is normally the only non-primary PC screen.
        var monitor = System.Windows.Forms.Screen.AllScreens.FirstOrDefault(s => !s.Primary)
            ?? System.Windows.Forms.Screen.PrimaryScreen
            ?? System.Windows.Forms.Screen.AllScreens[0];
        var rect = monitor.Bounds;

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

        // Win32 operates in physical pixels, including when the MSI and Dell have different DPI.
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
