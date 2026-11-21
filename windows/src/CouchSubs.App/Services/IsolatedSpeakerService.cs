// SPDX-License-Identifier: AGPL-3.0-only
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using CouchSubs.Core.Dubbing;
using CouchSubs.Shell;

namespace CouchSubs.App.Services;

/// <summary>
/// Supervised, optional experimental speaker recognizer. Inference occurs
/// only inside the child CouchSubs.exe --speaker-worker process.
/// Audio callbacks enqueue bounded copies and never wait for inference.
/// </summary>
internal sealed class IsolatedSpeakerService : IAutomaticSpeakerDetector
{
    public static bool ExplicitlyEnabled =>
        string.Equals(Environment.GetEnvironmentVariable("COUCHSUBS_ENABLE_ISOLATED_SPEAKER"), "1", StringComparison.Ordinal);
    private readonly object gate = new();
    private readonly SpeakerWorkerCircuitBreaker breaker = new();
    private Channel<Packet>? queue;
    private CancellationTokenSource? shutdown;
    private Process? process;
    private Task? task;
    private SpeakerIdentityResult latest = new(null, SpeakerIdentitySource.Uncertain, 0);
    private long latestAt;
    private readonly record struct Packet(byte[] Pcm, int Channels, int Rate, bool Speech);
    public SpeakerIdentityResult Latest
    {
        get
        {
            lock (gate)
                return Environment.TickCount64 - latestAt < 1200
                    ? latest : new SpeakerIdentityResult(null, SpeakerIdentitySource.Uncertain, 0);
        }
    }

    public void Start()
    {
        if (!ExplicitlyEnabled || !SherpaSpeakerService.IsBundled) return;
        lock (gate)
        {
            if (shutdown is not null) return;
            breaker.ResetForNewSession();
            latest = new(null, SpeakerIdentitySource.Uncertain, 0);
            latestAt = 0;
            shutdown = new CancellationTokenSource();
            queue = Channel.CreateBounded<Packet>(new BoundedChannelOptions(4)
            {
                SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.DropOldest
            });
            var token = shutdown.Token;
            task = Task.Run(() => RunAsync(queue.Reader, token));
        }
    }

    public void Push(short[] pcm, int channels, int rate, bool speechConfirmed)
    {
        if (!ExplicitlyEnabled || channels is < 1 or > 2 ||
            (rate != 16000 && rate != 48000) || pcm.Length is < 1 or > 48000)
            return;
        var bytes = new byte[pcm.Length * sizeof(short)];
        Buffer.BlockCopy(pcm, 0, bytes, 0, bytes.Length);
        queue?.Writer.TryWrite(new Packet(bytes, channels, rate, speechConfirmed));
    }

    public void Stop()
    {
        CancellationTokenSource? cancel;
        Process? child;
        lock (gate)
        {
            queue?.Writer.TryComplete();
            queue = null;
            cancel = shutdown;
            shutdown = null;
            child = process;
            process = null;
            latest = new(null, SpeakerIdentitySource.Uncertain, 0);
            latestAt = 0;
        }
        cancel?.Cancel();
        Kill(child);
        // Do not block UI or PS5 callback while the background worker ends.
        if (cancel is not null)
        {
            var pending = task;
            _ = Task.Run(async () =>
            {
                try { if (pending is not null) await pending.ConfigureAwait(false); }
                catch (Exception) { }
                cancel.Dispose();
            });
        }
    }

    private async Task RunAsync(ChannelReader<Packet> reader, CancellationToken token)
    {
        Process? child = null;
        try
        {
            if (!breaker.CanSend) return;
            var exe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exe)) return;
            child = new Process
            {
                StartInfo = new ProcessStartInfo(exe, "--speaker-worker")
                {
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardInput = true, RedirectStandardOutput = true,
                    RedirectStandardError = false
                }
            };
            if (!child.Start()) { breaker.ReportFailure(); return; }
            lock (gate) process = child;
            using (var startup = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                startup.CancelAfter(SpeakerWorkerProtocol.StartupTimeout);
                var hello = await child.StandardOutput.ReadLineAsync(startup.Token).ConfigureAwait(false);
                if (hello is null || !SpeakerWorkerProtocol.TryReadStatus(Encoding.UTF8.GetBytes(hello), out var health) ||
                    !health.Ready) { breaker.ReportFailure(); return; }
            }
            while (!token.IsCancellationRequested && breaker.CanSend &&
                   await reader.WaitToReadAsync(token).ConfigureAwait(false))
            {
                if (!reader.TryRead(out var frame)) continue;
                try
                {
                    var line = JsonSerializer.Serialize(new
                    {
                        pcm = Convert.ToBase64String(frame.Pcm),
                        channels = frame.Channels,
                        rate = frame.Rate,
                        speech = frame.Speech
                    });
                    await child.StandardInput.WriteLineAsync(line.AsMemory(), token).ConfigureAwait(false);
                    await child.StandardInput.FlushAsync(token).ConfigureAwait(false);
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                    timeout.CancelAfter(SpeakerWorkerProtocol.ResponseTimeout);
                    var response = await child.StandardOutput.ReadLineAsync(timeout.Token).ConfigureAwait(false);
                    if (response is null || !SpeakerWorkerProtocol.TryReadStatus(Encoding.UTF8.GetBytes(response), out var state) ||
                        !state.Ready) { breaker.ReportFailure(); break; }
                    using var document = JsonDocument.Parse(response);
                    if (document.RootElement.TryGetProperty("speaker", out var speaker) &&
                        speaker.ValueKind == JsonValueKind.String)
                    {
                        var name = speaker.GetString();
                        if (name is { Length: > 0 and <= 64 })
                        {
                            lock (gate)
                            {
                                latest = new SpeakerIdentityResult(name, SpeakerIdentitySource.VoiceEmbedding, 0.9f);
                                latestAt = Environment.TickCount64;
                            }
                        }
                    }
                    breaker.ReportSuccess();
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    breaker.ReportFailure();
                    break; // Nonresponsive child is never reused.
                }
                catch (Exception) { breaker.ReportFailure(); break; }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception) { breaker.ReportFailure(); }
        finally
        {
            Kill(child);
            lock (gate)
            {
                if (ReferenceEquals(process, child)) process = null;
            }
        }
    }

    private static void Kill(Process? child)
    {
        if (child is null) return;
        try { if (!child.HasExited) child.Kill(entireProcessTree: true); }
        catch (Exception) { }
        try { child.Dispose(); }
        catch (Exception) { }
    }
}
