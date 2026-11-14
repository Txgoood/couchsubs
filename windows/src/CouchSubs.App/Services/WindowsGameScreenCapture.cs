// SPDX-License-Identifier: AGPL-3.0-only

using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using CouchSubs.Core;
using CouchSubs.Shell;

namespace CouchSubs.App.Services;

/// <summary>Doğrudan Windows masaüstünden oyun görüntüsü alır; PS5 ve TV gerektirmez.
/// Tam ekran özel (exclusive) oyunlar yakalamayı engelleyebilir; kenarlıksız tam ekran önerilir.
/// Ekranlar takılıp çıkarıldığında her çağrıda yeniden listelenir.</summary>
internal sealed class WindowsGameScreenCapture : IGameScreenCapture
{
    public IReadOnlyList<GameDisplay> Displays() =>
        Screen.AllScreens.Select((screen, index) => new GameDisplay(
            screen.DeviceName,
            $"{(screen.Primary ? "Laptop / Ana ekran" : "Bağlı monitör")} {index + 1} — {screen.Bounds.Width}×{screen.Bounds.Height} ({screen.DeviceName})",
            screen.Primary)).ToArray();

    public Frame Capture(string displayId)
    {
        var screens = Screen.AllScreens;
        var screen = screens.FirstOrDefault(s => s.DeviceName == displayId)
            ?? screens.FirstOrDefault(s => s.Primary) ?? screens.First();
        var bounds = screen.Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
            throw new InvalidOperationException("Seçilen ekranın görüntü boyutu geçersiz.");
        using var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
            graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bitmap.Size, CopyPixelOperation.SourceCopy);
        var locked = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try { return Frame.Copy(locked.Scan0, bitmap.Width, bitmap.Height, locked.Stride); }
        finally { bitmap.UnlockBits(locked); }
    }
}
