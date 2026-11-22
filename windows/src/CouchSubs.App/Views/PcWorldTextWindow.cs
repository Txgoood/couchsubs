// SPDX-License-Identifier: AGPL-3.0-only

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using CouchSubs.App.Services;
using CouchSubs.Core;

namespace CouchSubs.App.Views;

/// <summary>
/// Click-through menu translations at their original screen coordinates.
/// </summary>
public sealed class PcWorldTextWindow : Window
{
    private static PcWorldTextWindow? current;
    private readonly Canvas content = new() { ClipToBounds = true };
    private readonly DispatcherTimer expiry = new();
    private readonly string displayId;
    private IReadOnlyList<ScreenTranslation.Item> items = [];
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

    public static void Display(IReadOnlyList<ScreenTranslation.Item> items, string displayId, int durationMs)
    {
        if (items.Count == 0) { Clear(); return; }
        if (current is null || current.displayId != displayId)
        {
            Clear();
            current = new PcWorldTextWindow(displayId);
            current.Show();
        }
        current.items = items;
        current.RenderItems();
        current.expiry.Stop();
        current.expiry.Interval = TimeSpan.FromMilliseconds(durationMs);
        current.expiry.Start();
    }

    public static void Clear()
    {
        current?.Close();
        current = null;
    }

    private PcWorldTextWindow(string displayId)
    {
        this.displayId = displayId;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        Width = Height = 1;
        Content = content;
        expiry.Tick += (_, _) => Close();
        content.SizeChanged += (_, _) => RenderItems();
        Loaded += (_, _) =>
        {
            try
            {
                var handle = new WindowInteropHelper(this).Handle;
                var flags = GetWindowLongPtr(handle, GwlExstyle);
                SetWindowLongPtr(handle, GwlExstyle, flags | WsExTransparent | WsExNoactivate);
                SetWindowDisplayAffinity(handle, WdaExcludeFromCapture);
                WindowsGameScreenCapture.PlaceOverlay(handle, displayId);
            }
            catch (Exception) { /* Monitör çıkarılırsa normal WPF konumlandırması kullanılır. */ }
        };
        Closed += (_, _) =>
        {
            expiry.Stop();
            if (ReferenceEquals(current, this)) current = null;
        };
    }

    private void RenderItems()
    {
        content.Children.Clear();
        foreach (var item in items)
        {
            var box = item.Box;
            var width = (box.Right - box.Left) * content.ActualWidth;
            var height = (box.Bottom - box.Top) * content.ActualHeight;
            if (width <= 0 || height <= 0) continue;
            var paint = item.Paint;
            var multiline = box.Bottom - box.Top > item.LineHeight * 1.4;
            var label = new TextBlock
            {
                Text = item.Translation,
                Foreground = paint is null ? Brushes.White : new SolidColorBrush(ParseColor(paint.Foreground)),
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = Math.Clamp(item.LineHeight * content.ActualHeight * 1.12, 7, 150),
                FontWeight = paint?.Bold == true ? FontWeights.SemiBold : FontWeights.Normal,
                MaxWidth = multiline ? width : double.PositiveInfinity,
                TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap
            };
            Brush backdrop = paint is null ? Brushes.DimGray :
                new LinearGradientBrush(ParseColor(paint.Left), ParseColor(paint.Right), 0);
            var panel = new Border
            {
                Background = backdrop,
                Width = width + 2,
                Height = height + 2,
                Child = new Viewbox
                {
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Stretch = Stretch.Uniform,
                    StretchDirection = StretchDirection.DownOnly,
                    Child = label
                }
            };
            Canvas.SetLeft(panel, Math.Max(0, box.Left * content.ActualWidth - 1));
            Canvas.SetTop(panel, Math.Max(0, box.Top * content.ActualHeight - 1));
            content.Children.Add(panel);
        }
    }

    private static Color ParseColor(string value) => (Color)ColorConverter.ConvertFromString(value);
}
