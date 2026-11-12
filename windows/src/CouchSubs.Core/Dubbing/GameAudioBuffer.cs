// SPDX-License-Identifier: AGPL-3.0-only

namespace CouchSubs.Core.Dubbing;

/// <summary>
/// Remote Play oyun sesinin gerçek zamanlı akış tamponu. Gecikme sabit kalsın diye
/// kapasite dolunca EN ESKİ örnekler atılır. Üretici ve tüketici farklı thread'lerde olabilir.
/// Örnekler kanallar arasında interleaved PCM float'tır.
/// </summary>
public sealed class GameAudioBuffer
{
    private readonly object gate = new();
    private readonly float[] samples;
    private int head;
    private int count;

    public GameAudioBuffer(int capacitySamples)
    {
        if (capacitySamples <= 0) throw new ArgumentOutOfRangeException(nameof(capacitySamples));
        samples = new float[capacitySamples];
    }

    public int CapacitySamples => samples.Length;

    public int BufferedSamples
    {
        get { lock (gate) return count; }
    }

    /// <summary>Önceki en eski örnekleri atarak yeni akışı yaz (tahsis yok).</summary>
    public void Write(ReadOnlySpan<float> pcm)
    {
        lock (gate)
        {
            // Tek seferde kapasiteden büyük bir paket gelirse yalnız en güncel kısmı tut.
            if (pcm.Length >= samples.Length)
            {
                pcm = pcm[^samples.Length..];
                pcm.CopyTo(samples);
                head = 0;
                count = samples.Length;
                return;
            }
            var overflow = Math.Max(0, count + pcm.Length - samples.Length);
            head = (head + overflow) % samples.Length;
            count -= overflow;
            var tail = (head + count) % samples.Length;
            foreach (var sample in pcm)
            {
                samples[tail] = sample;
                tail = (tail + 1) % samples.Length;
            }
            count += pcm.Length;
        }
    }

    /// <summary>Alınamayan örnekler sessizlikle doldurulur, her zaman tam tampon döner.</summary>
    public void Read(Span<float> destination)
    {
        destination.Clear();
        lock (gate)
        {
            var read = Math.Min(destination.Length, count);
            for (var i = 0; i < read; i++)
            {
                destination[i] = samples[head];
                head = (head + 1) % samples.Length;
            }
            count -= read;
        }
    }

    public void Clear()
    {
        lock (gate)
        {
            head = 0;
            count = 0;
        }
    }
}
