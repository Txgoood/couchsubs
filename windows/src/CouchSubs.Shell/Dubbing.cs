// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.Json.Serialization;
using CouchSubs.Core;
using CouchSubs.Core.Dubbing;

namespace CouchSubs.Shell;

/// <summary>Dublajın nereden çalınacağı.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<DubbingOutput>))]
public enum DubbingOutput
{
    /// <summary>TV uygulaması çalar; ses oyunun sesiyle aynı hoparlörden gelir.</summary>
    Tv,
    /// <summary>Bilgisayarın hoparlörü ya da kulaklığı.</summary>
    Computer,
    /// <summary>PS5 Agon ekranında; Türkçe ses bilgisayarın Windows ses çıkışından verilir.
    /// Monitörde duyulması için bilgisayarın ses çıkışı monitöre/hoparlöre fiziksel olarak yönlendirilmelidir.</summary>
    Agon,
    /// <summary>PS5 kablosuz kulaklık: PC sesi ancak kulaklık bilgisayardan ikinci ses akışı alabiliyorsa duyulur.</summary>
    Headset,
}

/// <summary>Seslendirme motoru.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<DubbingEngine>))]
public enum DubbingEngine
{
    /// <summary>pocket-tts: doğal sesler, bir kez indirilir; işlemciye yük, cümle başına bir iki saniye.</summary>
    Natural,
    /// <summary>Windows'un kendi Türkçe sesi: indirme yok, anında konuşur, daha mekanik.</summary>
    Fast,
}

public enum DubbingEmotionMode { Off, Balanced, Cinematic, Acoustic, Neural }

/// <summary>Optional game audio speech activity engine; legacy/installed defaults preserved.</summary>
public enum SpeechDetectionEngine { Automatic, SileroOnnx, TextOnly }

public sealed record DubbingSettings
{
    public bool Enabled { get; init; }
    /// <summary>Experimental speaker embeddings for PS5 audio without subtitle names.</summary>
    public bool AutoSpeakerTracking { get; init; } = true;
    /// <summary>Protect against wrong anonymous speaker attribution during rapid interruptions.</summary>
    public bool InterruptionGuard { get; init; } = false;
    public SpeechDetectionEngine SpeechDetection { get; init; } = SpeechDetectionEngine.Automatic;
    /// <summary>Auto-create/select library only when a recognizable game title is observed.</summary>
    public bool AutoGameLibrary { get; init; } = true;
    /// <summary>Opt-in emotional speech. Never affects subtitle OCR or translation.</summary>
    public DubbingEmotionMode EmotionMode { get; init; } = DubbingEmotionMode.Balanced;
    public DubbingEngine Engine { get; init; } = DubbingEngine.Natural;
    public DubbingOutput Output { get; init; } = DubbingOutput.Tv;
    /// <summary>Keep at least this percentage of PS5 game ambience while speaking (PULSE 3D mixer only).</summary>
    public int GameAtmospherePercent { get; init; } = 82;
    /// <summary>Turkish voice level on shared PULSE 3D output; other outputs are unaffected.</summary>
    public int TurkishVoicePercent { get; init; } = 85;
    /// <summary>Legacy by default: previous user audio settings are never reinterpreted.</summary>
    public AtmosphereMixMode AtmosphereMode { get; init; } = AtmosphereMixMode.Legacy;
    /// <summary>Geçerli oyunun sesleri; önceki sürümlerin Cast ayarıyla uyumludur.</summary>
    public VoiceCast Cast { get; init; } = new();
    /// <summary>Karakterler oyun adına göre ayrılır, oyun başına sınırsız giriş saklanabilir.</summary>
    public string ActiveGame { get; init; } = "Genel";
    public Dictionary<string, VoiceCast> GameCasts { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Seslendirme motoru (gerçekte uygulamayla gelen pocket-tts, bkz. <see cref="PocketTtsServer"/>).</summary>
public interface ISpeechSynthesizer : IAsyncDisposable
{
    Task StartAsync(CancellationToken cancellationToken = default);
    Task<SpeechAudio> SynthesizeAsync(string text, Uri voice, CancellationToken cancellationToken = default);
}

/// <summary>Bilgisayardan ses çalar.</summary>
public interface IAudioOutput
{
    void Play(byte[] wav);
    void Stop();
}

/// <summary>İsteğe bağlı oyun+dublaj miks çıkışı; sıradan TV/PC modları bu arayüzü kullanmaz.</summary>
public interface INeuralSpeechDetector
{
    AiPerformanceSnapshot Statistics => default;
    bool Ready { get; }
    bool RecentSpeech { get; }
    void Start();
    void Push(short[] pcm, int channels, int rate);
    void Stop();
}

/// <summary>Optional local speaker-embedding inference. Must never block the game-audio callback.</summary>
public interface IAutomaticSpeakerDetector
{
    AiPerformanceSnapshot Statistics => default;
    SpeakerIdentityResult Latest { get; }
    void Start();
    void Push(short[] pcm, int channels, int rate, bool speechConfirmed);
    void Stop();
}

public interface IGameAudioOutput : IAudioOutput, IDisposable
{
    void Start();
    void PushGameAudio(short[] pcm, int channels, int sampleRate);
    /// <summary>Optional mixer settings; existing implementations remain compatible.</summary>
    void SetAtmosphereMix(int keepGamePercent, int voicePercent) { }
}

/// <summary>Dublaj modeli: kurulu mu, indir, seslerin dosyaları, motor oluştur.</summary>
public interface IDubbingModel
{
    bool IsInstalled { get; }
    long DownloadBytes { get; }
    Task DownloadAsync(IProgress<double> progress, CancellationToken cancellationToken);
    byte[] VoiceFile(DubbingVoice voice);
    ISpeechSynthesizer CreateSynthesizer();
}

/// <summary>
/// Çevrilen cümleleri sırayla seslendirir (Mac: Dubber.swift). Bir cümle çalarken sonraki hazırlanır; oyun konuşması
/// hızlıysa geride kalan eski cümleler atlanır ki ses altyazının çok gerisinde kalmasın.
/// </summary>
public sealed class Dubber(IDubbingModel model, IAudioOutput audio, Func<SubtitleServer> server, Func<int> serverPort, Action<Action> post)
    : IAsyncDisposable
{
    /// <summary>Bekleyen en çok cümle: fazlası eskiden başlayarak atlanır.</summary>
    public const int Backlog = 3;
    private const string PreviewText = "Merhaba, oyundaki karakterleri Türkçe seslendireceğim.";

    private readonly object gate = new();
    private readonly Queue<(SpokenLine Line, DubbingVoice? Preview)> queue = new();
    private ISpeechSynthesizer? synthesizer;
    // Ses motorunun HTTP isteği bitmeden dispose edilmesini önler.
    // Prepare ve Next tarafından başlatılan TÜM ses işlemleri takip edilir.
    private int activeSynthOperations;
    private TaskCompletionSource? synthIdle;
    private bool disposed;
    private bool working;
    private int generation;
    private int Generation { get { lock (gate) return generation; } }
    private SubtitleServer? publishedOn;

    private readonly EmotionContinuity emotions = new();
    public GameProsodyMeter? Prosody { get; set; }
    public INeuralSpeechDetector? NeuralSpeech { get; set; }
    public DubbingEmotionMode EmotionMode { get; set; } = DubbingEmotionMode.Balanced;
    public DubbingEngine Engine { get; set; } = DubbingEngine.Natural;
    public VoiceCast Cast { get; set; } = new();
    public DubbingOutput Output { get; set; } = DubbingOutput.Tv;
    /// <summary>Yeni bir karaktere ses verildi; ayarlara kaydedilir (arayüz iş parçacığında).</summary>
    public event Action<VoiceCast>? CastChanged;
    public event Action<string>? Failed;

    /// <summary>Model yüklenir, ilk cümle beklemesin.</summary>
    public void Prepare()
    {
        PublishVoices();
        _ = TrackSynth(async engine =>
        {
            try { await engine.StartAsync(); }
            catch (Exception error) { ReportFailure(error); }
        });
    }

    public void Speak(IReadOnlyList<SpokenLine> lines)
    {
        if (lines.Count == 0) return;
        lock (gate) if (disposed) return;
        PublishVoices();
        bool interrupt;
        int number;
        lock (gate)
        {
            // Yeni altyazı geldiyse öncekilerin cümleleri artık geç kalmıştır: bekleyenler atılır, çalan da kesilir.
            // Ses yazının gerisinde kalıp oyunu karıştırmasın; dublaj hep ekrandaki altyazıyı söyler.
            queue.Clear();
            foreach (var line in lines) queue.Enqueue((line, null));
            while (queue.Count > Backlog) queue.Dequeue();
            interrupt = working;
            working = true;
            // Sıra ile kuşak aynı kilit altında değişir; eski döngü yeni cümleyi alamaz.
            number = ++generation;
        }
        if (interrupt)
        {
            audio.Stop();
            server().StopSpeech();
        }
        _ = Task.Run(() => RunAsync(number));
    }

    /// <summary>Ayarlardaki "Dinle": deneme her zaman bilgisayardan çalar, kullanıcı o an bilgisayarın başında.</summary>
    public void Preview(DubbingVoice voice)
    {
        lock (gate) if (disposed) return;
        Stop();
        PublishVoices();
        int number;
        lock (gate)
        {
            queue.Enqueue((new SpokenLine("Listen.", PreviewText), voice));
            working = true;
            number = ++generation;
        }
        _ = Task.Run(() => RunAsync(number));
    }

    public void Stop()
    {
        lock (gate)
        {
            queue.Clear();
            working = false;
            generation++;
        }
        audio.Stop();
        server().StopSpeech();
    }

    public async ValueTask DisposeAsync()
    {
        Task waitForSynthesis;
        ISpeechSynthesizer? engine;
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            waitForSynthesis = activeSynthOperations == 0
                ? Task.CompletedTask
                : (synthIdle ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).Task;
            engine = synthesizer;
            synthesizer = null;
        }
        Stop();
        // Model hazırlama ve HTTP /tts istekleri bitmeden HttpClient kapatılamaz.
        await waitForSynthesis.ConfigureAwait(false);
        if (engine is not null) await engine.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>Ses motoru oluşturma ve işlem sayma aynı kilit altında. Kapanırken yeni iş açılmaz.</summary>
    private Task<T> TrackSynth<T>(Func<ISpeechSynthesizer, Task<T>> work)
    {
        ISpeechSynthesizer engine;
        lock (gate)
        {
            if (disposed) return Task.FromResult(default(T)!);
            engine = synthesizer ??= model.CreateSynthesizer();
            activeSynthOperations++;
        }
        return Task.Run(async () =>
        {
            try { return await work(engine).ConfigureAwait(false); }
            finally { SynthesisFinished(); }
        });
    }

    private Task TrackSynth(Func<ISpeechSynthesizer, Task> work)
    {
        ISpeechSynthesizer engine;
        lock (gate)
        {
            if (disposed) return Task.CompletedTask;
            engine = synthesizer ??= model.CreateSynthesizer();
            activeSynthOperations++;
        }
        return Task.Run(async () =>
        {
            try { await work(engine).ConfigureAwait(false); }
            finally { SynthesisFinished(); }
        });
    }

    private void SynthesisFinished()
    {
        TaskCompletionSource? idle = null;
        lock (gate)
        {
            if (--activeSynthOperations == 0)
            {
                idle = synthIdle;
                synthIdle = null;
            }
        }
        idle?.TrySetResult();
    }

    private void ReportFailure(Exception error)
    {
        lock (gate) if (disposed) return;
        post(() =>
        {
            lock (gate) if (disposed) return;
            Failed?.Invoke(error.Message);
        });
    }

    private async Task RunAsync(int number)
    {
        Task<(SpeechAudio Audio, bool Preview)?>? next = Next(number);
        while (next is not null)
        {
            var result = await next;
            // Bu arada yeni altyazı geldiyse eski cümle hiç çalınmaz.
            if (number != Generation) return;
            if (result is not { } ready) break;
            // Bu cümle çalarken sonraki hazırlanır.
            next = Next(number);
            await PlayAsync(ready.Audio, ready.Preview ? DubbingOutput.Computer : Output);
            if (number != Generation) return;
        }
        lock (gate)
        {
            if (number != generation) return;
            if (queue.Count == 0) { working = false; return; }
        }
        // Bir cümle seslendirilemediyse sıradakilerle sürer.
        await RunAsync(number);
    }

    private Task<(SpeechAudio, bool)?>? Next(int number)
    {
        SpokenLine line;
        DubbingVoice? preview;
        lock (gate)
        {
            // Geç başlayan eski döngü yeni altyazının cümlesini alıp atmasın.
            if (disposed || queue.Count == 0 || number != generation) return null;
            (line, preview) = queue.Dequeue();
        }
        DubbingVoice voice;
        if (preview is { } chosen)
        {
            voice = chosen;
        }
        else
        {
            bool assigned;
            VoiceCast? copy = null;
            lock (gate)
            {
                voice = Cast.VoiceFor(line, out assigned);
                if (assigned) copy = Cast.Copy();
            }
            if (copy is not null) post(() => CastChanged?.Invoke(copy));
        }
        // Windows' built-in synthesizer cannot interpret pocket-tts [emotion] tags.
        // Text-only analysis is intentionally inexpensive and never blocks OCR or audio capture.
        // Mixed game audio must never invent anger from loud explosions or music.
        // Acoustic mode only allows a recent known emotion to carry into an
        // otherwise ambiguous line when sustained voice-like activity is present.
        // Explicit English text cues remain available even without a PS5 audio stream.
        var allowCarry = EmotionMode switch
        {
            DubbingEmotionMode.Acoustic => Prosody?.RecentSpeechCandidate() == true,
            DubbingEmotionMode.Neural => NeuralSpeech?.RecentSpeech == true,
            _ => true
        };
        var emotion = EmotionMode == DubbingEmotionMode.Off || Engine == DubbingEngine.Fast
            ? null : emotions.Resolve(line, EmotionMode, allowCarry);
        var text = SpeechText.Prepare(line.Turkish, emotion);
        var url = new Uri($"http://127.0.0.1:{serverPort()}/voices/{voice.FileName()}.wav");
        return TrackSynth<(SpeechAudio, bool)?>(async engine =>
        {
            try
            {
                return ((await engine.SynthesizeAsync(text, url)).Tightened().SoftEdges(), preview is not null);
            }
            // Motor ne olursa olsun (pocket-tts, Windows sesi) bir cümlenin hatası dublajı durdurmaz.
            catch (Exception error)
            {
                ReportFailure(error);
                return null;
            }
        });
    }

    private async Task PlayAsync(SpeechAudio speech, DubbingOutput output)
    {
        var wav = speech.ToWav();
        var milliseconds = (int)(speech.Duration * 1000);
        if (output == DubbingOutput.Tv)
        {
            server().SendSpeech(wav, milliseconds);
            // TV sesi ağdan alıp çalmaya biraz geç başlar; sıradaki cümle bunu kesmesin.
            await Task.Delay(milliseconds + 250);
        }
        else
        {
            audio.Play(wav);
            await Task.Delay(milliseconds + 60);
        }
    }

    /// <summary>pocket-tts sesleri altyazı sunucusundan alır (<c>/voices/&lt;ad&gt;.wav</c>).</summary>
    private void PublishVoices()
    {
        var current = server();
        if (ReferenceEquals(publishedOn, current) || !model.IsInstalled) return;
        foreach (var voice in DubbingVoices.All)
        {
            // Windows sesinin örnek dosyası yok; ses türü adresin adından anlaşılır.
            var file = model.VoiceFile(voice);
            if (file.Length > 0) current.Publish(file, $"/voices/{voice.FileName()}.wav");
        }
        publishedOn = current;
    }
}
