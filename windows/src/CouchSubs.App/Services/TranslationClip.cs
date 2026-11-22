// SPDX-License-Identifier: AGPL-3.0-only
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CouchSubs.Core;

namespace CouchSubs.App.Services;

internal sealed class TranslationClip
{
    private const int Fps = 5;
    private const int Seconds = 20;
    private const int Width = 1280;
    private const int Height = 720;
    private const int MaxEntries = 200;
    private static long Now => Environment.TickCount64;
    private readonly object gate = new();
    private readonly Queue<(long At, byte[] Image)> frames = new();
    private readonly Queue<Entry> entries = new();
    private sealed record Entry(long At, string Kind, string English, string Turkish, string? Speaker);
    private int generation;
    private bool armed;
    private bool sampling;

    public void Arm(bool value)
    {
        lock (gate)
        {
            armed = value;
            generation++;
            frames.Clear();
            entries.Clear();
        }
    }

    public void Add(string kind, string english, string turkish, string? speaker = null)
    {
        lock (gate)
        {
            if (!armed) return;
            entries.Enqueue(new Entry(Now, kind, english, turkish, speaker));
            while (entries.Count > MaxEntries) entries.Dequeue();
        }
    }

    public async Task SampleAsync(Frame frame)
    {
        int version;
        var at = Now;
        lock (gate)
        {
            if (!armed || sampling) return;
            sampling = true;
            version = generation;
        }
        try
        {
            var image = await Task.Run(() => Encode(frame)).ConfigureAwait(false);
            lock (gate)
            {
                if (version != generation) return;
                frames.Enqueue((at, image));
                while (frames.Count > Fps * Seconds || frames.Peek().At < at - Seconds * 1000L)
                    frames.Dequeue();
            }
        }
        finally
        {
            lock (gate) sampling = false;
        }
    }

    public async Task<string?> SaveAsync(string root)
    {
        (long At, byte[] Image)[] samples;
        Entry[] text;
        lock (gate)
        {
            samples = frames.ToArray();
            text = entries.ToArray();
        }
        if (samples.Length == 0) return null;
        return await Task.Run(() =>
        {
            var start = samples[0].At;
            var end = samples[^1].At + 1000 / Fps;
            var directory = Path.Combine(root, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss-fff") +
                "-" + Guid.NewGuid().ToString("N")[..6]);
            Directory.CreateDirectory(directory);
            var index = 0;
            var video = new List<byte[]>();
            for (var time = start; time < end; time += 1000 / Fps)
            {
                while (index + 1 < samples.Length && samples[index + 1].At <= time) index++;
                video.Add(samples[index].Image);
            }
            WriteAvi(Path.Combine(directory, "oyun.avi"), video);
            var report = text.Where(e => e.At >= start && e.At < end).ToArray();
            File.WriteAllLines(Path.Combine(directory, "ceviri.jsonl"),
                report.Select(e => JsonSerializer.Serialize(new
                {
                    timeMs = e.At - start, e.Kind, e.English, e.Turkish, e.Speaker
                })));
            File.WriteAllLines(Path.Combine(directory, "ceviri.txt"), report.Select(e =>
                $"[{TimeSpan.FromMilliseconds(e.At - start):mm\\:ss\\.fff}] " +
                (e.Kind == "subtitle" ? "Altyazı" : "Dublaj başlangıcı") +
                (e.Speaker is { Length: > 0 } speaker ? $" · {speaker}" : "") +
                $"{Environment.NewLine}EN: {e.English}{Environment.NewLine}TR: {e.Turkish}{Environment.NewLine}"));
            return directory;
        }).ConfigureAwait(false);
    }

    private static byte[] Encode(Frame frame)
    {
        var source = BitmapSource.Create(frame.Width, frame.Height, 96, 96,
            PixelFormats.Bgr32, null, frame.Pixels, frame.Stride);
        var visual = new DrawingVisual();
        using (var draw = visual.RenderOpen())
        {
            draw.DrawRectangle(Brushes.Black, null, new System.Windows.Rect(0, 0, Width, Height));
            var scale = Math.Min((double)Width / frame.Width, (double)Height / frame.Height);
            var width = frame.Width * scale;
            var height = frame.Height * scale;
            draw.DrawImage(source, new System.Windows.Rect((Width - width) / 2, (Height - height) / 2, width, height));
        }
        var bitmap = new RenderTargetBitmap(Width, Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new JpegBitmapEncoder { QualityLevel = 65 };
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
    }

    private static void WriteAvi(string path, IReadOnlyList<byte[]> images)
    {
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        void Tag(string value) => writer.Write(Encoding.ASCII.GetBytes(value));
        long Begin(string tag)
        {
            Tag(tag);
            var offset = stream.Position;
            writer.Write(0);
            return offset;
        }
        void End(long offset)
        {
            var end = stream.Position;
            stream.Position = offset;
            writer.Write(checked((int)(end - offset - 4)));
            stream.Position = end;
            if ((end & 1) != 0) writer.Write((byte)0);
        }
        void Numbers(params int[] values)
        {
            foreach (var value in values) writer.Write(value);
        }

        var riff = Begin("RIFF");
        Tag("AVI ");
        var header = Begin("LIST");
        Tag("hdrl");
        var main = Begin("avih");
        var largest = images.Max(image => image.Length);
        Numbers(1_000_000 / Fps, largest * Fps, 0, 0x10, images.Count, 0, 1,
            largest, Width, Height, 0, 0, 0, 0);
        End(main);
        var streamList = Begin("LIST");
        Tag("strl");
        var streamHeader = Begin("strh");
        Tag("vids");
        Tag("MJPG");
        Numbers(0, 0, 0, 1, Fps, 0, images.Count, largest, -1, 0);
        writer.Write((short)0);
        writer.Write((short)0);
        writer.Write((short)Width);
        writer.Write((short)Height);
        End(streamHeader);
        var format = Begin("strf");
        Numbers(40, Width, Height);
        writer.Write((short)1);
        writer.Write((short)24);
        Tag("MJPG");
        Numbers(Width * Height * 3, 0, 0, 0, 0);
        End(format);
        End(streamList);
        End(header);
        var movie = Begin("LIST");
        var movieStart = stream.Position;
        Tag("movi");
        var offsets = new List<int>();
        foreach (var image in images)
        {
            offsets.Add(checked((int)(stream.Position - movieStart)));
            var chunk = Begin("00dc");
            writer.Write(image);
            End(chunk);
        }
        End(movie);
        var index = Begin("idx1");
        for (var i = 0; i < images.Count; i++)
        {
            Tag("00dc");
            Numbers(0x10, offsets[i], images[i].Length);
        }
        End(index);
        End(riff);
    }
}
