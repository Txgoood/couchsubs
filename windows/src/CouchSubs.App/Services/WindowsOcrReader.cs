// SPDX-License-Identifier: AGPL-3.0-only

using System.Runtime.InteropServices.WindowsRuntime;
using CouchSubs.Core;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace CouchSubs.App.Services;

/// <summary>
/// Windows'un cihaz üstü yazı tanıması. Oyun dilinin OCR paketi yoksa kullanıcının yüklü dillerinden
/// biri kullanılır (Latin alfabeli diller İngilizce altyazıyı da okuyabilir). Satırlar yukarıdan aşağı sıralanır.
/// </summary>
public sealed class WindowsOcrReader : ITextReader
{
    private readonly OcrEngine engine;

    private WindowsOcrReader(OcrEngine engine) => this.engine = engine;

    /// <summary>Hiçbir OCR dili yüklü değilse null.</summary>
    public static WindowsOcrReader? Create(string language)
    {
        var engine = OcrEngine.TryCreateFromLanguage(new Language(language))
                     ?? OcrEngine.TryCreateFromUserProfileLanguages();
        return engine is null ? null : new WindowsOcrReader(engine);
    }

    /// <summary>Oyun dilinin kendi OCR paketi yüklü mü (değilse ayarlarda eklenmesi önerilir).</summary>
    public static bool HasLanguage(string language) => OcrEngine.IsLanguageSupported(new Language(language));

    public string Read(Frame image) => Read(image, deep: true);

    public string Read(Frame image, bool deep) => string.Join(' ', ReadLines(image, deep).Lines.Select(line => line.Text));

    public Reading ReadLines(Frame image, bool deep) => deep ? ReadingFallback.FirstLines(image, Recognize) : Recognize(image);

    public Reading ReadScreenPreviewLines(Frame image) =>
        image.Width > OcrEngine.MaxImageDimension || image.Height > OcrEngine.MaxImageDimension
            ? ReadScreenLines(image) : RecognizeOnce(image);

    public Reading ReadScreenLines(Frame image)
    {
        var limit = OcrEngine.MaxImageDimension;
        var scale = image.Height < 900 && image.Width <= limit / 2 && image.Height <= limit / 2 ? 2 : 1;
        var full = scale == 2 ? image.Scaled(scale) : image;
        var lines = RecognizeOnce(full).Lines.ToList();
        // Overlapping bands recover small labels and text near the bottom. Keep complete lines from the full pass.
        var columns = Math.Max(1, (int)Math.Ceiling(image.Width / (limit * 0.85)));
        var rows = Math.Max(3, (int)Math.Ceiling(image.Height / (limit * 0.85)));
        for (var row = 0; row < rows; row++)
        for (var column = 0; column < columns; column++)
        {
            var left = image.Width * column / columns;
            var right = image.Width * (column + 1) / columns;
            var top = image.Height * row / rows;
            var bottom = image.Height * (row + 1) / rows;
            var xPad = columns == 1 ? 0 : (right - left) / 14;
            var yPad = (bottom - top) / 14;
            left = Math.Max(0, left - xPad);
            right = Math.Min(image.Width, right + xPad);
            top = Math.Max(0, top - yPad);
            bottom = Math.Min(image.Height, bottom + yPad);
            var area = new TextBox((double)left / image.Width, (double)top / image.Height,
                (double)right / image.Width, (double)bottom / image.Height);
            var part = image.Crop(left, top, right - left, bottom - top);
            if (image.Height < 900 && part.Width <= limit / 2 && part.Height <= limit / 2)
                part = part.Scaled(2);
            foreach (var line in Recognize(part).PlacedIn(area).Lines)
            {
                if (line.Box is not { } box) continue;
                if (left > 0 && box.Left <= area.Left + 2.0 / image.Width ||
                    right < image.Width && box.Right >= area.Right - 2.0 / image.Width ||
                    top > 0 && box.Top <= area.Top + 2.0 / image.Height ||
                    bottom < image.Height && box.Bottom >= area.Bottom - 2.0 / image.Height)
                    continue;
                var match = lines.FindIndex(existing => existing.Box is { } other &&
                    ScreenTranslation.SameOverlayPosition(box, other));
                if (match < 0)
                    lines.Add(line);
                else
                {
                    var original = TextMatch.Normalize(lines[match].Text);
                    var candidate = TextMatch.Normalize(line.Text);
                    if (original.Length > 2 && candidate.Length > original.Length + 3 &&
                        (" " + candidate + " ").Contains(" " + original + " ", StringComparison.Ordinal))
                        lines[match] = line;
                }
            }
        }
        return new Reading(lines);
    }


    /// <summary>
    /// Kare hem olduğu gibi hem de yalnız beyaz yazısı bırakılarak okunur; daha sözcüğe benzeyen okuma seçilir.
    /// Parlak sahnelerde ikincisi, renkli altyazılarda birincisi kazanır.
    /// </summary>
    private Reading Recognize(Frame image)
    {
        var raw = RecognizeOnce(image);
        if (image.Width < 40 || image.Height < 40) return raw;
        var whiteText = RecognizeOnce(TextImage.WhiteTextOnly(image));
        return OcrNoise.Score(whiteText.Text) > OcrNoise.Score(raw.Text) ? whiteText : raw;
    }

    private Reading RecognizeOnce(Frame image)
    {
        // OCR motoru çok küçük ya da çok büyük görüntüyü reddeder.
        if (image.Width < 40 || image.Height < 40 || image.Width > OcrEngine.MaxImageDimension || image.Height > OcrEngine.MaxImageDimension)
            return Reading.Empty;
        using var bitmap = new SoftwareBitmap(BitmapPixelFormat.Bgra8, image.Width, image.Height, BitmapAlphaMode.Premultiplied);
        bitmap.CopyFromBuffer(image.Pixels.AsBuffer());
        var result = engine.RecognizeAsync(bitmap).AsTask().GetAwaiter().GetResult();
        double width = image.Width, height = image.Height;
        var lines = result.Lines
            .SelectMany(line => Split(line, width, height))
            .OrderBy(line => line.Box?.Top ?? 0);
        return new Reading(lines);
    }

    /// <summary>
    /// Satırın kutusu sözcük kutularının birleşimidir; görüntünün kenarlarına oranla verilir. Windows aynı hizadaki
    /// uzak yazıları (soldaki menü, sağdaki gösterge) bazen tek satır sayar; aradaki boşluk bir sözcük boşluğu olamayacak
    /// kadar genişse satır orada ayrı satırlara bölünür (<see cref="Reading.IsSeparateText"/>).
    /// </summary>
    private static IEnumerable<TextLine> Split(OcrLine line, double width, double height)
    {
        var words = line.Words.ToList();
        if (words.Count == 0) yield break;
        var lineHeight = words.Max(word => word.BoundingRect.Height);
        var start = 0;
        for (var index = 1; index <= words.Count; index++)
        {
            var end = index == words.Count;
            if (!end && !Reading.IsSeparateText(words[index].BoundingRect.X - (words[index - 1].BoundingRect.X + words[index - 1].BoundingRect.Width), lineHeight, width))
                continue;
            var part = words.GetRange(start, index - start);
            var wordBoxes = part.Select(word =>
            {
                var r = word.BoundingRect;
                return new TextLine(word.Text, new TextBox(r.X / width, r.Y / height,
                    (r.X + r.Width) / width, (r.Y + r.Height) / height));
            }).ToArray();
            var box = wordBoxes.Select(word => word.Box!.Value).Aggregate((a, b) => a.Union(b));
            // Bölünmeyen satırın metni Windows'un verdiği gibi kalır (boşluksuz yazılan dillerde sözcükler birleşik).
            var text = start == 0 && end ? line.Text : string.Join(' ', part.Select(word => word.Text));
            yield return new TextLine(text.Trim(), box) { Words = wordBoxes };
            start = index;
        }
    }
}
