// SPDX-License-Identifier: AGPL-3.0-only

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CouchSubs.App.Services;

namespace CouchSubs.App.Views;

internal sealed class SmoothVideoWindow(string displayId, int pixelWidth, int pixelHeight) : Window
{
    private const int ExtendedStyle = -20;
    private const int ClickThroughAndNoActivate = 0x08000020;
    private const uint ExcludeFromCapture = 0x11;
    private WriteableBitmap? bitmap;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint handle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint handle, int index, nint value);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowDisplayAffinity(nint handle, uint affinity);

    [DllImport("winmm.dll")]
    private static extern uint timeBeginPeriod(uint period);

    [DllImport("winmm.dll")]
    private static extern uint timeEndPeriod(uint period);

    public async Task PlayAsync(System.Drawing.Rectangle bounds, Action ready, CancellationToken cancellation)
    {
        using var generator = new SmoothFrameGenerator(pixelWidth, pixelHeight);
        await generator.WarmUpAsync(cancellation).ConfigureAwait(false);
        using var capture = new ScaledGameCapture(bounds, pixelWidth, pixelHeight);
        var fineTimer = timeBeginPeriod(1) == 0;
        try { await RenderAsync(capture, generator, ready, cancellation).ConfigureAwait(false); }
        finally { if (fineTimer) timeEndPeriod(1); }
    }

    private async Task RenderAsync(ScaledGameCapture capture, SmoothFrameGenerator generator,
        Action ready, CancellationToken cancellation)
    {
        var frameSize = pixelWidth * pixelHeight * 4;
        var available = Channel.CreateUnbounded<byte[]>();
        for (var i = 0; i < 3; i++) available.Writer.TryWrite(new byte[frameSize]);
        var latest = Channel.CreateBounded<(byte[] Pixels, TimeSpan At)>(1);
        var generated = new byte[frameSize];
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        var token = lifetime.Token;
        var clock = Stopwatch.StartNew();
        var interval = TimeSpan.FromSeconds(1.0 / 30);
        var previousCapture = TimeSpan.Zero;
        var rateCheck = TimeSpan.Zero;
        var presented = 0;
        var consumed = 0;
        var slowIntervals = 0;
        var first = true;
        var source = Task.Run(CaptureAsync);
        try
        {
            while (true)
            {
                var (original, capturedAt) = await latest.Reader.ReadAsync(token).ConfigureAwait(false);
                var gap = capturedAt - previousCapture;
                previousCapture = capturedAt;
                var reset = first || gap > TimeSpan.FromMilliseconds(100);
                if (await generator.GenerateAsync(original, generated, reset, token).ConfigureAwait(false))
                {
                    await DrawAsync(generated).ConfigureAwait(false);
                    presented++;
                    await Task.Delay(TimeSpan.FromMilliseconds(12), token).ConfigureAwait(false);
                }
                await DrawAsync(original).ConfigureAwait(false);
                available.Writer.TryWrite(original);
                presented++;
                consumed++;
                if (first)
                {
                    first = false;
                    rateCheck = clock.Elapsed;
                    await Dispatcher.InvokeAsync(ready, DispatcherPriority.Background, token);
                }
                var elapsed = clock.Elapsed - rateCheck;
                if (elapsed < TimeSpan.FromSeconds(1)) continue;
                var writesPerSecond = presented / elapsed.TotalSeconds;
                Trace.WriteLine($"Smooth preview: {writesPerSecond:F1} writes/s from {consumed / elapsed.TotalSeconds:F1} input frames/s.");
                // Long stationary sections need no new frames. Only fall back under a sustained active load.
                slowIntervals = consumed >= 12 && writesPerSecond < 36 ? slowIntervals + 1 : 0;
                if (slowIntervals >= 2)
                    throw new InvalidOperationException("Akıcı görüntü yetişemedi. Normal görüntüye dönüldü.");
                rateCheck = clock.Elapsed;
                presented = consumed = 0;
            }
        }
        catch (ChannelClosedException error) when (error.InnerException is { } captureError)
        {
            throw new InvalidOperationException("Akıcı görüntü için ekran yakalama durdu.", captureError);
        }
        catch (ChannelClosedException) when (token.IsCancellationRequested)
        {
            token.ThrowIfCancellationRequested();
        }
        finally
        {
            lifetime.Cancel();
            await source.ConfigureAwait(false);
        }

        Task DrawAsync(byte[] pixels) =>
            Dispatcher.InvokeAsync(() => Present(pixels), DispatcherPriority.Render, token).Task;

        async Task CaptureAsync()
        {
            using var timer = new PeriodicTimer(interval);
            var gotFrame = false;
            try
            {
                while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
                {
                    var pixels = await available.Reader.ReadAsync(token).ConfigureAwait(false);
                    var capturedAt = clock.Elapsed;
                    if (!await capture.CopyToAsync(pixels, token).ConfigureAwait(false))
                    {
                        available.Writer.TryWrite(pixels);
                        if (!gotFrame && capturedAt >= TimeSpan.FromSeconds(2))
                            throw new InvalidOperationException("Seçilen ekrandan kare alınamadı.");
                        continue;
                    }
                    gotFrame = true;
                    if (latest.Reader.TryRead(out var replaced)) available.Writer.TryWrite(replaced.Pixels);
                    latest.Writer.TryWrite((pixels, capturedAt));
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception error) { latest.Writer.TryComplete(error); }
            finally { latest.Writer.TryComplete(); }
        }
    }

    public void Present(byte[] pixels)
    {
        if (bitmap is null)
        {
            bitmap = new WriteableBitmap(pixelWidth, pixelHeight, 96, 96, PixelFormats.Bgr32, null);
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Width = Height = 1;
            ShowActivated = false;
            ShowInTaskbar = false;
            Topmost = true;
            IsHitTestVisible = false;
            var image = new Image { Source = bitmap, Stretch = Stretch.Fill };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.LowQuality);
            Content = image;
            var handle = new WindowInteropHelper(this).EnsureHandle();
            SetWindowLongPtr(handle, ExtendedStyle, GetWindowLongPtr(handle, ExtendedStyle) | ClickThroughAndNoActivate);
            if (!SetWindowDisplayAffinity(handle, ExcludeFromCapture))
                throw new InvalidOperationException("Windows akıcı görüntüyü ekran yakalamadan ayıramadı.");
            WindowsGameScreenCapture.PlaceOverlay(handle, displayId);
            Write(pixels);
            Show();
            WindowsGameScreenCapture.PlaceOverlay(handle, displayId);
            PcWorldTextWindow.KeepAboveGameVideo();
            PcResultWindow.KeepAboveGameVideo();
            return;
        }
        Write(pixels);
    }

    private void Write(byte[] pixels) =>
        bitmap!.WritePixels(new Int32Rect(0, 0, pixelWidth, pixelHeight), pixels, pixelWidth * 4, 0);
}
