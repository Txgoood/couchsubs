// SPDX-License-Identifier: AGPL-3.0-only
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using NAudio.Wave;

namespace CouchSubs.App.Services;

/// <summary>Bounded loopback of the default Windows playback device. No microphone input.</summary>
internal sealed class TranslationClipAudio
{
    private readonly object gate = new();
    private readonly Queue<Packet> packets = new();
    private WasapiLoopbackCapture? capture;
    private PcmFormat format;
    private bool running;
    private long lastEnd;

    internal readonly record struct PcmFormat(int SampleRate, int Channels)
    {
        public int BlockAlign => Channels * sizeof(short);
        public long AtMilliseconds(long milliseconds) => milliseconds * SampleRate / 1000;
    }

    internal sealed record Packet(long Start, byte[] Pcm);

    internal sealed record Track(PcmFormat Format, Packet[] Packets)
    {
        public byte[]? Align(long startMs, int durationMs)
        {
            var start = Format.AtMilliseconds(startMs);
            var length = (int)Format.AtMilliseconds(durationMs);
            byte[]? result = null;
            foreach (var packet in Packets)
            {
                var first = Math.Max(start, packet.Start);
                var end = Math.Min(start + length, packet.Start + packet.Pcm.Length / Format.BlockAlign);
                if (end <= first) continue;
                result ??= new byte[length * Format.BlockAlign];
                Buffer.BlockCopy(packet.Pcm, (int)(first - packet.Start) * Format.BlockAlign,
                    result, (int)(first - start) * Format.BlockAlign, (int)(end - first) * Format.BlockAlign);
            }
            return result;
        }
    }

    public bool IsRunning { get { lock (gate) return running; } }

    public Track Snapshot()
    {
        lock (gate) return new Track(format, packets.ToArray());
    }

    public void Arm(bool enabled)
    {
        WasapiLoopbackCapture? previous;
        lock (gate)
        {
            previous = capture;
            capture = null;
            running = false;
            packets.Clear();
            lastEnd = 0;
        }
        previous?.Dispose();
        if (!enabled) return;

        WasapiLoopbackCapture? next = null;
        try
        {
            next = new WasapiLoopbackCapture();
            var input = next.WaveFormat;
            if (input is WaveFormatExtensible extensible) input = extensible.ToStandardWaveFormat();
            if (!IsSupported(input)) return;

            var output = new PcmFormat(input.SampleRate, Math.Min(input.Channels, 2));
            next.DataAvailable += (sender, args) =>
            {
                var at = Environment.TickCount64;
                if (args.BytesRecorded >= input.BlockAlign)
                    Add((WasapiLoopbackCapture)sender!, output, at, ConvertToPcm(args, input, output));
            };
            next.RecordingStopped += (sender, _) =>
            {
                lock (gate)
                    if (ReferenceEquals(capture, sender)) running = false;
            };

            lock (gate)
            {
                capture = next;
                format = output;
                running = true;
            }
            next.StartRecording();
            next = null;
        }
        catch (Exception error) when (error is COMException or InvalidOperationException or NotSupportedException or ArgumentException)
        {
            lock (gate)
            {
                capture = null;
                running = false;
            }
        }
        finally { next?.Dispose(); }
    }

    private void Add(WasapiLoopbackCapture sender, PcmFormat output, long at, byte[] pcm)
    {
        lock (gate)
        {
            if (!ReferenceEquals(capture, sender)) return;
            var frames = pcm.Length / output.BlockAlign;
            var latest = output.AtMilliseconds(at);
            var start = latest - frames;
            // Use a continuous sample clock across callback jitter, but leave real pauses silent.
            if (Math.Abs(lastEnd - start) <= output.AtMilliseconds(60)) start = lastEnd;
            if (start < lastEnd) start = lastEnd;
            lastEnd = start + frames;
            packets.Enqueue(new Packet(start, pcm));
            var oldest = latest - output.AtMilliseconds(21_000);
            while (packets.TryPeek(out var packet) && packet.Start < oldest)
                packets.Dequeue();
        }
    }

    private static bool IsSupported(WaveFormat input) => input.Channels > 0 && input.SampleRate > 0 &&
        (input.Encoding == WaveFormatEncoding.IeeeFloat && input.BitsPerSample == 32 ||
         input.Encoding == WaveFormatEncoding.Pcm && input.BitsPerSample is 16 or 24 or 32);

    private static byte[] ConvertToPcm(WaveInEventArgs data, WaveFormat input, PcmFormat output)
    {
        var frameCount = data.BytesRecorded / input.BlockAlign;
        var pcm = new byte[frameCount * output.BlockAlign];
        var sampleBytes = input.BitsPerSample / 8;
        if (input.Encoding == WaveFormatEncoding.Pcm && sampleBytes == 2 && input.Channels == output.Channels)
        {
            Buffer.BlockCopy(data.Buffer, 0, pcm, 0, pcm.Length);
            return pcm;
        }

        for (var frame = 0; frame < frameCount; frame++)
        {
            var source = frame * input.BlockAlign;
            // Mix additional speaker channels into both sides so dialogue in a center channel is audible.
            var extra = 0;
            for (var channel = output.Channels; channel < input.Channels; channel++)
                extra += Sample(data.Buffer.AsSpan(source + channel * sampleBytes, sampleBytes), input.Encoding);
            for (var channel = 0; channel < output.Channels; channel++)
            {
                var value = Sample(data.Buffer.AsSpan(source + channel * sampleBytes, sampleBytes), input.Encoding);
                if (input.Channels > output.Channels) value = (2 * value + extra / (input.Channels - output.Channels)) / 3;
                BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(frame * output.BlockAlign + channel * 2), (short)value);
            }
        }
        return pcm;
    }

    private static int Sample(ReadOnlySpan<byte> sample, WaveFormatEncoding encoding)
    {
        if (encoding == WaveFormatEncoding.IeeeFloat)
        {
            var value = BinaryPrimitives.ReadSingleLittleEndian(sample);
            return float.IsFinite(value) ? (int)(Math.Clamp(value, -1f, 1f) * short.MaxValue) : 0;
        }
        return BinaryPrimitives.ReadInt16LittleEndian(sample[^2..]);
    }
}
