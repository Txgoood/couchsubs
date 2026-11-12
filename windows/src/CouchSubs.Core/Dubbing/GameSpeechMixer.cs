// SPDX-License-Identifier: AGPL-3.0-only

namespace CouchSubs.Core.Dubbing;

/// <summary>
/// PS5 oyun sesi ile Türkçe konuşma sesini aynı Windows çıkışına verecek akışın
/// PCM karıştırma aşaması. İki akışın sample rate ve kanal düzeni çağıran
/// katmanda eşitlenmiş olmalıdır; Remote Play Opus çözümü ayrı bir aşamadır.
/// </summary>
public static class GameSpeechMixer
{
    /// <summary>
    /// Ayrık interleaved PCM örneklerini karıştırır. Türkçe dublaj aktifken
    /// oyunun sesini kısar (ducking) ve taşmayı önler. Tamponu yerinde işler;
    /// gerçek zamanlı ses iş parçacığında ek tahsis yapmaz.
    /// </summary>
    public static void Mix(Span<float> destination, ReadOnlySpan<float> game, ReadOnlySpan<float> speech,
        float gameVolume = 1f, float speechVolume = 1f, float ducking = 0.35f)
    {
        if (!float.IsFinite(gameVolume) || gameVolume < 0f || gameVolume > 1f ||
            !float.IsFinite(speechVolume) || speechVolume < 0f || speechVolume > 1f ||
            !float.IsFinite(ducking) || ducking < 0f || ducking > 1f)
            throw new ArgumentOutOfRangeException(nameof(gameVolume), "Volumes and ducking must be between 0 and 1.");

        var count = destination.Length;
        for (var i = 0; i < count; i++)
        {
            var voice = i < speech.Length ? speech[i] * speechVolume : 0f;
            var gameSample = i < game.Length ? game[i] * gameVolume : 0f;
            var background = MathF.Abs(voice) > 0.0001f ? gameSample * ducking : gameSample;
            destination[i] = Math.Clamp(background + voice, -1f, 1f);
        }
    }
}
