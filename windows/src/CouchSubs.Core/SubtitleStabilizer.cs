// SPDX-License-Identifier: AGPL-3.0-only

namespace CouchSubs.Core;

public enum StabilizerKind { None, Translate, Clear }

/// <summary>Okuma hattının bir kareye vereceği tepki.</summary>
public readonly record struct StabilizerAction(StabilizerKind Kind, string? Text = null)
{
    public static readonly StabilizerAction None = new(StabilizerKind.None);
    public static readonly StabilizerAction Clear = new(StabilizerKind.Clear);
    public static StabilizerAction Translate(string text) => new(StabilizerKind.Translate, text);
}

/// <summary>
/// Kare kare gelen OCR çıktısını izler; altyazı kararlı hale gelince çevrilmesini,
/// ekrandan kalkınca TV'den temizlenmesini ister. Aynı altyazıyı tekrar göndermez.
/// </summary>
public sealed class SubtitleStabilizer(double similarity = 0.85, int stableFrames = 2, int emptyFramesToClear = 2, int repeatGuardFrames = 30)
{
    private string candidate = "";
    private int candidateCount;
    private string sent = "";
    private string sentLabel = "";
    private string sentBody = "";
    private int emptyCount;
    // Keep a short-term identity after OCR briefly loses the English subtitle.
    // Unlike sent, recentSent survives the visual Clear event; a flicker is not
    // a new spoken line. Frame-based rather than wall clock for deterministic tests.
    private string recentSent = "";
    private int frameNumber;
    private int recentSentFrame;

    public StabilizerAction Feed(string rawText)
    {
        frameNumber++;
        var text = TextMatch.Normalize(rawText);
        var labelEnd = rawText.IndexOf(':');
        var label = labelEnd is > 0 and <= 28 ? TextMatch.Normalize(rawText[..labelEnd]) : "";
        var body = label.Length > 0 ? TextMatch.Normalize(rawText[(labelEnd + 1)..]) : text;
        // 3 karakterden kısa okumalar (gürültü, tek simge) boş kare sayılır.
        if (text.Length < 3)
        {
            candidate = "";
            candidateCount = 0;
            emptyCount++;
            if (emptyCount == emptyFramesToClear && sent.Length > 0)
            {
                sent = "";
                return StabilizerAction.Clear;
            }
            return StabilizerAction.None;
        }

        // Missing words at either edge of a named subtitle are common when
        // the background moves. Let an empty run or another named turn end it.
        if (emptyCount < emptyFramesToClear && label.Length == 0 && sentLabel.Length > 0 &&
            IsClippedReading(body, sentBody))
        {
            emptyCount = 0;
            return StabilizerAction.None;
        }

        emptyCount = 0;
        if (TextMatch.IsSimilar(text, candidate, similarity))
        {
            candidateCount++;
        }
        else
        {
            candidate = text;
            candidateCount = 1;
        }
        // Daktilo efektinde metin büyürken aday değişir; tamamlanıp kararlı olunca gider.
        // Noktalamayla biten satır zaten tamamdır; hızlı konuşmada beklemeden ilk karede gider.
        var ready = candidateCount == stableFrames || (candidateCount == 1 && EndsSentence(rawText));
        // Gönderilenin gürültülü yeniden okuması yeni satır değildir; TV'de aynı cümle iki kez görünmesin.
        if (!ready || TextMatch.IsSimilar(text, sent, similarity) || TextMatch.IsSameSubtitle(text, sent)) return StabilizerAction.None;
        // An OCR dropout can clear the TV subtitle although the English words
        // remain visible. Do not translate or dub the same sentence again.
        if (recentSent.Length > 0 && frameNumber - recentSentFrame <= repeatGuardFrames &&
            (TextMatch.IsSimilar(text, recentSent, similarity) || TextMatch.IsSameSubtitle(text, recentSent)))
        {
            // Retain the same identity until an actually different subtitle arrives.
            sent = recentSent;
            return StabilizerAction.None;
        }
        sent = text;
        sentLabel = label;
        sentBody = body;
        recentSent = text;
        recentSentFrame = frameNumber;
        return StabilizerAction.Translate(rawText.Trim());
    }

    private static bool IsClippedReading(string fragment, string full)
    {
        if (fragment.Length < 6 || fragment.Length >= full.Length) return false;
        var part = fragment.Split(' ');
        var whole = full.Split(' ');
        if (part.Length < 2 || whole.Length < 4 || part.Length >= whole.Length) return false;

        // Compare only the edges, allowing a space to disappear in OCR.
        var compact = string.Concat(part);
        for (var count = Math.Max(1, part.Length - 1); count <= Math.Min(whole.Length, part.Length + 1); count++)
            if (TextMatch.IsSimilar(compact, string.Concat(whole.Take(count)), 0.84) ||
                TextMatch.IsSimilar(compact, string.Concat(whole.TakeLast(count)), 0.84)) return true;
        return false;
    }

    /// <summary>Satır nokta, ünlem, soru ya da üç noktayla bitiyor mu (kapanan tırnak ve parantezler yok sayılır).</summary>
    public static bool EndsSentence(string raw)
    {
        var text = raw.Trim().TrimEnd('"', '\'', '”', '’', '»', ')', ']');
        return text.Length > 0 && ".!?…".Contains(text[^1]);
    }

    /// <summary>Çeviri kaynağıyla neredeyse aynıysa metin zaten hedef dildedir (ör. konsolun Türkçe menüleri).</summary>
    public bool ShouldSend(string source, string translation) =>
        !TextMatch.IsSimilar(TextMatch.Normalize(source), TextMatch.Normalize(translation), 0.90);

    /// <summary>Son gönderilen satırı unutur (ör. çevirisi başarısız oldu); satır ekranda kaldıkça yeniden kararlı sayılıp çevrilir.</summary>
    public void ForgetSent()
    {
        sent = "";
        sentLabel = "";
        sentBody = "";
        recentSent = "";
        candidate = "";
        candidateCount = 0;
    }

    public StabilizerAction Reset()
    {
        candidate = "";
        candidateCount = 0;
        emptyCount = 0;
        recentSent = "";
        recentSentFrame = 0;
        frameNumber = 0;
        sentLabel = "";
        sentBody = "";
        if (sent.Length == 0) return StabilizerAction.None;
        sent = "";
        return StabilizerAction.Clear;
    }
}
