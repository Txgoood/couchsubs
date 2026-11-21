// SPDX-License-Identifier: AGPL-3.0-only

using System.IO;
using CouchSubs.Core.Dubbing;
using CouchSubs.Shell;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace CouchSubs.App.Services;

/// <summary>
/// PS5 Remote Play sesini ve Türkçe dublajı Windows varsayılan çıkışına miksler.
/// PULSE 3D USB adaptörü bilgisayara takılı ve Windows varsayılan çıkışı seçili olmalıdır.
/// 48 kHz stereo PCM akışı; tampon dolarsa eski ses atılır, sessizlikte takılmaz.
/// </summary>
internal sealed class Pulse3DAudioOutput : IGameAudioOutput, ISampleProvider
{
    private const int Rate = 48000;
    private const int Channels = 2;
    private readonly GameAudioBuffer game = new(Rate * Channels / 4); // 250 ms max
    private readonly SeparatedAudioFrameRouter stemRouter = new();
    // Only processes externally VERIFIED, time-aligned dialogue stem frames.
    // Standard PS5 PCM always uses the unchanged PushGameAudio path.
    private readonly AtmosphereSafeStemProcessor atmosphereStem = new();
    private readonly object speechGate = new();
    private float[]? speech;
    private int speechPosition;
    private float nextSpeechPan;
    private float nextSpeechConfidence;
    // Separate envelopes preserve stereo space and prevent abrupt ambience changes.
    private readonly AtmosphereMixEnvelope mix = new(Rate);
    private int keepGamePercent = 82;
    private int voicePercent = 85;
    private int atmosphereMode = (int)AtmosphereMixMode.Legacy;
    private WaveOutEvent? output;
    private readonly object lifecycleGate = new();

    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(Rate, Channels);
    public GameAudioBufferStats AudioBufferStatistics => game.Statistics;

    public void SetAtmosphereMix(int keepGame, int voice)
    {
        Volatile.Write(ref keepGamePercent, Math.Clamp(keepGame, 35, 100));
        Volatile.Write(ref voicePercent, Math.Clamp(voice, 0, 100));
    }

    public void SetAtmosphereMode(AtmosphereMixMode mode)
    {
        if (!Enum.IsDefined(mode)) mode = AtmosphereMixMode.Legacy;
        Volatile.Write(ref atmosphereMode, (int)mode);
    }

    /// <summary>Only opt-in, explicitly trusted spatial metadata may pan the NEXT Turkish line.</summary>
    public void SetNextSpeechPosition(float pan, float confidence)
    {
        lock (speechGate)
        {
            nextSpeechPan = pan;
            nextSpeechConfidence = confidence;
        }
    }

    public void Start()
    {
        lock (lifecycleGate)
        {
            if (output is not null) return;
            var player = new WaveOutEvent { DesiredLatency = 90, NumberOfBuffers = 3 };
            try
            {
                player.Init(new SampleToWaveProvider(this));
                player.Play();
                output = player;
            }
            catch
            {
                player.Dispose();
                throw;
            }
        }
    }

    /// <summary>Oyun PCM örneklerini alır; kaynak Remote Play'in gönderdiği kanallar / örnek hızıdır.</summary>
    public void PushGameAudio(short[] pcm, int channels, int sampleRate)
    {
        if (pcm.Length == 0 || channels is < 1 or > 2 || sampleRate is < 8000 or > 96000) return;
        // Giriş kanal ve hızı farklıysa basit lineer örnekleme ile 48 kHz stereo'ya dönüştür.
        var frames = pcm.Length / channels;
        if (frames == 0) return;
        var outFrames = (int)Math.Round((double)frames * Rate / sampleRate);
        if (outFrames <= 0 || outFrames > Rate) return;
        var converted = new float[outFrames * Channels];
        for (var i = 0; i < outFrames; i++)
        {
            var source = (double)i * sampleRate / Rate;
            var first = Math.Min((int)source, frames - 1);
            var next = Math.Min(first + 1, frames - 1);
            var ratio = (float)(source - first);
            for (var channel = 0; channel < Channels; channel++)
            {
                var sourceChannel = Math.Min(channel, channels - 1);
                var a = pcm[first * channels + sourceChannel] / 32768f;
                var b = pcm[next * channels + sourceChannel] / 32768f;
                converted[i * Channels + channel] = a + (b - a) * ratio;
            }
        }
        game.Write(converted);
    }

    /// <summary>
    /// Optional future model integration: caller supplies already-resampled,
    /// frame-aligned 48kHz stereo game + dialogue PCM. Missing or unsafe stems
    /// always preserve the original game sound. Standard PushGameAudio is untouched.
    /// Only a dedicated model worker should call this (never a live audio callback).
    /// </summary>
    public void PushSeparatedGameAudio(long frameId, float[] gamePcm, float[]? dialogueStem,
        float confidence, int ageMilliseconds, bool turkishDubActive)
    {
        if (gamePcm is null || gamePcm.Length == 0 || gamePcm.Length % Channels != 0 ||
            gamePcm.Length > Rate * Channels) return;
        var safe = new float[gamePcm.Length];
        var trustedStem = dialogueStem is null ? ReadOnlySpan<float>.Empty : dialogueStem;
        if (stemRouter.Process(frameId, gamePcm, trustedStem, safe,
                confidence, ageMilliseconds, turkishDubActive))
        {
            // Router validates frame order and the established safety gate.
            // Keep stereo side information and softly ramp dialogue attenuation.
            atmosphereStem.Process(gamePcm, trustedStem, safe,
                confidence, ageMilliseconds, turkishDubActive);
        }
        else
            atmosphereStem.Reset();
        game.Write(safe);
    }

    /// <summary>Yeni Türkçe cümle önceki sesin yerine geçer; arka plan oyun sesi kesilmez.</summary>
    public void Play(byte[] wav)
    {
        var sample = SpeechAudio.FromWav(wav);
        var total = (int)Math.Round((double)sample.Samples.Length * Rate / sample.SampleRate);
        if (total <= 0) return;
        var converted = new float[total * Channels];
        for (var i = 0; i < total; i++)
        {
            var source = (double)i * sample.SampleRate / Rate;
            var first = Math.Min((int)source, sample.Samples.Length - 1);
            var next = Math.Min(first + 1, sample.Samples.Length - 1);
            var ratio = (float)(source - first);
            var value = sample.Samples[first] + (sample.Samples[next] - sample.Samples[first]) * ratio;
            converted[i * 2] = converted[i * 2 + 1] = value;
        }
        lock (speechGate)
        {
            var gains = SpatialVoicePanner.Gains(nextSpeechPan, nextSpeechConfidence);
            nextSpeechPan = nextSpeechConfidence = 0f;
            // No extra resampler pass: placement affects only TTS, never PS5 ambience.
            for (var i = 0; i < converted.Length; i += Channels)
            {
                converted[i] *= gains.Left;
                converted[i + 1] *= gains.Right;
            }
            speech = converted;
            speechPosition = 0;
        }
    }

    public void Stop()
    {
        lock (speechGate)
        {
            speech = null;
            speechPosition = 0;
        }
    }

    public int Read(float[] buffer, int offset, int count)
    {
        var destination = buffer.AsSpan(offset, count);
        game.Read(destination);
        lock (speechGate)
        {
            // Preserve stereo position and ambience while a Turkish line plays.
            var keep = Volatile.Read(ref keepGamePercent) / 100f;
            var voiceLevel = Volatile.Read(ref voicePercent) / 100f;
            var selectedMode = (AtmosphereMixMode)Volatile.Read(ref atmosphereMode);
            for (var i = 0; i < count; i += Channels)
            {
                var speaking = speech is { } voice && speechPosition < voice.Length;
                var left = speaking ? speech![speechPosition] : 0f;
                var right = speaking && speechPosition + 1 < speech!.Length ? speech![speechPosition + 1] : left;
                if (speaking)
                {
                    speechPosition += Channels;
                    if (speechPosition >= speech!.Length)
                    {
                        speech = null;
                        speechPosition = 0;
                    }
                }
                var mixed = mix.Step(destination[i], i + 1 < count ? destination[i + 1] : 0f,
                    left, right, keep, voiceLevel, selectedMode);
                destination[i] = mixed.Left;
                if (i + 1 < count) destination[i + 1] = mixed.Right;
            }
        }
        return count;
    }

    public void Dispose()
    {
        lock (lifecycleGate)
        {
            output?.Stop();
            output?.Dispose();
            output = null;
        }
        Stop();
        game.Clear();
        stemRouter.Reset();
        atmosphereStem.Reset();
        lock (speechGate) mix.Reset();
    }
}
