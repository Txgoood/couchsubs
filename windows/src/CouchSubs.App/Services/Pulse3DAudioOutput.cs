// SPDX-License-Identifier: AGPL-3.0-only

using System.IO;
using CouchSubs.Core.Dubbing;
using CouchSubs.Shell;
using NAudio.Wave;

namespace CouchSubs.App.Services;

/// <summary>
/// PS5 Remote Play sesini ve Türkçe dublajı Windows varsayılan çıkışına miksler.
/// PULSE 3D USB adaptörü bilgisayara takılı ve Windows varsayılan çıkışı seçili olmalıdır.
/// 48 kHz stereo PCM akışı; tampon dolarsa eski ses atılır, sessizlikte takılmaz.
/// </summary>
internal sealed class Pulse3DAudioOutput : IAudioOutput, ISampleProvider, IDisposable
{
    private const int Rate = 48000;
    private const int Channels = 2;
    private readonly GameAudioBuffer game = new(Rate * Channels / 4); // 250 ms max
    private readonly object speechGate = new();
    private float[]? speech;
    private int speechPosition;
    private WaveOutEvent? output;
    private readonly object lifecycleGate = new();

    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(Rate, Channels);

    public void Start()
    {
        lock (lifecycleGate)
        {
            if (output is not null) return;
            var player = new WaveOutEvent { DesiredLatency = 90, NumberOfBuffers = 3 };
            try
            {
                player.Init(this);
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
        // WaveOutEvent 16-bit stereo ya da float stereo fark etmeksizin sample provider'ı okur.
        game.Read(destination);
        lock (speechGate)
        {
            if (speech is { } voice)
            {
                var remaining = voice.Length - speechPosition;
                var mixed = Math.Min(count, remaining);
                // Ducking yalnız konuşma örneklerinin bulunduğu yerde uygulanır.
                for (var i = 0; i < mixed; i++)
                {
                    var spoken = voice[speechPosition + i];
                    var gain = MathF.Abs(spoken) > 0.0001f ? 0.35f : 1f;
                    destination[i] = Math.Clamp(destination[i] * gain + spoken, -1f, 1f);
                }
                speechPosition += mixed;
                if (speechPosition >= voice.Length)
                {
                    speech = null;
                    speechPosition = 0;
                }
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
    }
}
