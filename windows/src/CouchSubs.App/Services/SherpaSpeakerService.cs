// SPDX-License-Identifier: AGPL-3.0-only
using System.IO;
using System.Diagnostics;
using System.Threading.Channels;
using CouchSubs.Core.Dubbing;
using CouchSubs.Shell;
using SherpaOnnx;

namespace CouchSubs.App.Services;

/// <summary>
/// Experimental LOCAL voice-embedding identity for anonymous game characters.
/// Requires Silero VAD confirmation: game soundtrack and effects are unseparated.
/// Inference runs on a bounded worker, never on PS5 audio callback/UI.
/// </summary>
internal sealed class SherpaSpeakerService(string? overrideModelPath = null) : IAutomaticSpeakerDetector
{
    public static string ModelPath => Path.Combine(AppContext.BaseDirectory, "speaker",
        "3dspeaker_speech_eres2net_sv_en_voxceleb_16k.onnx");
    public static bool IsBundled => File.Exists(ModelPath);
    private string LocalModelPath => overrideModelPath ?? ModelPath;
    private int modelReady;
    public bool ModelReady => Volatile.Read(ref modelReady) == 1;
    private readonly object gate = new();
    private readonly AiPerformanceMetrics performance = new();
    public AiPerformanceSnapshot Statistics => performance.Snapshot();
    private readonly HybridSpeakerRegistry identities = new();
    private Channel<AudioPacket>? channel;
    private CancellationTokenSource? cancellation;
    private Task? worker;
    private SpeakerIdentityResult latest = new(null, SpeakerIdentitySource.Uncertain, 0);
    private long latestTicks;
    private int generation;
    private readonly record struct AudioPacket(short[] Data, int Channels, int Rate, bool Speech);

    public SpeakerIdentityResult Latest
    {
        get
        {
            lock (gate)
                return Environment.TickCount64 - latestTicks < 1200
                    ? latest : new SpeakerIdentityResult(null, SpeakerIdentitySource.Uncertain, 0);
        }
    }

    public void Start()
    {
        // Second fail-closed gate if a different caller constructs this service.
        // Intentionally do not call any Sherpa native API in this process.
        if (!NativeSpeakerSafetyPolicy.InProcessInferenceAllowed) return;
        lock (gate)
        {
            if (cancellation is not null || !File.Exists(LocalModelPath)) return;
            performance.Reset();
            latest = new(null, SpeakerIdentitySource.Uncertain, 0);
            latestTicks = 0;
            Interlocked.Exchange(ref modelReady, 0);
            identities.Reset();
            cancellation = new CancellationTokenSource();
            var current = ++generation;
            channel = Channel.CreateBounded<AudioPacket>(new BoundedChannelOptions(8)
            {
                SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.DropOldest
            });
            worker = Task.Run(() => RunAsync(channel.Reader, cancellation.Token, current));
        }
    }

    public void Push(short[] pcm, int channels, int rate, bool speechConfirmed)
    {
        if (channels is < 1 or > 2 || (rate != 16000 && rate != 48000) ||
            pcm.Length is < 1 or > 96000) return;
        if (speechConfirmed) performance.RecordPacket();
        channel?.Writer.TryWrite(new AudioPacket(pcm, channels, rate, speechConfirmed));
    }

    public void Stop()
    {
        lock (gate)
        {
            generation++;
            channel?.Writer.TryComplete();
            channel = null;
            cancellation?.Cancel();
            var token = cancellation;
            if (worker is { } previous)
                _ = previous.ContinueWith(_ => token?.Dispose(), TaskScheduler.Default);
            else token?.Dispose();
            worker = null;
            cancellation = null;
            latest = new(null, SpeakerIdentitySource.Uncertain, 0);
            latestTicks = 0;
            identities.Reset();
            Interlocked.Exchange(ref modelReady, 0);
        }
    }

    private async Task RunAsync(ChannelReader<AudioPacket> reader, CancellationToken cancel, int gen)
    {
        try
        {
            var cfg = new SpeakerEmbeddingExtractorConfig
            {
                Model = LocalModelPath, NumThreads = 1, Debug = 0, Provider = "cpu"
            };
            using var extractor = new SpeakerEmbeddingExtractor(cfg);
            if (extractor.Dim <= 0) return;
            Interlocked.Exchange(ref modelReady, 1);
            var samples = new float[32000]; // 2s at 16 kHz, no raw audio persisted
            var filled = 0;
            while (await reader.WaitToReadAsync(cancel).ConfigureAwait(false))
            {
                while (reader.TryRead(out var packet))
                {
                    if (!packet.Speech) { filled = 0; continue; }
                    var frames = packet.Data.Length / packet.Channels;
                    var stride = packet.Rate / 16000;
                    for (var frame = 0; frame < frames; frame += stride)
                    {
                        float sum = 0;
                        var count = 0;
                        for (var tap = 0; tap < stride && frame + tap < frames; tap++)
                        {
                            var pos = (frame + tap) * packet.Channels;
                            sum += packet.Channels == 2
                                ? (packet.Data[pos] + packet.Data[pos + 1]) / 65536f
                                : packet.Data[pos] / 32768f;
                            count++;
                        }
                        samples[filled++] = sum / count;
                        if (filled != samples.Length) continue;
                        var stream = extractor.CreateStream();
                        try
                        {
                            stream.AcceptWaveform(16000, samples);
                            stream.InputFinished();
                            if (extractor.IsReady(stream))
                            {
                                var started = Stopwatch.GetTimestamp();
                                var embedding = extractor.Compute(stream);
                                performance.RecordInference(started);
                                if (cancel.IsCancellationRequested || gen != Volatile.Read(ref generation)) return;
                                // Model confidence isn't calibrated: only repeated, unambiguous
                                // cosine matches are admitted by HybridSpeakerRegistry.
                                lock (gate)
                                {
                                    if (gen != generation) return;
                                    var match = identities.Resolve(new SpeakerEvidence(
                                        null, embedding, 0.95f, true, true));
                                    if (match.Name is not null)
                                    {
                                        latest = match;
                                        latestTicks = Environment.TickCount64;
                                    }
                                }
                            }
                        }
                        finally { stream.Dispose(); }
                        filled = 0;
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            performance.RecordError();
            lock (gate)
            {
                if (gen == generation)
                {
                    latest = new(null, SpeakerIdentitySource.Uncertain, 0);
                    latestTicks = 0;
                }
            }
        }
    }
}
