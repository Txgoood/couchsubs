// SPDX-License-Identifier: AGPL-3.0-only
using System.IO;
using System.Text.Json;
using CouchSubs.Core.Dubbing;
using SherpaOnnx;

namespace CouchSubs.App.Services;

/// <summary>
/// Runs ONLY in a dedicated child process launched with --speaker-worker.
/// All native Sherpa calls are isolated from the WPF/PS5/audio parent.
/// Input is bounded line-delimited JSON; no PCM or embeddings are persisted.
/// </summary>
internal static class IsolatedSpeakerWorker
{
    private sealed record Packet(string? Pcm, int Channels, int Rate, bool Speech,
        string? Game, string? RenameFrom, string? RenameTo, bool ForgetGame, string? ForgetSpeaker);
    private static void Reply(object value)
    {
        Console.Out.WriteLine(JsonSerializer.Serialize(value));
        Console.Out.Flush();
    }

    public static int Run()
    {
        // Never allow app startup paths, Velopack or WPF in the inference child.
        try
        {
            var model = SherpaSpeakerService.ModelPath;
            if (!File.Exists(model))
            {
                Reply(new { protocol = 1, ready = false, modelLoaded = false });
                return 2;
            }
            var config = new SpeakerEmbeddingExtractorConfig
            {
                Model = model, NumThreads = 1, Debug = 0, Provider = "cpu"
            };
            using var extractor = new SpeakerEmbeddingExtractor(config);
            if (extractor.Dim <= 0)
            {
                Reply(new { protocol = 1, ready = false, modelLoaded = false });
                return 3;
            }
            var registry = new HybridSpeakerRegistry();
            var memory = new GameSpeakerMemory(GameSpeakerMemory.DefaultDirectory);
            string? activeGame = null;
            var samples = new float[32000];
            var count = 0;
            Reply(new { protocol = 1, ready = true, modelLoaded = true });
            string? input;
            while ((input = Console.In.ReadLine()) is not null)
            {
                if (input.Length is 0 or > 150000) return 4;
                Packet? packet;
                try { packet = JsonSerializer.Deserialize<Packet>(input, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }); }
                catch (JsonException) { return 5; }
                if (packet is null) return 6;
                var requestedGame = GameSpeakerMemory.NormalizeGame(packet.Game);
                if (!string.Equals(activeGame, requestedGame, StringComparison.OrdinalIgnoreCase))
                {
                    activeGame = requestedGame;
                    registry.Reset();
                    memory.Load(activeGame, registry);
                    count = 0;
                }
                if (packet.ForgetGame)
                {
                    registry.Reset();
                    memory.Delete(activeGame);
                    Reply(new { protocol = 1, ready = true, modelLoaded = true });
                    continue;
                }
                if (packet.RenameFrom is not null && packet.RenameTo is not null)
                {
                    if (registry.Rename(packet.RenameFrom, packet.RenameTo))
                        memory.Save(activeGame, registry);
                    Reply(new { protocol = 1, ready = true, modelLoaded = true });
                    continue;
                }
                if (packet.ForgetSpeaker is not null)
                {
                    if (registry.Forget(packet.ForgetSpeaker))
                        memory.Save(activeGame, registry);
                    Reply(new { protocol = 1, ready = true, modelLoaded = true });
                    continue;
                }
                if (packet.Channels is < 1 or > 2 ||
                    (packet.Rate != 16000 && packet.Rate != 48000) ||
                    packet.Pcm is null || packet.Pcm.Length > 130000) return 6;
                byte[] bytes;
                try { bytes = Convert.FromBase64String(packet.Pcm); }
                catch (FormatException) { return 7; }
                if (bytes.Length is < 2 or > 192000 || bytes.Length % (2 * packet.Channels) != 0) return 8;
                if (!packet.Speech)
                {
                    count = 0;
                    Reply(new { protocol = 1, ready = true, modelLoaded = true });
                    continue;
                }
                var data = new short[bytes.Length / 2];
                Buffer.BlockCopy(bytes, 0, data, 0, bytes.Length);
                var frames = data.Length / packet.Channels;
                var stride = packet.Rate / 16000;
                string? name = null;
                for (var frame = 0; frame < frames; frame += stride)
                {
                    var sum = 0f;
                    var taps = 0;
                    for (var tap = 0; tap < stride && frame + tap < frames; tap++)
                    {
                        var position = (frame + tap) * packet.Channels;
                        sum += packet.Channels == 2
                            ? (data[position] + data[position + 1]) / 65536f
                            : data[position] / 32768f;
                        taps++;
                    }
                    samples[count++] = sum / taps;
                    if (count < samples.Length) continue;
                    using var stream = extractor.CreateStream();
                    stream.AcceptWaveform(16000, samples);
                    stream.InputFinished();
                    if (extractor.IsReady(stream))
                    {
                        var vector = extractor.Compute(stream);
                        var before = registry.Snapshot().Vectors.Count;
                        var result = registry.Resolve(new SpeakerEvidence(null, vector, 0.95f, true, true));
                        name = result.Name;
                        if (registry.Snapshot().Vectors.Count != before)
                            memory.Save(activeGame, registry);
                    }
                    count = 0;
                }
                Reply(new { protocol = 1, ready = true, modelLoaded = true, speaker = name });
            }
            return 0;
        }
        catch (Exception)
        {
            // Native faults can terminate this process; never affect the parent.
            try { Reply(new { protocol = 1, ready = false, modelLoaded = false }); }
            catch { }
            return 9;
        }
    }
}
