// SPDX-License-Identifier: AGPL-3.0-only

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using CouchSubs.App.Services;
using CouchSubs.Core;

namespace CouchSubs.App.Views;

/// <summary>
/// Konuşma altyazısından bağımsız, ekranın sol üstünde tıklamayı engellemeyen menü çeviri kutusu.
/// PS5/TV ekranı bunun yerine ScreenTranslation koordinatlı yerleşim kullanır.
/// </summary>
public sealed class PcWorldTextWindow : Window
{
    private static PcWorldTextWindow? current;
    private readonly TextBlock content;
    private const int GwlExstyle = -20;
    private static readonly nint WsExTransparent = (nint)0x20;
    private static readonly nint WsExNoactivate = (nint)0x08000000;
    private const uint WdaExcludeFromCapture = 0x11;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint hwnd, int index, nint style);
    [DllImport("user32.dll")]
    private static extern bool SetWindowDisplayAffinity(nint hwnd, uint affinity);

    public static void Display(IReadOnlyList<ScreenTranslation.Item> items, string displayId)
    {
        if (items.Count == 0) { Clear(); return; }
        if (current is null)
        {
            current = new PcWorldTextWindow(displayId);
            current.Show();
        }
        current.content.Text = string.Join("\n", items.Select(item => item.Translation));
    }

    public static void Clear()
    {
        current?.Close();
        current = null;
    }

    private PcWorldTextWindow(string displayId)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        Width = 440;
        MaxHeight = 680;
        var box = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xDC, 12, 18, 25)),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14),
            Child = content = new TextBlock
            {
                Foreground = Brushes.White,
                FontSize = 18,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 420,
            },
        };
        Content = box;
        Loaded += (_, _) =>
        {
            try
            {
                var bounds = WindowsGameScreenCapture.BoundsFor(displayId);
                var dpi = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice
                    ?? Matrix.Identity;
                var origin = dpi.Transform(new Point(bounds.Left, bounds.Top));
                Left = origin.X + 24;
                Top = origin.Y + 32;
                var handle = new WindowInteropHelper(this).Handle;
                var flags = GetWindowLongPtr(handle, GwlExstyle);
                SetWindowLongPtr(handle, GwlExstyle, flags | WsExTransparent | WsExNoactivate);
                SetWindowDisplayAffinity(handle, WdaExcludeFromCapture);
            }
            catch (Exception) { /* Monitör çıkarılırsa normal WPF konumlandırması kullanılır. */ }
        };
        Closed += (_, _) =>
        {
            if (ReferenceEquals(current, this)) current = null;
        };
    }
}
