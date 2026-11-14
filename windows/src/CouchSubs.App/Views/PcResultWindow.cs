// SPDX-License-Identifier: AGPL-3.0-only

using System.Windows;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Forms;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace CouchSubs.App.Views;

/// <summary>
/// Ek bölgenin çevirisini bilgisayarda gösteren küçük kutu: ekranın altında ortada, her pencerenin üstünde durur,
/// odağı almaz. Süre dolunca ya da tıklanınca kapanır.
/// </summary>
public sealed class PcResultWindow : Window
{
    private static PcResultWindow? current;
    private readonly DispatcherTimer timer;
    private readonly System.Drawing.Rectangle? monitorBounds;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowDisplayAffinity(nint hwnd, uint affinity);

    // Windows 10 2004+: bu pencere ekran görüntüsü alırken görünmez, oyuncuya görünür.
    private const uint WdaExcludeFromCapture = 0x11;

    /// <summary>Çeviriyi gösterir; önceki kutu varsa yerine geçer.</summary>
    public static void Display(string text, int milliseconds)
    {
        current?.Close();
        current = new PcResultWindow(text, milliseconds);
        current.Show();
    }

    /// <summary>PC oyun çevirisini seçilen laptop/Agon ekranında göster.</summary>
    public static void DisplayOnMonitor(string text, int milliseconds, string displayId)
    {
        current?.Close();
        var monitor = Screen.AllScreens.FirstOrDefault(s => s.DeviceName == displayId)
            ?? Screen.AllScreens.FirstOrDefault(s => s.Primary);
        current = new PcResultWindow(text, milliseconds, monitor?.Bounds);
        current.Show();
    }

    private PcResultWindow(string text, int milliseconds, System.Drawing.Rectangle? screen = null)
    {
        monitorBounds = screen;
        var work = SystemParameters.WorkArea;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        MaxWidth = (monitorBounds?.Width ?? work.Width) * 0.7;
        Cursor = Cursors.Hand;

        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xEE, 0, 0, 0)),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(20, 14, 20, 14),
            Child = new TextBlock
            {
                Text = text,
                Foreground = Brushes.White,
                FontSize = 24,
                TextWrapping = TextWrapping.Wrap,
            },
        };

        // Boyut ölçüldükten sonra altta ortaya yerleştirilir.
        Loaded += (_, _) =>
        {
            var bounds = monitorBounds;
            if (bounds is null)
            {
                Left = work.Left + ((work.Width - ActualWidth) / 2);
                Top = work.Bottom - ActualHeight - 40;
                return;
            }
            // Windows Forms piksel koordinatları; WPF DPI dönüşümü burada uygulanır.
            var source = PresentationSource.FromVisual(this);
            var dpi = source?.CompositionTarget?.TransformFromDevice ?? System.Windows.Media.Matrix.Identity;
            var topLeft = dpi.Transform(new System.Windows.Point(bounds.Value.Left, bounds.Value.Top));
            var bottomRight = dpi.Transform(new System.Windows.Point(bounds.Value.Right, bounds.Value.Bottom));
            Left = topLeft.X + ((bottomRight.X - topLeft.X - ActualWidth) / 2);
            Top = bottomRight.Y - ActualHeight - 40;
            // Overlay'in tekrar OCR'a girip döngü oluşturmasını önle.
            try { SetWindowDisplayAffinity(new WindowInteropHelper(this).Handle, WdaExcludeFromCapture); }
            catch (Exception) { /* daha eski Windows'ta desteklenmez */ }
        };
        MouseLeftButtonDown += (_, _) => Close();

        timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Math.Max(1500, milliseconds)) };
        timer.Tick += (_, _) => Close();
        timer.Start();
        Closed += (_, _) =>
        {
            timer.Stop();
            if (ReferenceEquals(current, this)) current = null;
        };
    }
}
