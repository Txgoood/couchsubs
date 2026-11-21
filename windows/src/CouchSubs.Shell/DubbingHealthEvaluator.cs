// SPDX-License-Identifier: AGPL-3.0-only
namespace CouchSubs.Shell;

/// <summary>
/// Passive, bounded health checks for long dubbing sessions. No audio
/// processing, network calls, logs containing speech, or automatic restarts.
/// A stale or unavailable measurement is not presented as a failure.
/// </summary>
public enum DubbingHealthLevel { Idle, InsufficientData, Healthy, Warning, Error }

public readonly record struct DubbingHealthInputs(
    bool SessionRunning, bool DubbingEnabled, bool ModelInstalled,
    int PendingLines, int BacklogLimit,
    int LastSynthesisMs, long SynthesisCalls, double AverageSynthesisMs,
    long SlowSynthesisCalls, long LateLines, long SuppressedDuplicates,
    long SpeechInferenceCalls, long SpeechInferenceErrors, string? DubbingError);

public readonly record struct DubbingHealthResult(DubbingHealthLevel Level,
    string Summary, string Recommendation);

public static class DubbingHealthEvaluator
{
    public static DubbingHealthResult Evaluate(in DubbingHealthInputs state)
    {
        if (!state.SessionRunning || !state.DubbingEnabled)
            return new(DubbingHealthLevel.Idle, "Dublaj oturumu kapalı",
                "Dublajı başlattığınızda ölçümler görünür.");
        if (!state.ModelInstalled)
            return new(DubbingHealthLevel.Error, "Dublaj ses motoru kurulu değil",
                "Ses modelini indirin veya kullanılabilir ses motorunu seçin.");
        if (!string.IsNullOrWhiteSpace(state.DubbingError))
            return new(DubbingHealthLevel.Error, "Dublaj motoru hata bildirdi",
                "Dublaj hata mesajını kontrol edin; oyun sesine dokunulmadı.");
        if (state.PendingLines >= state.BacklogLimit && state.BacklogLimit > 0)
            return new(DubbingHealthLevel.Warning, "Dublaj kuyruğu dolu",
                "Gecikmeyi ve TTS üretim süresini kontrol edin.");
        if (state.LastSynthesisMs > 3500 || state.AverageSynthesisMs > 2500)
            return new(DubbingHealthLevel.Warning, "Türkçe seslendirme gecikiyor",
                "TTS motorunu ve işlemci kullanımını kontrol edin.");
        if (state.SpeechInferenceCalls >= 10 &&
            state.SpeechInferenceErrors > state.SpeechInferenceCalls / 5)
            return new(DubbingHealthLevel.Warning, "Konuşma algılama hata oranı yüksek",
                "Konuşma tanıma modelini kontrol edin; metin çevirisi devam edebilir.");
        if (state.SynthesisCalls == 0)
            return new(DubbingHealthLevel.InsufficientData,
                "Dublaj etkin; henüz ölçülebilir konuşma yok",
                "Bir konuşma geçtikten sonra veriler otomatik güncellenir.");
        return new(DubbingHealthLevel.Healthy, "Dublaj ölçümleri normal",
            "Oyun sesi ve altyazı akışı değiştirilmedi.");
    }
}
