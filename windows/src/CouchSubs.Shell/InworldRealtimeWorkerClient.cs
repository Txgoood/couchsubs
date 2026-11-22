// SPDX-License-Identifier: AGPL-3.0-only
using System.Diagnostics;
using System.Text;
using System.Threading.Channels;
using CouchSubs.Core.Dubbing;

namespace CouchSubs.Shell;

/// <summary>
/// EXPLICIT-OPT-IN supervised subprocess client for the genuine Inworld
/// streaming model. No PS5 audio callback may await this process.
/// This client only returns aligned stem metadata. A future caller must
/// own the original-audio 90ms deadline and safe output fallback.
/// </summary>
public sealed class InworldRealtimeWorkerClient : IAsyncDisposable
{
    public const int MaxOutstandingWrites = 8;
    private readonly Channel<byte[]> writes =
        Channel.CreateBounded<byte[]>(new BoundedChannelOptions(MaxOutstandingWrites)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });
    private readonly CancellationTokenSource shutdown = new();
    private readonly Action<long, float[]> onStem;
    private Process? process;
    private Task? writer;
    private Task? reader;
    private Task? stderrDrain;
    private int ready;
    private int disposed;

    public InworldRealtimeWorkerClient(Action<long, float[]> onStem) =>
        this.onStem = onStem ?? throw new ArgumentNullException(nameof(onStem));

    public bool Ready => Volatile.Read(ref ready) == 1;
    public string? Failure { get; private set; }

    /// <summary>
    /// Only call explicitly from a background service start, after selecting
    /// an installed Python 3.10+ with inworld-voice-isolation==1.0.0.
    /// Not invoked by the default CouchSubs startup or TTS.
    /// </summary>
    public async Task StartAsync(string pythonExe, string workerScript,
        string device = "cpu", CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref disposed) != 0)
            throw new ObjectDisposedException(nameof(InworldRealtimeWorkerClient));
        if (process is not null)
            throw new InvalidOperationException("Worker already started");
        if (!File.Exists(workerScript) || !Path.IsPathFullyQualified(workerScript))
            throw new FileNotFoundException("Explicit worker path required", workerScript);
        if (device is not ("cpu" or "cuda"))
            throw new ArgumentOutOfRangeException(nameof(device));
        if (string.IsNullOrWhiteSpace(pythonExe))
            throw new ArgumentException("Python executable is required", nameof(pythonExe));
        var start = new ProcessStartInfo
        {
            FileName = pythonExe, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add(workerScript);
        start.ArgumentList.Add("--stdio");
        start.ArgumentList.Add("--device");
        start.ArgumentList.Add(device);
        process = Process.Start(start) ??
            throw new InvalidOperationException("Python worker did not start");
        // Drain stderr before model initialization: large Python/torch native
        // error logs must not block the child before it can emit READY.
        var launched = process;
        stderrDrain = Task.Run(async () =>
        {
            try
            {
                var buffer = new char[512];
                while (await launched.StandardError.ReadAsync(
                    buffer.AsMemory(), shutdown.Token).ConfigureAwait(false) > 0) { }
            }
            catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { }
            catch (IOException) { } // Child terminated; main reader reports it.
        });
        try
        {
            // No StreamReader here: its buffering can consume binary
            // model frames after READY and corrupt 20ms alignment.
            using var handshake = CancellationTokenSource.CreateLinkedTokenSource(
                shutdown.Token, cancellationToken);
            handshake.CancelAfter(TimeSpan.FromSeconds(120));
            var marker = new byte[6];
            await ReadFullyAsync(process.StandardOutput.BaseStream,
                marker, handshake.Token).ConfigureAwait(false);
            if (Encoding.ASCII.GetString(marker) != "READY\n")
                throw new InvalidDataException("Worker handshake mismatch");
            Volatile.Write(ref ready, 1);
            writer = Task.Run(() => WriteLoopAsync(shutdown.Token));
            reader = Task.Run(() => ReadLoopAsync(shutdown.Token));
        }
        catch
        {
            Volatile.Write(ref ready, 0);
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            process.Dispose();
            process = null;
            throw;
        }
    }

    /// <summary>Nonblocking writer from a worker/capture callback.</summary>
    public bool TryQueue(long frameId, ReadOnlySpan<float> originalStereo)
    {
        if (!Ready || Volatile.Read(ref disposed) != 0) return false;
        var packet = InworldStemWireProtocol.EncodeInput(frameId, originalStereo);
        return writes.Writer.TryWrite(packet);
    }

    private async Task WriteLoopAsync(CancellationToken ct)
    {
        try
        {
            var target = process!.StandardInput.BaseStream;
            await foreach (var packet in writes.Reader.ReadAllAsync(ct))
            {
                await target.WriteAsync(packet, ct).ConfigureAwait(false);
                await target.FlushAsync(ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception error) { Fail(error); }
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        try
        {
            var source = process!.StandardOutput.BaseStream;
            var packet = new byte[InworldStemWireProtocol.OutputBytes];
            while (!ct.IsCancellationRequested)
            {
                await ReadFullyAsync(source, packet, ct).ConfigureAwait(false);
                if (!InworldStemWireProtocol.TryDecodeOutput(packet, out var frameId, out var speech))
                    throw new InvalidDataException("Invalid or truncated model output");
                onStem(frameId, speech);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception error) { Fail(error); }
    }

    private static async Task ReadFullyAsync(Stream stream, byte[] packet,
        CancellationToken ct)
    {
        var position = 0;
        while (position < packet.Length)
        {
            var count = await stream.ReadAsync(packet.AsMemory(position), ct)
                .ConfigureAwait(false);
            if (count == 0) throw new EndOfStreamException("Inworld process stopped");
            position += count;
        }
    }

    private void Fail(Exception error)
    {
        Failure = error.GetType().Name + ": " + error.Message;
        Volatile.Write(ref ready, 0);
        writes.Writer.TryComplete(error);
        shutdown.Cancel();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        Volatile.Write(ref ready, 0);
        writes.Writer.TryComplete();
        shutdown.Cancel();
        if (process is { } p)
        {
            try { if (!p.HasExited) p.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            if (writer is not null) try { await writer.ConfigureAwait(false); }
                catch (OperationCanceledException) { }
            if (reader is not null) try { await reader.ConfigureAwait(false); }
                catch (OperationCanceledException) { }
            if (stderrDrain is not null) try { await stderrDrain.ConfigureAwait(false); }
                catch (OperationCanceledException) { }
            p.Dispose();
            process = null;
        }
        shutdown.Dispose();
    }
}
