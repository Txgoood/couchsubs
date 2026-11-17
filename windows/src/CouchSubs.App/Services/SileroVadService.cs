// SPDX-License-Identifier: AGPL-3.0-only
using System.IO;
using System.Threading.Channels;
using CouchSubs.Shell;
using Microsoft.ML.OnnxRuntime;

namespace CouchSubs.App.Services;

/// <summary>
/// Optional local Silero v6.2 ONNX speech-activity detector (MIT).
/// Important: detects likely SPEECH, not anger, voice identity, or separated dialogue.
/// Processes PS5 game audio in a bounded background queue, never on the native audio callback.
/// </summary>
internal sealed class SileroVadService(string? overrideModelPath = null) : INeuralSpeechDetector
{
    public static string ModelPath => Path.Combine(AppContext.BaseDirectory, "vad", "silero_vad.onnx");
    public static bool IsBundled => File.Exists(ModelPath);
    private string LocalModelPath => overrideModelPath ?? ModelPath;
    private readonly object lifecycle = new();
    private CancellationTokenSource? cancellation;
    private Channel<AudioPacket>? incoming;
    private Task? worker;
    private long lastSpeechTicks;
    private long lastEvaluationTicks;
    private int generation;

    private readonly record struct AudioPacket(short[] Pcm, int Channels, int Rate);

    public bool Ready => Interlocked.Read(ref lastEvaluationTicks) > 0;
    public bool RecentSpeech => Ready &&
        Environment.TickCount64 - Interlocked.Read(ref lastSpeechTicks) <= 420 &&
        Environment.TickCount64 - Interlocked.Read(ref lastEvaluationTicks) <= 1500;

    public void Start()
    {
        lock (lifecycle)
        {
            if (cancellation is not null || !File.Exists(LocalModelPath)) return;
            Interlocked.Exchange(ref lastSpeechTicks, 0);
            Interlocked.Exchange(ref lastEvaluationTicks, 0);
            var token = new CancellationTokenSource();
            var channel = Channel.CreateBounded<AudioPacket>(new BoundedChannelOptions(6)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.DropOldest
            });
            incoming = channel;
            cancellation = token;
            var current = ++generation;
            worker = Task.Run(() => AnalyzeAsync(channel.Reader, token.Token, current));
        }
    }

    public void Push(short[] pcm, int channels, int rate)
    {
        // Supported Remote Play variants: 16 kHz or 48 kHz.
        // Data already has its own fresh allocation from StreamSession.OnAudioPcm.
        if (channels is < 1 or > 2 || (rate != 16000 && rate != 48000) ||
            pcm.Length == 0 || pcm.Length > 96000) return;
        incoming?.Writer.TryWrite(new AudioPacket(pcm, channels, rate));
    }

    public void Stop()
    {
        lock (lifecycle)
        {
            var token = cancellation;
            generation++;
            cancellation = null;
            var channel = incoming;
            incoming = null;
            channel?.Writer.TryComplete();
            token?.Cancel();
            // Never join the worker on the UI or audio callback thread.
            if (worker is { } closing)
                _ = closing.ContinueWith(_ => token?.Dispose(), TaskScheduler.Default);
            else token?.Dispose();
            worker = null;
            Interlocked.Exchange(ref lastSpeechTicks, 0);
            Interlocked.Exchange(ref lastEvaluationTicks, 0);
        }
    }

    private async Task AnalyzeAsync(ChannelReader<AudioPacket> reader, CancellationToken cancellationToken, int current)
    {
        try
        {
            using var options = new SessionOptions
            {
                InterOpNumThreads = 1,
                IntraOpNumThreads = 1,
                ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
            };
            using var session = new InferenceSession(LocalModelPath, options);
            // Silero model state: [2,1,128], plus 64 preceding samples of context.
            float[] state = new float[256];
            float[] context = new float[64];
            float[] chunk = new float[512];
            float[] modelInput = new float[576];
            int filled = 0, rateBefore = 0, hits = 0;
            long[] sampleRate = [16000];
            using var runOptions = new RunOptions();
            while (await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                while (reader.TryRead(out var packet))
                {
                    if (packet.Rate != rateBefore && rateBefore != 0)
                    {
                        Array.Clear(state);
                        Array.Clear(context);
                        filled = hits = 0;
                    }
                    rateBefore = packet.Rate;
                    var stride = packet.Rate / 16000;
                    var frames = packet.Pcm.Length / packet.Channels;
                    for (int frame = 0; frame < frames; frame += stride)
                    {
                        // Simple anti-alias 3-tap box filter for 48k -> 16k analysis.
                        // Downmix both channels; the original game PCM is never modified.
                        float sum = 0f;
                        var gathered = 0;
                        for (int tap = 0; tap < stride && frame + tap < frames; tap++)
                        {
                            var at = (frame + tap) * packet.Channels;
                            sum += packet.Channels == 2
                                ? (packet.Pcm[at] + packet.Pcm[at + 1]) / 65536f
                                : packet.Pcm[at] / 32768f;
                            gathered++;
                        }
                        float mono = sum / gathered;
                        chunk[filled++] = mono;
                        if (filled < 512) continue;
                        Array.Copy(context, 0, modelInput, 0, 64);
                        Array.Copy(chunk, 0, modelInput, 64, 512);
                        using var input = OrtValue.CreateTensorValueFromMemory(modelInput, [1L, 576L]);
                        using var stateInput = OrtValue.CreateTensorValueFromMemory(state, [2L, 1L, 128L]);
                        using var srInput = OrtValue.CreateTensorValueFromMemory(sampleRate, Array.Empty<long>());
                        using var outputs = session.Run(runOptions,
                            ["input", "state", "sr"], [input, stateInput, srInput], ["output", "stateN"]);
                        float probability = outputs[0].GetTensorDataAsSpan<float>()[0];
                        outputs[1].GetTensorDataAsSpan<float>().CopyTo(state);
                        Array.Copy(modelInput, 512, context, 0, 64);
                        filled = 0;
                        if (cancellationToken.IsCancellationRequested || current != Volatile.Read(ref generation))
                            return; // Ignore results from a prior PS5 connection.
                        var now = Environment.TickCount64;
                        Interlocked.Exchange(ref lastEvaluationTicks, now);
                        if (probability >= 0.62f)
                        {
                            hits = Math.Min(6, hits + 1);
                            if (hits >= 3)
                                Interlocked.Exchange(ref lastSpeechTicks, now);
                        }
                        else hits = Math.Max(0, hits - 1);
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            // Missing/corrupt/unsupported model must NEVER interrupt gameplay or dubbing.
            Interlocked.Exchange(ref lastSpeechTicks, 0);
            Interlocked.Exchange(ref lastEvaluationTicks, 0);
        }
    }
}
