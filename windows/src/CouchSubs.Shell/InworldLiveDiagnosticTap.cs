// SPDX-License-Identifier: AGPL-3.0-only
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Channels;
using CouchSubs.Core.Dubbing;

namespace CouchSubs.Shell;

/// <summary>
/// Phase-1 real PS5 sample tap for the genuine Inworld streaming model.
/// Crucial: READ ONLY. Original PCM continues on the unchanged PULSE3D
/// mixing path regardless of worker stalls, errors and model output.
/// The tap is opt-in and only writes a local clip if separately requested.
/// This is a diagnostic precursor, NOT live English-voice suppression.
/// </summary>
public sealed class InworldLiveDiagnosticTap : IAsyncDisposable
{
    private readonly Channel<AudioChunk> input =
        Channel.CreateBounded<AudioChunk>(new BoundedChannelOptions(12)
        {
            SingleReader = true, SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });
    private readonly Ps5DialogueFrameSegmenter segmenter = new();
    private readonly CancellationTokenSource stopping = new();
    private readonly InworldRealtimeWorkerClient model;
    private readonly object metricsGate = new();
    private readonly (long Frame, long CapturedAt)[] pending = new (long, long)[64];
    private readonly long[] ageBuckets = new long[2001];
    private Task? consumer;
    private long chunksDropped;
    private long modelQueueRejected;
    private long stemsReceived;
    private long validChunks;
    private long unmatchedStems;
    private long rejectedCaptureChunks;
    private const int ClipSampleCount = 15 * 48000 * 2;
    private readonly string? clipPath = Environment.GetEnvironmentVariable("COUCHSUBS_INWORLD_LOCAL_GAME_CLIP");
    private short[]? clip;
    private int clipSamples;
    private bool clipWritten;
    private bool clipWriteFailed;
    private readonly long sessionStarted = Stopwatch.GetTimestamp();
    private GameAudioBufferStats? playbackStats;
    private int disposed;
    private readonly record struct AudioChunk(short[] Pcm, int Channels, int Rate, long CapturedAt);

    public InworldLiveDiagnosticTap()
    {
        model = new InworldRealtimeWorkerClient((id, stem) =>
        {
            // Actual model output is intentionally NOT routed to the game
            // mixer until trustworthy per-frame speech confidence and stable
            // total measured latency have been demonstrated.
            if (stem.Length != InworldStemWireProtocol.StemSamplesMono) return;
            Interlocked.Increment(ref stemsReceived);
            lock (metricsGate)
            {
                ref var entry = ref pending[id % pending.Length];
                if (entry.Frame != id || entry.CapturedAt == 0)
                {
                    unmatchedStems++;
                    return;
                }
                var age = Stopwatch.GetElapsedTime(entry.CapturedAt).TotalMilliseconds;
                ageBuckets[(int)Math.Min(ageBuckets.Length - 1, Math.Ceiling(age))]++;
                entry = default;
            }
        });
    }

    public bool Ready => model.Ready;
    public long ReceivedStems => Interlocked.Read(ref stemsReceived);
    public long DroppedCaptureChunks => Interlocked.Read(ref chunksDropped);
    public long RejectedModelFrames => Interlocked.Read(ref modelQueueRejected);
    public long ValidCaptureChunks => Interlocked.Read(ref validChunks);
    public string? Failure => model.Failure;

    internal void SetFinalPlaybackStats(GameAudioBufferStats? stats) => playbackStats = stats;

    public async Task StartAsync(string pythonExe, string scriptPath, string device,
        CancellationToken cancellationToken = default)
    {
        await model.StartAsync(pythonExe, scriptPath, device, cancellationToken)
            .ConfigureAwait(false);
        consumer = Task.Run(() => ConsumeAsync(stopping.Token));
    }

    /// <summary>
    /// Native Opus callback: bounded TryWrite only, no model or file IO,
    /// waits, locks, resampling or copying PCM. Audio is already a new array
    /// cloned by StreamSession.OnAudioPcm per native callback.
    /// </summary>
    public bool TryCapture(short[] pcm, int channels, int rate)
    {
        if (!Ready || Volatile.Read(ref disposed) != 0 || pcm.Length == 0)
            return false;
        if (!input.Writer.TryWrite(new AudioChunk(pcm, channels, rate, Stopwatch.GetTimestamp())))
        {
            Interlocked.Increment(ref chunksDropped);
            return false;
        }
        return true;
    }

    private async Task ConsumeAsync(CancellationToken token)
    {
        if (!string.IsNullOrWhiteSpace(clipPath) && Path.IsPathFullyQualified(clipPath) &&
            !File.Exists(clipPath))
            clip = new short[ClipSampleCount];
        try
        {
            await foreach (var chunk in input.Reader.ReadAllAsync(token))
            {
                if (segmenter.Append(chunk.Pcm, chunk.Channels, chunk.Rate,
                    (id, frame) =>
                    {
                        lock (metricsGate)
                            pending[id % pending.Length] = (id, chunk.CapturedAt);
                        if (!model.TryQueue(id, frame))
                        {
                            lock (metricsGate)
                                pending[id % pending.Length] = default;
                            Interlocked.Increment(ref modelQueueRejected);
                        }
                    }))
                {
                    Interlocked.Increment(ref validChunks);
                    CaptureClip(chunk.Pcm);
                }
                else
                    Interlocked.Increment(ref rejectedCaptureChunks);
                // Unlike the game mix, dropped diagnostic input causes NO
                // game-audio artifact: this tap is observation only.
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    private void CaptureClip(short[] pcm)
    {
        if (clip is null) return;
        var count = Math.Min(pcm.Length, clip.Length - clipSamples);
        pcm.AsSpan(0, count).CopyTo(clip.AsSpan(clipSamples));
        clipSamples += count;
        if (clipSamples != clip.Length) return;

        var audio = clip;
        clip = null;
        if (DroppedCaptureChunks != 0 || Interlocked.Read(ref rejectedCaptureChunks) != 0)
        {
            clipWriteFailed = true;
            return;
        }

        try
        {
            WriteClip(audio);
            clipWritten = true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            clipWriteFailed = true;
        }
    }

    private void WriteClip(short[] audio)
    {
        using var stream = new FileStream(clipPath!, FileMode.CreateNew);
        using var writer = new BinaryWriter(stream);
        var bytes = MemoryMarshal.AsBytes(audio.AsSpan());
        writer.Write("RIFF"u8);
        writer.Write(36 + bytes.Length);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)2);
        writer.Write(48000);
        writer.Write(48000 * 4);
        writer.Write((short)4);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        input.Writer.TryComplete();
        stopping.Cancel();
        if (consumer is not null)
        {
            try { await consumer.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        await model.DisposeAsync().ConfigureAwait(false);
        stopping.Dispose();
        var path = Environment.GetEnvironmentVariable("COUCHSUBS_INWORLD_METRICS_PATH");
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) return;
        var measured = ageBuckets.Sum();
        var rank = (long)Math.Ceiling(measured * .95);
        int? p95 = null;
        for (var i = 0; i < ageBuckets.Length && measured > 0; i++)
        {
            rank -= ageBuckets[i];
            if (rank > 0) continue;
            p95 = i;
            break;
        }
        var report = JsonSerializer.Serialize(new
        {
            validCaptureChunks = ValidCaptureChunks,
            rejectedCaptureChunks,
            droppedCaptureChunks = DroppedCaptureChunks,
            rejectedModelFrames = RejectedModelFrames,
            receivedStems = ReceivedStems,
            unmatchedStems,
            measured,
            p95MsCappedAt2000 = p95,
            over90Ms = ageBuckets.Skip(91).Sum(),
            workerFailed = Failure is not null,
            sessionSeconds = Math.Round(Stopwatch.GetElapsedTime(sessionStarted).TotalSeconds, 1),
            playback = playbackStats,
            clipWritten,
            clipWriteFailed
        });
        await File.AppendAllTextAsync(path, report + Environment.NewLine).ConfigureAwait(false);
    }
}
