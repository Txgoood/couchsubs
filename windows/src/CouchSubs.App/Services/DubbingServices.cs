// SPDX-License-Identifier: AGPL-3.0-only

using System.IO;
using System.Runtime.InteropServices;
using CouchSubs.Core.Dubbing;
using CouchSubs.Shell;
using CouchSubs.Translation;

namespace CouchSubs.App.Services;

/// <summary>
/// Pocket TTS Turkish (wite-tech, CC-BY-4.0) ilk kullanımda %LOCALAPPDATA%\CouchSubs\models\tts-tr\&lt;sürüm&gt;
/// altına indirilir; ses motoru uygulamayla gelen tts\pocket-tts.exe (Kyutai, MIT).
/// </summary>
internal sealed class PocketDubbingModel : IDubbingModel
{
    private readonly ModelStore store = new(Path.Combine(AppPaths.Models, "tts-tr", DubbingModel.Revision),
        [.. DubbingModel.Files.Select(file => new ModelFile(file.Name, file.Url, file.Bytes, file.Sha256))]);

    public static string Executable => Path.Combine(AppContext.BaseDirectory, "tts", "pocket-tts.exe");

    /// <summary>Ses motoru pakette yoksa (geliştirici derlemesi) dublaj gösterilmez.</summary>
    public static bool EngineBundled => File.Exists(Executable);

    public bool IsInstalled => store.IsInstalled;
    public long DownloadBytes => store.TotalBytes;

    public async Task DownloadAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        await store.DownloadAsync(progress, cancellationToken).ConfigureAwait(false);
        WriteConfig();
        // Önceki sürümlerin modelleri yer kaplamasın.
        foreach (var old in Directory.EnumerateDirectories(Path.Combine(AppPaths.Models, "tts-tr")))
            if (!string.Equals(Path.GetFileName(old), DubbingModel.Revision, StringComparison.Ordinal))
                try { Directory.Delete(old, recursive: true); } catch (IOException) { }
    }

    public byte[] VoiceFile(DubbingVoice voice) => File.ReadAllBytes(store.PathOf($"voices/{voice.FileName()}.wav"));

    // SentencePiece'in Windows derlemesi Unicode kullanıcı dizinlerindeki dosyayı
    // Win32 dar-karakterli API ile açamayabilir. Kaynak modeli silmeden ASCII
    // yollu bir çalışma kopyası hazırla (yalnız ilk kullanımda).
    public ISpeechSynthesizer CreateSynthesizer() => new Synthesizer(() =>
    {
        var directory = store.InstallDirectory;
        if (!DubbingModel.IsAsciiPath(directory))
            directory = PrepareAsciiRuntimeDirectory();

        var config = Path.Combine(directory, "couchsubs.yaml");
        var local = DubbingModel.LocalConfig(File.ReadAllText(Path.Combine(directory, "config.yaml")), directory);
        if (!File.Exists(config) || File.ReadAllText(config) != local)
            File.WriteAllText(config, local);
        return new PocketTtsServer(Executable, config, Path.Combine(directory, "voices", "male_1.wav"));
    });

    private string PrepareAsciiRuntimeDirectory()
    {
        // PUBLIC Windows'ta genellikle C:\\Users\\Public'tir ve Türkçe kullanıcı
        // adını içermez. Modeller herkese açık lisanslıdır; kişisel veri kopyalanmaz.
        var publicRoot = Environment.GetEnvironmentVariable("PUBLIC");
        if (string.IsNullOrWhiteSpace(publicRoot) || !DubbingModel.IsAsciiPath(publicRoot))
            throw new InvalidOperationException("Pocket TTS için ASCII yol bulunamadı (PUBLIC).");

        var directory = Path.Combine(publicRoot, "CouchSubs", "tts-runtime", DubbingModel.Revision);
        Directory.CreateDirectory(directory);
        foreach (var file in DubbingModel.Files)
        {
            var destination = Path.Combine(directory, file.Name.Replace('/', Path.DirectorySeparatorChar));
            var info = new FileInfo(destination);
            if (info.Exists && info.Length == file.Bytes) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            var temp = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.Copy(store.PathOf(file.Name), temp, overwrite: false);
                if (new FileInfo(temp).Length != file.Bytes)
                    throw new InvalidDataException("Pocket TTS modeli eksik kopyalandı: " + file.Name);
                File.Move(temp, destination, overwrite: true);
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }
        return directory;
    }

    private string WriteConfig()
    {
        var path = store.PathOf("couchsubs.yaml");
        var local = DubbingModel.LocalConfig(File.ReadAllText(store.PathOf("config.yaml")), store.InstallDirectory);
        if (!File.Exists(path) || File.ReadAllText(path) != local) File.WriteAllText(path, local);
        return path;
    }

    private sealed class Synthesizer(Func<PocketTtsServer> createServer) : ISpeechSynthesizer
    {
        private readonly object gate = new();
        private Task<PocketTtsServer>? ready;

        // 438 MB'lık yerel kopyalama arayüz iş parçacığını kilitlemesin.
        private Task<PocketTtsServer> Server()
        {
            lock (gate) return ready ??= Task.Run(createServer);
        }

        public async Task StartAsync(CancellationToken cancellationToken = default) =>
            await (await Server().WaitAsync(cancellationToken)).StartAsync(cancellationToken);

        public async Task<SpeechAudio> SynthesizeAsync(string text, Uri voice, CancellationToken cancellationToken = default) =>
            await (await Server().WaitAsync(cancellationToken)).SynthesizeAsync(text, voice, cancellationToken);

        public async ValueTask DisposeAsync()
        {
            Task<PocketTtsServer>? existing;
            lock (gate) existing = ready;
            if (existing is not null)
                await (await existing).DisposeAsync();
        }
    }
}

/// <summary>Bellekteki WAV'ı Windows'un kendi sesiyle (winmm PlaySound) çalar; ek kitaplık gerekmez.</summary>
internal sealed partial class WindowsAudioOutput : IAudioOutput
{
    private const uint SndAsync = 0x0001, SndMemory = 0x0004, SndNoDefault = 0x0002;
    private readonly object gate = new();
    /// <summary>Çalan ses bitene kadar bellekte sabit kalmalı.</summary>
    private GCHandle playing;

    public void Play(byte[] wav)
    {
        lock (gate)
        {
            PlaySound(0, 0, 0);
            if (playing.IsAllocated) playing.Free();
            playing = GCHandle.Alloc(wav, GCHandleType.Pinned);
            PlaySound(playing.AddrOfPinnedObject(), 0, SndMemory | SndAsync | SndNoDefault);
        }
    }

    public void Stop()
    {
        lock (gate)
        {
            PlaySound(0, 0, 0);
            if (playing.IsAllocated) playing.Free();
        }
    }

    [LibraryImport("winmm.dll", EntryPoint = "PlaySoundW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PlaySound(nint sound, nint module, uint flags);
}
