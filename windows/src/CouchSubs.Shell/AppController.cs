// SPDX-License-Identifier: AGPL-3.0-only

using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using CouchSubs.Core;
using CouchSubs.Core.Dubbing;
using CouchSubs.Core.LgTv;
using CouchSubs.RemotePlay;

namespace CouchSubs.Shell;

/// <summary>
/// Uygulamanın durumu ve akışı (Mac'teki AppModel'in karşılığı): kurulum sihirbazı, çeviriyi
/// başlatma/durdurma, PS5 ve TV durumları. Arayüz bu nesneye bağlanır; olaylar <c>post</c> ile
/// arayüz iş parçacığına aktarılır.
/// </summary>
public sealed class AppController : INotifyPropertyChanged, IDisposable
{
    public const string TvAppId = "com.couchsubs.tv";
    public const int DefaultServerPort = 8787;

    private readonly IAppServices services;
    private readonly IAppStore store;
    private readonly ISettingsStore settingsStore;
    private readonly ISleepGuard sleep;
    private readonly Localizer text;
    private readonly Action<Action> post;
    private readonly List<TranslationMemoryLine> recentTranslations = [];

    private OnboardingStep step;
    private byte[]? accountId;
    private RegisteredConsole? console;
    private LgTv? tv;
    private TvAppState tvApp = TvAppState.Unknown;
    /// <summary>Son eşleşmede TV uygulaması açılamadı.</summary>
    private bool tvAppMissing;
    /// <summary>TV uygulaması denetimi sürüyor (bekleme ekranı ve çeviri başlangıcı aynı anda çağırabilir).</summary>
    private bool refreshingTvApp;
    private int? devModeHoursLeft;
    private UserSettings settings;
    private bool isRunning;
    private bool isStarting;
    private SessionState sessionState = SessionState.Disconnected(DisconnectKind.Stopped);
    private int tvClients;
    private string lastSource = "";
    private string lastTranslation = "";
    private TranslatorStatus translatorStatus = TranslatorStatus.Ready;
    private bool ocrMissing;
    private Frame? lastFrame;
    private readonly object frameGate = new();
    private int sessionGeneration;
    private bool frameUpdateQueued;
    private string? startError;
    private string? pcCaptureError;
    private bool modelDownloading;
    private double modelProgress;
    private string? modelError;
    private Task? modelDownload;

    private SubtitleServer server = new(DefaultServerPort);
    private bool serverStarted;
    private IStreamSession? session;
    private IKeeper? keeper;
    private SubtitlePipeline? pipeline;
    /// <summary>Hattın yazı okuyucusu; ek bölgelerin tek seferlik okuması da bunu kullanır.</summary>
    private ITextReader? activeReader;
    private int extraBusy;
    private bool subtitlesPaused;
    /// <summary>Oyundaki altyazının son görüldüğü yer; deneme altyazısı TV'de oraya yerleşir.</summary>
    private TextBox? lastSubtitleBox;
    private int processing;
    private ITextReader? worldReader;
    private ITextTranslator? worldTranslator;
    private CancellationTokenSource worldWork = new();
    private CancellationTokenSource? worldScanWork;
    private int worldBusy;
    private DateTime nextWorldScan;
    private DateTime nextDetailedPcScan;
    private readonly PcScreenScanPolicy pcScanPolicy = new();
    private int pcFullCaptureInterval = 500;
    private readonly List<CropRegion> changedWorldRegions = [];
    private Frame? latestScreenFrame;
    private ScreenOverlay? worldOverlay;
    private int worldRevision;
    private long nextOverlayCheck;
    private long fastCaptureUntil;
    private int inspectionCapture;
    private sealed record ScreenOverlay(Frame Frame, IReadOnlyList<ScreenTranslation.Item> Items, bool Detailed = false);
    private Dubber? dubber;
    private CancellationTokenSource? pcCaptureCancellation;
    private bool pulseAudioActive;
    private readonly GameProsodyMeter gameProsody = new();
    private readonly LongSessionAudioMonitor longSessionAudioMonitor = new();
    private InworldLiveDiagnosticTap? inworldDiagnosticTap;
    private readonly SpeakerTurnGuard speakerTurnGuard = new();
    private readonly MultiSpeakerTurnTracker multiSpeakerTracker = new();
    private bool manualGamePinned;
    private readonly HashSet<string> suppressedAutoGames = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? gameProbeCancellation;
    private int pulseChannels = 2;
    private int pulseSampleRate = 48000;
    private string? updateVersion;
    private bool updating;
    private double updateProgress;
    private string? updateError;
    private bool dubbingDownloading;
    private double dubbingProgress;
    private string? dubbingError;

    public event PropertyChangedEventHandler? PropertyChanged;

    public AppController(IAppServices services, IAppStore store, ISettingsStore settingsStore, ISleepGuard sleep, Localizer text, Action<Action> post)
    {
        this.services = services;
        this.store = store;
        this.settingsStore = settingsStore;
        this.sleep = sleep;
        this.text = text;
        this.post = post;
        accountId = store.Load<byte[]>(StoreKeys.AccountId);
        console = store.Load<RegisteredConsole>(StoreKeys.Console);
        tv = store.Load<LgTv>(StoreKeys.Tv);
        settings = settingsStore.Load();
        var ready = settings.Source == GameSource.Pc || accountId is not null && console is not null && tv is not null && store.Load<string>(StoreKeys.TvKey) is not null;
        step = ready ? OnboardingStep.Done : OnboardingStep.Welcome;
        server.ClientsChanged += OnClientsChanged;
    }

    // --- Durum ---

    public OnboardingStep Step
    {
        get => step;
        set
        {
            // Eşleştirme adımındaki güvenlik duvarı önerisi başka adıma taşınmaz.
            if (Set(ref step, value, [nameof(CanAdvance), nameof(StepNumber)])) ClearFirewallPrompt();
        }
    }
    public static int StepCount => (int)OnboardingStep.Done;
    public int StepNumber => Math.Min((int)step + 1, StepCount);
    public bool HasAccount => accountId is not null;
    public RegisteredConsole? Console => console;
    public LgTv? Tv => tv;
    private bool HasPairedPs5 => accountId is not null && console is not null && tv is not null
        && store.Load<string>(StoreKeys.TvKey) is not null;
    /// <summary>PS5 olmadan doğrudan bilgisayar oyununu kullan; yerel modeller gerektiğinde indirilir.</summary>
    public void EnterPcMode()
    {
        Settings = settings with { Source = GameSource.Pc };
        if (!ModelInstalled) StartModelDownload();
        Step = OnboardingStep.Done;
    }

    public bool IsPcMode => settings.Source == GameSource.Pc;
    public IReadOnlyList<GameDisplay> PcDisplays => services.GameScreen?.Displays() ?? [];
    public string? PcCaptureError { get => pcCaptureError; private set => Set(ref pcCaptureError, value); }
    public string? StartError { get => startError; private set => Set(ref startError, value); }
    public event Action<string, int, string>? PcSubtitleReady;
    public event Action? PcSubtitleClear;
    public event Action<string, string>? InspectionSubtitle;
    public event Action<SpokenLine>? InspectionPlayback;
    /// <summary>PC üzerinde diyalogdan ayrı envanter/menü/nesne çevirileri.</summary>
    public event Action<IReadOnlyList<ScreenTranslation.Item>, string, int>? PcWorldTextReady;
    public event Action? PcWorldTextClear;
    public bool IsRunning { get => isRunning; private set => Set(ref isRunning, value, [nameof(Ps5StatusText), nameof(Ps5Tone), nameof(TvTone)]); }
    public bool IsStarting { get => isStarting; private set => Set(ref isStarting, value); }
    public SessionState SessionState { get => sessionState; private set => Set(ref sessionState, value, [nameof(Ps5StatusText), nameof(Ps5Tone)]); }
    public string Ps5StatusText => StatusText(sessionState, isRunning, text);
    public StatusTone Ps5Tone => Ps5ToneFor(sessionState, isRunning);
    public int TvClients { get => tvClients; private set => Set(ref tvClients, value, [nameof(TvTone), nameof(TvStatusText)]); }
    public StatusTone TvTone => TvToneFor(tvClients, isRunning);
    public string TvStatusText => tvClients > 0 ? text["Connected"] : text["Waiting for the TV app"];
    public string LastSource { get => lastSource; private set => Set(ref lastSource, value); }
    public string LastTranslation { get => lastTranslation; private set => Set(ref lastTranslation, value); }
    public TranslatorStatus TranslatorStatus { get => translatorStatus; private set => Set(ref translatorStatus, value); }
    /// <summary>Bilgisayarda yazı tanıma dili yok; ayarlardan eklenmesi gerekir.</summary>
    public bool OcrMissing { get => ocrMissing; private set => Set(ref ocrMissing, value); }
    /// <summary>Canlı altyazı çevirisi duraklatıldı (Num 0); ek bölge ve serbest alan çevirileri çalışmaya devam eder.</summary>
    public bool SubtitlesPaused { get => subtitlesPaused; private set => Set(ref subtitlesPaused, value); }
    public Frame? LastFrame { get => lastFrame; private set => Set(ref lastFrame, value); }
    public string? ActiveSourceLanguage { get; private set; }
    /// <summary>Sunucunun gerçekten dinlediği port (8787 doluysa rastgele bir port).</summary>
    public int ServerPort { get; private set; } = DefaultServerPort;

    /// <summary>Bu oyun dilinden hedef dile çeviri modeli var mı (ayarlarda listelenecek diller).</summary>
    public bool SupportsLanguage(string source) => services.Translation.IsSupported(source, settings.TargetLanguage);

    public bool ModelInstalled => services.Translation.IsInstalled(settings.SourceLanguage, settings.TargetLanguage);
    public bool ModelDownloading { get => modelDownloading; private set => Set(ref modelDownloading, value); }
    public double ModelProgress { get => modelProgress; private set => Set(ref modelProgress, value); }
    public string? ModelError { get => modelError; private set => Set(ref modelError, value); }

    public UserSettings Settings
    {
        get => settings;
        set
        {
            var old = settings;
            if (!Set(ref settings, value)) return;
            if ((isRunning || isStarting) && (old.Source != value.Source || old.PcDisplayId != value.PcDisplayId)) Stop();
            settingsStore.Save(value);
            Changed(nameof(IsPcMode), nameof(PcDisplays), nameof(KnownGames));
            if (old.Source != value.Source && value.Source == GameSource.Ps5 && !HasPairedPs5)
                Step = OnboardingStep.Welcome;
            server.SendStyle(value.Style);
            if (old.Style.Timing != value.Style.Timing && pipeline is not null)
                pipeline.TimingMode = value.Style.Timing;
            if (old.Dubbing != value.Dubbing) DubbingSettingsChanged(old.Dubbing);
            var gameChanged = !string.Equals(old.Dubbing.ActiveGame, value.Dubbing.ActiveGame,
                StringComparison.OrdinalIgnoreCase);
            if (gameChanged)
                services.SpeakerDetector?.ConfigureGame(value.Dubbing.ActiveGame);
            if (old.AutoWorldText && !value.AutoWorldText && screenWork is null &&
                DateTime.UtcNow >= screenShownUntil)
            {
                ClearWorldOverlay();
            }
            if (isRunning && (old.SourceLanguage != value.SourceLanguage || old.TargetLanguage != value.TargetLanguage
                              || old.OnlineTranslation != value.OnlineTranslation || old.AutoWorldText != value.AutoWorldText
                              || old.PcPerformanceProfile != value.PcPerformanceProfile
                              || (gameChanged && HasTranslationMemory)))
            {
                RefreshTranslatorStatus();
                MakePipeline();
            }
        }
    }

    // --- Sihirbaz ---

    public bool CanAdvance => step switch
    {
        OnboardingStep.Welcome or OnboardingStep.SecondAccount or OnboardingStep.Test => true,
        OnboardingStep.PsnLogin => accountId is not null,
        OnboardingStep.Console => console is not null,
        OnboardingStep.Tv => tv is not null && store.Load<string>(StoreKeys.TvKey) is not null,
        OnboardingStep.Translation => ModelInstalled,
        _ => false,
    };

    public void Advance()
    {
        if (!CanAdvance || step == OnboardingStep.Done) return;
        // Model büyük; kullanıcı diğer adımlarla uğraşırken arka planda iner.
        if (step == OnboardingStep.Welcome) StartModelDownload();
        Step = step + 1;
    }

    public void Back()
    {
        if (step > OnboardingStep.Welcome) Step = step - 1;
    }

    public async Task CompletePsnLoginAsync(Uri redirect)
    {
        var id = await services.AccountIdFromRedirectAsync(redirect);
        accountId = id;
        store.Save(StoreKeys.AccountId, id);
        Changed(nameof(HasAccount), nameof(CanAdvance));
    }

    public Task<IReadOnlyList<ConsoleInfo>> DiscoverConsolesAsync() => services.DiscoverConsolesAsync();

    public async Task RegisterAsync(string host, string pin)
    {
        var digits = new string([.. pin.Where(char.IsDigit)]);
        if (digits.Length != 8 || !uint.TryParse(digits, out var value)) throw Error(AppError.InvalidPin);
        if (accountId is null) throw Error(AppError.MissingAccount);
        RegisteredConsole registered;
        try
        {
            registered = await services.RegisterAsync(host, accountId, value);
        }
        catch (RegistrationException error)
        {
            // PS5 kendi kodunu bildirdiyse yanıt bize ulaşmıştır, güvenlik duvarı sebep değildir.
            if (error.Reason is null && error.Failure != RegistrationFailure.WrongAccount) await OfferFirewallAsync();
            throw RegistrationError(error);
        }
        catch (Exception error) when (error is not AppException)
        {
            await OfferFirewallAsync();
            throw;
        }
        ClearFirewallPrompt();
        console = registered;
        store.Save(StoreKeys.Console, registered);
        Changed(nameof(Console), nameof(CanAdvance));
    }

    /// <summary>Kullanıcıya gösterilen hata; destek bildirimleri teşhis edilebilsin diye PS5'in kodu da yazılır.</summary>
    private AppException RegistrationError(RegistrationException error)
    {
        var kind = error.Failure switch
        {
            RegistrationFailure.WrongAccount => AppError.WrongAccount,
            RegistrationFailure.NoAnswer => AppError.ConsoleNoAnswer,
            RegistrationFailure.InUse => AppError.ConsoleInUse,
            RegistrationFailure.ConsoleCrashed => AppError.ConsoleCrashed,
            RegistrationFailure.VersionMismatch => AppError.ConsoleVersion,
            _ => AppError.RegistrationFailed,
        };
        var message = AppException.Describe(kind, text);
        return new AppException(kind, string.IsNullOrEmpty(error.Detail) ? message : $"{message} ({error.Detail})");
    }

    public Task<IReadOnlyList<LgTv>> DiscoverTvsAsync() => services.DiscoverTvsAsync();

    public async Task PairAsync(LgTv target)
    {
        await StartServerAsync();
        var pairing = await services.PairTvAsync(target, store.Load<string>(StoreKeys.TvKey), ServerAddress(target));
        if (tv?.Brand != target.Brand || (target.Brand != TvBrand.AndroidTv && tv?.Host != target.Host))
            store.Remove(StoreKeys.TvAppVersion);
        tv = target;
        store.Save(StoreKeys.Tv, target);
        store.Save(StoreKeys.TvKey, pairing.Key);
        tvAppMissing = !pairing.Launched;
        Changed(nameof(Tv), nameof(CanAdvance), nameof(CanInstallTvApp));
        if (target.Brand == TvBrand.Lg) await RefreshTvAppAsync();
        // Samsung girişi ya da Android onayı dakikalar sürebilir; eşleşme beklemesin.
        else _ = RefreshTvAppAsync();
    }

    // --- TV uygulamasını kurma ---

    public TvAppState TvApp
    {
        get => tvApp;
        private set
        {
            tvApp = value;
            Changed(nameof(TvApp));
        }
    }

    /// <summary>LG geliştirici modunun kalan süresi (saat); CouchSubs uzattıktan sonra bilinir.</summary>
    public int? DevModeHoursLeft
    {
        get => devModeHoursLeft;
        private set
        {
            devModeHoursLeft = value;
            Changed(nameof(DevModeHoursLeft));
        }
    }

    /// <summary>Seçili TV'ye uygulamayı CouchSubs kurabilir mi (paket uygulamayla geldi mi)?</summary>
    public bool CanInstallTvApp => tv is { } target &&
        (target.Brand == TvBrand.Lg ? services.BundledTvAppVersion is not null : services.BundledApp(target.Brand) is not null);

    /// <summary>Samsung'un sertifika sayfasını tarayıcıda açar (arayüz atar; testlerde boş kalır).</summary>
    public Action<Uri> OpenInBrowser { get; set; } = _ => { };

    /// <summary>Samsung'da geliştirici modunda "Host PC IP" olarak yazılacak bu bilgisayarın adresi.</summary>
    public string? ComputerAddress => LocalAddress(tv is { Host.Length: > 0 } target ? target.Host : "192.168.1.1");

    /// <summary>
    /// Geliştirici modu açık TV'de uygulamayı eksikse kurar, eskiyse günceller.
    /// Anahtar yoksa ve uygulama çalışıyorsa kullanıcıdan bir şey istenmez.
    /// </summary>
    public async Task RefreshTvAppAsync()
    {
        if (tv is not { } target || refreshingTvApp) return;
        refreshingTvApp = true;
        try
        {
            switch (target.Brand)
            {
                case TvBrand.Lg: await RefreshLgAppAsync(target); break;
                case TvBrand.Samsung: await RefreshSamsungAppAsync(target); break;
                case TvBrand.AndroidTv: await RefreshAndroidAppAsync(target); break;
            }
        }
        finally
        {
            refreshingTvApp = false;
        }
    }

    private string? InstalledAppVersion => store.Load<string>(StoreKeys.TvAppVersion);

    private bool NeedsInstall(BundledTvApp bundled) =>
        InstalledAppVersion is not { } installed || DevModeInstaller.IsNewer(bundled.Version, installed);

    private async Task RefreshLgAppAsync(LgTv target)
    {
        if (services.BundledTvAppVersion is not { } bundled) return;
        if (!await services.IsDevModeOnAsync(target.Host))
        {
            TvApp = tvAppMissing ? new TvAppState(TvAppKind.DevModeOff) : TvAppState.Unknown;
            return;
        }
        if (store.Load<string>(StoreKeys.TvDevKey) is not { } devKey)
        {
            TvApp = tvAppMissing ? new TvAppState(TvAppKind.NeedsPassphrase) : TvAppState.Unknown;
            return;
        }
        await InstallTvAppAsync(target, devKey, bundled);
    }

    /// <summary>Kurulum hatasında TV'nin verdiği sebep de gösterilir; genel mesaj tek başına sorunu bulmaya yetmiyordu.</summary>
    private static TvAppState FailedState(DevModeException error) =>
        new(TvAppKind.Failed, Error: error.Error,
            Reason: error.Error is DevModeError.InstallFailed or DevModeError.NotEnoughSpace && !string.IsNullOrWhiteSpace(error.Detail)
                ? error.Detail.Trim() : null);

    /// <summary>Samsung: geliştirici modu açıksa uygulamayı imzalayıp kurar. Sertifika yoksa Samsung girişi istenir.</summary>
    private async Task RefreshSamsungAppAsync(LgTv target)
    {
        if (services.BundledApp(TvBrand.Samsung) is not { } bundled) return;
        if (!NeedsInstall(bundled))
        {
            TvApp = new TvAppState(TvAppKind.UpToDate, InstalledAppVersion ?? bundled.Version);
            return;
        }
        if (!await services.IsSamsungDevModeOnAsync(target.Host))
        {
            TvApp = new TvAppState(TvAppKind.DevModeOff);
            return;
        }
        TvApp = new TvAppState(TvAppKind.Working);
        try
        {
            await services.InstallSamsungAppAsync(target.Host, bundled, e => post(() => InstallEvent(e)));
            store.Save(StoreKeys.TvAppVersion, bundled.Version);
            TvApp = new TvAppState(TvAppKind.UpToDate, bundled.Version);
            await OpenTvAppAfterInstallAsync(target);
        }
        catch (DevModeException error)
        {
            TvApp = FailedState(error);
        }
    }

    /// <summary>Android TV: ağda ADB'si açık TV'yi bulur, uygulamayı kurar ve açar.</summary>
    private async Task RefreshAndroidAppAsync(LgTv target)
    {
        if (services.BundledApp(TvBrand.AndroidTv) is not { } bundled) return;
        var host = target.Host;
        if (host.Length == 0 || !await services.IsAndroidDebugOnAsync(host))
        {
            // Uygulama kuruluysa ve TV'de hata ayıklama kapalıysa yapılacak bir şey yok.
            if (!NeedsInstall(bundled))
            {
                TvApp = new TvAppState(TvAppKind.UpToDate, InstalledAppVersion ?? bundled.Version);
                return;
            }
            if ((await services.ScanAndroidTvsAsync()).FirstOrDefault() is not { } found)
            {
                TvApp = new TvAppState(TvAppKind.DevModeOff);
                return;
            }
            host = found;
        }
        TvApp = new TvAppState(TvAppKind.Working);
        try
        {
            var version = await services.InstallAndroidAppAsync(host, bundled, e => post(() => InstallEvent(e)));
            // TV'nin adresi bundan sonra sürüm denetimi için kullanılır.
            var located = target with { Host = host };
            tv = located;
            store.Save(StoreKeys.Tv, located);
            store.Save(StoreKeys.TvAppVersion, version);
            Changed(nameof(Tv));
            TvApp = new TvAppState(TvAppKind.UpToDate, version);
        }
        catch (DevModeException error)
        {
            TvApp = FailedState(error);
        }
    }

    private void InstallEvent(TvInstallEvent e)
    {
        // Kurulum bittikten sonra gelen geç olay sonucu ezmesin.
        if (TvApp.Kind is not (TvAppKind.Working or TvAppKind.SignIn or TvAppKind.ApproveOnTv)) return;
        switch (e.Kind)
        {
            case TvInstallEventKind.SignIn when e.Url is { } url:
                TvApp = new TvAppState(TvAppKind.SignIn, Url: url);
                OpenInBrowser(url);
                break;
            case TvInstallEventKind.ApproveOnTv:
                TvApp = new TvAppState(TvAppKind.ApproveOnTv);
                break;
            case TvInstallEventKind.Progress when TvApp.Kind == TvAppKind.SignIn:
                TvApp = new TvAppState(TvAppKind.Working);
                break;
        }
    }

    /// <summary>Key Server'dan anahtarı alır (TV ekranındaki parolayla) ve uygulamayı kurar.</summary>
    public async Task InstallTvAppAsync(string passphrase)
    {
        if (tv is not { } target || services.BundledTvAppVersion is not { } bundled) return;
        TvApp = new TvAppState(TvAppKind.Working);
        try
        {
            var devKey = await services.FetchDevKeyAsync(target.Host, passphrase.Trim());
            store.Save(StoreKeys.TvDevKey, devKey);
            await InstallTvAppAsync(target, devKey, bundled);
        }
        catch (DevModeException error)
        {
            TvApp = new TvAppState(TvAppKind.NeedsPassphrase, Error: error.Error);
        }
    }

    private async Task InstallTvAppAsync(LgTv target, string devKey, string bundled)
    {
        TvApp = new TvAppState(TvAppKind.Working);
        try
        {
            var installed = await services.InstalledTvAppVersionAsync(target.Host, devKey);
            if (installed is not null && !DevModeInstaller.IsNewer(bundled, installed))
            {
                TvApp = new TvAppState(TvAppKind.UpToDate, installed);
                await RenewDevSessionIfDueAsync(target, devKey);
                return;
            }
            await services.InstallTvAppAsync(target.Host, devKey);
            TvApp = new TvAppState(TvAppKind.UpToDate, bundled);
            tvAppMissing = false;
            await RenewDevSessionIfDueAsync(target, devKey);
            await OpenTvAppAfterInstallAsync(target);
        }
        catch (DevModeException error) when (error.Error == DevModeError.KeyRefused)
        {
            // Geliştirici modu yeniden açılınca TV yeni anahtar üretir; eskisi işe yaramaz.
            store.Remove(StoreKeys.TvDevKey);
            TvApp = new TvAppState(TvAppKind.NeedsPassphrase);
        }
        catch (DevModeException error)
        {
            TvApp = FailedState(error);
        }
    }

    /// <summary>
    /// LG geliştirici modunun 1000 saatlik süresini günde en çok bir kez baştan başlatır; kullanıcı TV'den uzatmak
    /// zorunda kalmaz.
    /// </summary>
    private async Task RenewDevSessionIfDueAsync(LgTv target, string devKey)
    {
        var last = store.Load<DateTimeOffset?>(StoreKeys.DevSessionRenewed) ?? DateTimeOffset.MinValue;
        if (DateTimeOffset.UtcNow - last < TimeSpan.FromHours(20)) return;
        if (await services.RenewDevSessionAsync(target.Host, devKey) is { } hours)
        {
            DevModeHoursLeft = hours;
            store.Save(StoreKeys.DevSessionRenewed, (DateTimeOffset?)DateTimeOffset.UtcNow);
        }
    }

    private async Task OpenTvAppAfterInstallAsync(LgTv target)
    {
        if (keeper is not null)
        {
            keeper.Reconnect();
            return;
        }
        try { await services.PairTvAsync(target, store.Load<string>(StoreKeys.TvKey), ServerAddress(target)); }
        catch (Exception) { /* TV uygulaması kuruldu; açılmadıysa kullanıcı açabilir */ }
    }

    /// <summary>TV ağda yeni bir adreste bulundu.</summary>
    public void TvMoved(LgTv moved)
    {
        tv = moved;
        store.Save(StoreKeys.Tv, moved);
        Changed(nameof(Tv));
    }

    public void StartModelDownload()
    {
        if (ModelInstalled || modelDownload is { IsCompleted: false }) return;
        ModelDownloading = true;
        ModelError = null;
        var download = services.Translation.DownloadAsync(settings.SourceLanguage, settings.TargetLanguage, new ImmediateProgress(value => post(() => ModelProgress = value)), CancellationToken.None);
        modelDownload = FinishDownload(download);
    }

    private async Task FinishDownload(Task download)
    {
        try
        {
            await download;
            post(() => ModelProgress = 1);
        }
        catch (Exception error)
        {
            post(() => ModelError = error.Message);
        }
        finally
        {
            post(() =>
            {
                ModelDownloading = false;
                Changed(nameof(ModelInstalled), nameof(CanAdvance));
                RefreshTranslatorStatus();
                // Çeviri sürerken model indiyse hat yeni çevirmenle kurulur.
                if (isRunning && TranslatorStatus == TranslatorStatus.Ready) MakePipeline();
            });
        }
    }

    public Task WaitForModelAsync() => modelDownload ?? Task.CompletedTask;

    public void SendTestSubtitle()
    {
        if (settings.Source == GameSource.Pc)
        {
            PcSubtitleReady?.Invoke("Türkçe altyazı testi — PC ekranı", 5000, settings.PcDisplayId);
            return;
        }
        server.SendStyle(settings.Style);
        // Oyunda henüz altyazı görülmediyse altyazı bölgesinin altına yakın tipik bir yer kullanılır.
        var region = settings.Region;
        var box = lastSubtitleBox ?? new TextBox(0.3, region.Bottom - 0.1, 0.7, region.Bottom - 0.03);
        server.SendSubtitle(text["This is a test subtitle. Can you see it on top of the game?"], 8000, box);
    }

    /// <summary>
    /// Num 0: canlı altyazı çevirisini duraklatır ya da sürdürür. Alan çevirirken sürekli çeviri de TV'de görünüp
    /// karışmasın diye kullanılır. TV'de kısa bir bildirim çıkar. Çeviri sürmüyorsa bir şey yapmaz.
    /// </summary>
    public void ToggleSubtitles()
    {
        if (!isRunning) return;
        var pause = !subtitlesPaused;
        SubtitlesPaused = pause;
        if (pause)
        {
            pipeline?.Reset();
            server.Clear();
        }
        server.SendStyle(settings.Style);
        server.SendSubtitle(text[pause ? "Subtitle translation paused" : "Subtitle translation resumed"], 1800);
    }

    /// <summary>Num 8: ayar ekranı, oyunun şu anki görüntüsünü yeniden alsın.</summary>
    public event Action? SnapshotRequested;

    public void RequestSnapshot() => SnapshotRequested?.Invoke();

    public void SetInspectionCapture(bool enabled) => Volatile.Write(ref inspectionCapture, enabled ? 1 : 0);

    public async Task<Frame?> CaptureSnapshotAsync()
    {
        if (!isRunning) return null;
        if (!IsPcMode || services.GameScreen is not { } capture) return LastFrame;
        var generation = Volatile.Read(ref sessionGeneration);
        var display = settings.PcDisplayId;
        try
        {
            var frame = await Task.Run(() => capture.Capture(display));
            return IsCurrentSession(generation) ? frame : null;
        }
        catch (Exception error)
        {
            if (IsCurrentSession(generation)) PcCaptureError = "Ekran yakalanamadı. " + error.Message;
            return null;
        }
    }

    public void RefreshTextRecognition()
    {
        if (isRunning) MakePipeline();
        else OcrMissing = !services.HasOcrLanguage(settings.SourceLanguage);
    }

    /// <summary>Çeviri bilgisayarda da gösterilecekse (metin, kalma süresi ms) ile çağrılır.</summary>
    public event Action<string, int>? PcResultReady;

    /// <summary>
    /// 1, 2 ya da 3 numaralı ek bölgeyi şimdi bir kez okuyup çevirir; sonuç ayardaki yere (TV, bilgisayar ya da ikisi) gider
    /// (görev paneli gibi yazılar için). Çeviri sürmüyorsa, bölge kapalıysa ya da önceki istek bitmediyse hiçbir şey yapılmaz.
    /// </summary>
    public async Task<ExtraRegionOutcome> TranslateExtraRegionAsync(int slot)
    {
        if (settings.ExtraRegion(slot) is not { } region) return ExtraRegionOutcome.NotConfigured;
        if (await CaptureSnapshotAsync() is not { } frame) return ExtraRegionOutcome.NotRunning;
        var target = settings.ExtraTarget(slot);
        return (await RunRegionAsync(frame, region, target != ResultTarget.Pc, target != ResultTarget.Tv)).Outcome;
    }

    /// <summary>
    /// Serbest alan (Num 5): verilen karedeki alanı bir kez okuyup çevirir. Sonuç, ayardaki hedefe göre TV'ye de gönderilir;
    /// bilgisayarda gösterilmek üzere her durumda döner (serbest alan penceresi kendisi gösterir).
    /// </summary>
    public Task<(ExtraRegionOutcome Outcome, RegionTranslation.Result? Result)> TranslateAreaAsync(Frame frame, CropRegion region) =>
        RunRegionAsync(frame, region, !IsPcMode && settings.FreeAreaTarget != ResultTarget.Pc, false);

    private async Task<(ExtraRegionOutcome Outcome, RegionTranslation.Result? Result)> RunRegionAsync(Frame frame, CropRegion region, bool toTv, bool toPcPopup)
    {
        var reader = activeReader;
        if (!isRunning || reader is null) return (ExtraRegionOutcome.NotRunning, null);
        var generation = Volatile.Read(ref sessionGeneration);
        // Tuşa üst üste basılınca ikinci bir okuma başlamaz.
        if (Interlocked.Exchange(ref extraBusy, 1) == 1) return (ExtraRegionOutcome.Busy, null);
        try
        {
            var scope = CurrentTranslationMemoryScope;
            var blocks = new List<TranslationMemoryLine>();
            RegionTranslation.Result? result;
            try
            {
                result = await RegionTranslation.TranslateOnceAsync(frame, region, reader, MakeScreenTranslator(scope),
                    onBlockTranslated: (source, translation) => blocks.Add(new(scope, source, translation)));
            }
            catch (TranslatorException error)
            {
                post(() => { if (IsCurrentSession(generation)) TranslatorStatus = error.Status; });
                return (ExtraRegionOutcome.TranslatorUnavailable, null);
            }
            catch
            {
                return (ExtraRegionOutcome.Failed, null);
            }
            if (!IsCurrentSession(generation)) return (ExtraRegionOutcome.NotRunning, null);
            if (result is null)
            {
                var message = text["No text found in this area."];
                if (toTv)
                {
                    server.SendStyle(settings.Style);
                    server.SendSubtitle(message, 2500, new TextBox(region.Left, region.Top, region.Right, region.Bottom));
                }
                if (toPcPopup) post(() => PcResultReady?.Invoke(message, 2500));
                return (ExtraRegionOutcome.NoText, null);
            }
            var milliseconds = RegionTranslation.DisplayMilliseconds(result.Translation);
            if (toTv)
            {
                server.SendStyle(settings.Style);
                server.SendSubtitle(result.Translation, milliseconds, result.Box, result.Structured);
            }
            post(() =>
            {
                if (!IsCurrentSession(generation)) return;
                LastSource = result.Source;
                LastTranslation = result.Translation;
                foreach (var block in blocks) RecordRecentTranslation(block);
                if (toPcPopup) PcResultReady?.Invoke(result.Translation, milliseconds);
            });
            return (ExtraRegionOutcome.Shown, result);
        }
        finally
        {
            Volatile.Write(ref extraBusy, 0);
        }
    }

    /// <summary>Süren "ekranı çevir" işi; yeniden basılınca iptal edilir.</summary>
    private CancellationTokenSource? screenWork;
    /// <summary>TV'deki ekran çevirilerinin kalkacağı an; bu andan önce yeniden basmak onları kaldırır.</summary>
    private DateTime screenShownUntil;
    private Frame? screenSource;
    private ScreenOverlay? manualOverlay;
    private sealed record PcCaption(TextLine Source, string Translation, string Display, long Until);
    private sealed record PcOverlay(IReadOnlyList<ScreenTranslation.Item> Items, string Display, long Until);
    private const int WorldOverlayDuration = 9000;
    private const int WorldOverlayRenewalMargin = 2000;
    private PcCaption? pcCaption;
    private PcOverlay? pcOverlay;
    private bool pcOverlayOwnedCaption;
    private bool EnglishToTurkishScreen => settings.SourceLanguage == "en" && settings.TargetLanguage == "tr";

    /// <summary>
    /// Num 9: oyunun şu anki karesinin tamamını bir kez okur, her yazı grubunu ayrı çevirip TV'de kendi yerine koyar.
    /// Çeviriler hazırlandıkça TV'ye gider. Çeviri sürerken ya da çeviriler TV'deyken yeniden basmak onları kaldırır.
    /// </summary>
    public async Task<ScreenOutcome> TranslateScreenAsync()
    {
        var reader = activeReader;
        if (!isRunning || reader is null) return ScreenOutcome.NotRunning;
        if (screenWork is { } running)
        {
            running.Cancel();
            screenWork = null;
            HideScreen();
            return ScreenOutcome.Cleared;
        }
        if (DateTime.UtcNow < screenShownUntil)
        {
            HideScreen();
            return ScreenOutcome.Cleared;
        }

        using var work = new CancellationTokenSource();
        screenWork = work;
        var pc = IsPcMode;
        var display = settings.PcDisplayId;
        var generation = Volatile.Read(ref sessionGeneration);
        var snapshot = await CaptureSnapshotAsync();
        if (snapshot is not { } frame || work.IsCancellationRequested || !IsCurrentSession(generation))
        {
            if (ReferenceEquals(screenWork, work)) screenWork = null;
            return work.IsCancellationRequested ? ScreenOutcome.Cleared : ScreenOutcome.NotRunning;
        }
        if (pc) PublishFrame(frame, generation);
        screenSource = frame;
        worldScanWork?.Cancel();
        ClearWorldOverlay();
        server.SendStyle(settings.Style);
        if (!pc) server.SendSubtitle(text["Translating the screen…"], 2500);
        try
        {
            var scope = CurrentTranslationMemoryScope;
            var translator = MakeScreenTranslator(scope);
            var items = await ScreenTranslation.TranslateOverlayAsync(frame, reader, translator, progress: shown =>
            {
                post(() =>
                {
                    if (work.IsCancellationRequested || !ReferenceEquals(screenSource, frame) || !IsCurrentScreen(frame))
                        return;
                    var visible = KeepCurrentItems(frame, shown);
                    if (visible.Count == 0) return;
                    var duration = ScreenTranslation.DisplayMilliseconds(visible);
                    screenShownUntil = DateTime.UtcNow.AddMilliseconds(duration);
                    manualOverlay = new ScreenOverlay(frame, visible);
                    RecordScreenTranslations(scope, visible);
                    PublishOverlay(visible, duration, pc, display);
                });
            }, cancellationToken: work.Token, isCurrent: () => IsCurrentScreen(frame),
                englishToTurkish: EnglishToTurkishScreen, isRegionCurrent: box => IsCurrentTextRegion(frame, box),
                waitForModel: WaitForDialogueAsync);
            if (work.IsCancellationRequested || !IsCurrentScreen(frame)) return ScreenOutcome.Cleared;
            if (!pc) server.Clear();
            if (items.Count == 0)
            {
                if (pc) PcResultReady?.Invoke(text["No text found on the screen."], 2500);
                else server.SendSubtitle(text["No text found on the screen."], 2500);
                return ScreenOutcome.NoText;
            }
            return ScreenOutcome.Shown;
        }
        catch (OperationCanceledException)
        {
            return ScreenOutcome.Cleared;
        }
        catch (TranslatorException error)
        {
            if (!pc) server.Clear();
            post(() => TranslatorStatus = error.Status);
            return ScreenOutcome.TranslatorUnavailable;
        }
        catch
        {
            if (!pc) server.Clear();
            return ScreenOutcome.Failed;
        }
        finally
        {
            if (screenWork == work) screenWork = null;
        }
    }

    private void HideScreen(bool clearSubtitle = true)
    {
        screenSource = null;
        manualOverlay = null;
        screenShownUntil = default;
        pcOverlay = null;
        PcWorldTextClear?.Invoke();
        server.ClearScreen(obsOnly: IsPcMode);
        if (!IsPcMode && clearSubtitle) server.Clear();
    }

    private void ClearWorldOverlay()
    {
        worldOverlay = null;
        pcOverlay = null;
        server.ClearScreen(obsOnly: IsPcMode);
        if (IsPcMode) PcWorldTextClear?.Invoke();
    }

    private bool IsCurrentScreen(Frame frame) =>
        Volatile.Read(ref latestScreenFrame) is not { } latest || ScreenTranslation.SameScreen(frame, latest);

    private bool IsCurrentTextRegion(Frame frame, TextBox box) =>
        Volatile.Read(ref latestScreenFrame) is not { } latest || ScreenTranslation.SameTextRegion(frame, latest, box);

    private IReadOnlyList<ScreenTranslation.Item> KeepCurrentItems(Frame source,
        IReadOnlyList<ScreenTranslation.Item> items, TextLine? subtitle = null)
    {
        if (Volatile.Read(ref latestScreenFrame) is not { } latest) return items;
        return items.Where(item => ScreenTranslation.SameTextRegion(source, latest, item.Box) &&
            !ScreenTranslation.IsActiveSubtitle(new TextLine(item.Source, item.Box), subtitle)).ToArray();
    }

    private void PublishOverlay(IReadOnlyList<ScreenTranslation.Item> items, int duration, bool pc, string display)
    {
        if (!pc)
        {
            server.SendScreen(items, duration);
            return;
        }
        pcOverlay = new PcOverlay(items, display, Environment.TickCount64 + duration);
        DrawPcOverlay(pcOverlay);
    }

    private bool DrawPcOverlay(PcOverlay overlay, bool captionChanged = false)
    {
        var now = Environment.TickCount64;
        if (overlay.Until <= now) return false;
        var items = overlay.Items;
        var ownsCaption = false;
        if (pcCaption is { } caption && caption.Until > now && caption.Display == overlay.Display)
            items = ScreenCaptionOwnership.PreferPositioned(items, caption.Source, caption.Translation, out ownsCaption);
        if (captionChanged && !ownsCaption && !pcOverlayOwnedCaption) return false;
        pcOverlayOwnedCaption = ownsCaption;
        if (ownsCaption) PcSubtitleClear?.Invoke();
        var duration = (int)(overlay.Until - now);
        server.SendScreen(items, duration, obsOnly: true);
        PcWorldTextReady?.Invoke(items, overlay.Display, duration);
        return ownsCaption;
    }

    private void ShowPcCaption(string source, string translation, TextBox? box)
    {
        var duration = SubtitlePipeline.ReadingTime(translation);
        var display = settings.PcDisplayId;
        pcCaption = new PcCaption(new TextLine(source, box), translation, display, Environment.TickCount64 + duration);
        if (pcOverlay is { } overlay && overlay.Display == display && DrawPcOverlay(overlay, captionChanged: true)) return;
        PcSubtitleReady?.Invoke(translation, duration, display);
    }

    private void RefreshScreenOverlay()
    {
        var now = Environment.TickCount64;
        if (now < nextOverlayCheck) return;
        nextOverlayCheck = now + 100;
        if (screenWork is null && screenShownUntil != default && DateTime.UtcNow >= screenShownUntil)
        {
            screenSource = null;
            manualOverlay = null;
            screenShownUntil = default;
        }
        if (screenSource is { } manual && !IsCurrentScreen(manual))
        {
            screenWork?.Cancel();
            screenWork = null;
            HideScreen(clearSubtitle: false);
            nextWorldScan = default;
        }
        if (manualOverlay is { } shown)
        {
            var current = KeepCurrentItems(shown.Frame, shown.Items);
            if (current.Count == 0)
            {
                screenWork?.Cancel();
                screenWork = null;
                HideScreen(clearSubtitle: false);
                nextWorldScan = default;
            }
            else if (current.Count != shown.Items.Count)
            {
                manualOverlay = shown with { Items = current };
                var remaining = (int)Math.Max(1, (screenShownUntil - DateTime.UtcNow).TotalMilliseconds);
                PublishOverlay(current, remaining, IsPcMode, settings.PcDisplayId);
            }
        }
        if (worldOverlay is not { } automatic) return;
        var remainingItems = KeepCurrentItems(automatic.Frame, automatic.Items, pipeline?.CurrentSubtitle);
        if (remainingItems.Count == automatic.Items.Count)
        {
            // The current full frame still contains these labels. Renew their display
            // lifetime without scheduling OCR or asking the translator again.
            if (IsPcMode && pcOverlay is { } displayed && displayed.Until - now <= WorldOverlayRenewalMargin)
                PublishOverlay(remainingItems, WorldOverlayDuration, true, displayed.Display);
            return;
        }
        foreach (var removed in automatic.Items.Except(remainingItems))
        {
            if (IsCurrentTextRegion(automatic.Frame, removed.Box)) continue;
            var box = removed.Box;
            var padding = Math.Max(.02, box.Bottom - box.Top);
            var area = new CropRegion(Math.Max(0, box.Top - padding), Math.Min(1, box.Bottom + padding),
                Math.Max(0, box.Left - .04), Math.Min(1, box.Right + .09));
            if (!changedWorldRegions.Contains(area) && changedWorldRegions.Count < 5) changedWorldRegions.Add(area);
        }
        if (remainingItems.Count == 0) ClearWorldOverlay();
        else
        {
            worldOverlay = automatic with { Items = remainingItems };
            PublishOverlay(remainingItems, WorldOverlayDuration, IsPcMode, settings.PcDisplayId);
        }
        Interlocked.Increment(ref worldRevision);
        worldScanWork?.Cancel();
        Volatile.Write(ref fastCaptureUntil, now + 500);
    }

    /// <summary>Kurulumu baştan başlatır (kayıtlı her şeyi siler).</summary>
    public void ResetSetup()
    {
        Stop();
        foreach (var key in new[] { StoreKeys.AccountId, StoreKeys.Console, StoreKeys.Tv, StoreKeys.TvKey, StoreKeys.TvDevKey, StoreKeys.TvAppVersion }) store.Remove(key);
        accountId = null;
        console = null;
        tv = null;
        tvAppMissing = false;
        TvApp = TvAppState.Unknown;
        Step = OnboardingStep.Welcome;
        Changed(nameof(HasAccount), nameof(Console), nameof(Tv));
    }

    // --- Çeviri ---

    public async Task StartAsync()
    {
        // Düğmeye iki kez basılması ya da beklerken tekrar çağrılması ikinci bir oturum açmaz.
        if (isRunning || isStarting) return;
        if (settings.Source == GameSource.Pc)
        {
            await StartPcAsync();
            return;
        }
        if (console is null || accountId is null || tv is null) return;
        var generation = Volatile.Read(ref sessionGeneration);
        StartError = null;
        IsStarting = true;
        try
        {
            RefreshTranslatorStatus();
            try { await StartServerAsync(); } catch { /* port açılamadıysa TV durumu "bekleniyor" kalır */ }
            if (!IsCurrentSession(generation)) return;
            server.SendStyle(settings.Style);
            SubtitlesPaused = false;
            MakePipeline();
            // PULSE 3D: yalnız Headset modunda isteğe bağlı Remote Play oyun sesi açılır.
            // USB alıcısı Windows'un varsayılan çıkışı seçilmelidir.
            pulseAudioActive = false;
            // A Headset PS5 session always has an audio-capable stream and a
            // reusable mixer. The Turkish Dubbing checkbox must never require
            // disconnecting Remote Play to enable speech again.
            TryStartHeadsetAudio();

            bool acousticMode = settings.Dubbing.Enabled && settings.Dubbing.EmotionMode == DubbingEmotionMode.Acoustic;
            // Safe model registry v1: Automatic/explicit Silero use the existing
            // verified model. TextOnly disables neural capture and returns to the
            // pre-existing text/acoustic dubbing without changing the game soundtrack.
            bool speechModelEnabled = settings.Dubbing.SpeechDetection != SpeechDetectionEngine.TextOnly
                && services.NeuralSpeech is not null;
            bool autoSpeaker = settings.Dubbing.Enabled && settings.Dubbing.AutoSpeakerTracking
                && services.SpeakerDetector is not null && speechModelEnabled;
            bool neuralMode = settings.Dubbing.Enabled &&
                (settings.Dubbing.EmotionMode == DubbingEmotionMode.Neural || autoSpeaker)
                && speechModelEnabled;
            if (neuralMode) services.NeuralSpeech?.Start();
            if (autoSpeaker)
            {
                services.SpeakerDetector?.ConfigureGame(settings.Dubbing.ActiveGame);
                services.SpeakerDetector?.Start();
            }
            bool captureAudio = Ps5DubbingTogglePolicy.RequiresAudioCapture(
                settings.Dubbing.Output == DubbingOutput.Headset,
                pulseAudioActive, acousticMode, neuralMode);
            gameProsody.Clear();
            speakerTurnGuard.Reset();
            multiSpeakerTracker.Reset();
            if (captureAudio && pulseAudioActive) TryStartInworldDiagnosticTap();
            var stream = captureAudio ? services.MakeAudioStream(console, accountId) : services.MakeStream(console, accountId);
            if (captureAudio && stream is StreamSession audioStream)
            {
                audioStream.GameAudioFormatReceived += (channels, rate) =>
                {
                    Volatile.Write(ref pulseChannels, channels);
                    Volatile.Write(ref pulseSampleRate, rate);
                };
                audioStream.GameAudioReceived += pcm =>
                {
                    var channels = Volatile.Read(ref pulseChannels);
                    var rate = Volatile.Read(ref pulseSampleRate);
                    if (acousticMode) gameProsody.Push(pcm, channels, rate);
                    if (neuralMode) services.NeuralSpeech?.Push(pcm, channels, rate);
                    if (autoSpeaker) services.SpeakerDetector?.Push(pcm, channels, rate,
                        services.NeuralSpeech?.RecentSpeech == true);
                    // Diagnostic side-channel only: original PS5 PCM ALWAYS
                    // goes directly to the existing PULSE 3D mixer, unchanged.
                    Volatile.Read(ref inworldDiagnosticTap)?.TryCapture(pcm, channels, rate);
                    if (pulseAudioActive && services.GameAudio is { } mixer)
                        mixer.PushGameAudio(pcm, channels, rate);
                };
            }
            stream.StateChanged += state => post(() => { if (IsCurrentSession(generation)) SessionStateChanged(state); });
            stream.ConsoleMoved += moved => post(() => { if (IsCurrentSession(generation)) ConsoleMoved(moved); });
            stream.FrameReceived += frame => HandleFrame(frame, generation);
            session = stream;
            stream.Start();
            if (settings.Dubbing.AutoGameLibrary)
            {
                gameProbeCancellation = new CancellationTokenSource();
                _ = PollPs5GameTitleAsync(gameProbeCancellation.Token);
            }
            if (settings.Dubbing.Enabled && DubbingInstalled) MakeDubber()?.Prepare();

            // TV uygulamasını başlat ve açık tut; TV kapalıysa açıldığında bağlanır.
            var running = server;
            var tvKeeper = services.MakeKeeper(tv, store.Load<string>(StoreKeys.TvKey), new Dictionary<string, string> { ["server"] = ServerAddress(tv) },
                () => running.ConnectedClients > 0, moved => post(() => TvMoved(moved)));
            keeper = tvKeeper;
            tvKeeper.Start(TimeSpan.FromSeconds(5));
            _ = RefreshTvAppAsync();

            sleep.Begin();
            IsRunning = true;
            UpdateTroubleClocks();
        }
        catch (Exception error)
        {
            if (IsCurrentSession(generation)) FailStart(error, "PS5 bağlantısını ve TV ayarlarını kontrol edip yeniden dene.");
        }
        finally
        {
            IsStarting = false;
        }
    }

    /// <summary>Windows ekranında oyun: PS5/TV/PSN bağlantısı açılmadan yerel OCR ve dublaj.</summary>
    private async Task StartPcAsync()
    {
        if (services.GameScreen is not { } capture)
        {
            StartError = "Ekran yakalama kullanılamıyor.";
            return;
        }
        var generation = Volatile.Read(ref sessionGeneration);
        StartError = null;
        IsStarting = true;
        try
        {
            RefreshTranslatorStatus();
            await StartServerAsync();
            if (!IsCurrentSession(generation)) return;
            server.SendStyle(settings.Style);
            SubtitlesPaused = false;
            PcCaptureError = null;
            MakePipeline();
            // PC oyun sesi zaten Windows mikserindedir; ikinci bir PS5 ses hattı başlatılmaz.
            pulseAudioActive = false;
            if (settings.Dubbing.Enabled && DubbingInstalled) MakeDubber()?.Prepare();
            var token = new CancellationTokenSource();
            pcCaptureCancellation = token;
            sleep.Begin();
            IsRunning = true;
            var display = settings.PcDisplayId;
            _ = Task.Run(async () =>
            {
                string? lastError = null;
                long lastFullCapture = 0;
                while (!token.IsCancellationRequested)
                {
                    string? captureError = null;
                    try
                    {
                        var now = Environment.TickCount64;
                        var region = settings.Region;
                        var inspection = Volatile.Read(ref inspectionCapture) != 0;
                        var manual = Volatile.Read(ref screenWork) is not null || Volatile.Read(ref manualOverlay) is not null;
                        var world = Volatile.Read(ref worldOverlay) is not null || now < Volatile.Read(ref fastCaptureUntil);
                        var adaptive = settings.PcPerformanceProfile && settings.AutoWorldText;
                        var fullInterval = inspection || manual ? 130 : adaptive
                            ? Volatile.Read(ref pcFullCaptureInterval) : world ? 400 : settings.AutoWorldText ? 1200 : 2500;
                        if (lastFullCapture == 0 || now - lastFullCapture >= fullInterval)
                        {
                            HandleFrame(capture.Capture(display), generation);
                            lastFullCapture = now;
                        }
                        else if (!subtitlesPaused)
                        {
                            var area = capture.CaptureSubtitle(display, region);
                            await ProcessSubtitleFrameAsync(area, region, generation, cropped: true).ConfigureAwait(false);
                        }
                    }
                    catch (Exception error)
                    {
                        captureError = "Ekran yakalanamadı. " + error.Message;
                    }
                    if (captureError != lastError)
                    {
                        lastError = captureError;
                        post(() => { if (IsCurrentSession(generation)) PcCaptureError = captureError; });
                    }
                    var menuActive = Volatile.Read(ref inspectionCapture) != 0 || Volatile.Read(ref screenWork) is not null ||
                        Volatile.Read(ref manualOverlay) is not null || Environment.TickCount64 < Volatile.Read(ref fastCaptureUntil);
                    try { await Task.Delay(menuActive ? 130 : 280, token.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) { break; }
                }
            });
        }
        catch (Exception error)
        {
            if (IsCurrentSession(generation)) FailStart(error, "Oyun ekranını kontrol edip yeniden dene.");
        }
        finally { IsStarting = false; }
    }

    private void FailStart(Exception error, string suggestion)
    {
        Trace.TraceError("CouchSubs startup failed: {0}", error);
        Stop();
        StartError = "Çeviri başlatılamadı. " + suggestion;
    }

    private bool IsCurrentSession(int generation) => generation == Volatile.Read(ref sessionGeneration);

    public void Stop()
    {
        lock (frameGate)
        {
            Interlocked.Increment(ref sessionGeneration);
            Volatile.Write(ref latestScreenFrame, null);
        }
        LastFrame = null;
        gameProbeCancellation?.Cancel();
        gameProbeCancellation = null;
        manualGamePinned = false;
        pcCaptureCancellation?.Cancel();
        pcCaptureCancellation = null;
        worldWork.Cancel();
        worldWork = new CancellationTokenSource();
        worldReader = null;
        worldTranslator = null;
        worldOverlay = null;
        changedWorldRegions.Clear();
        Volatile.Write(ref fastCaptureUntil, 0);
        server.ClearScreen();
        post(() =>
        {
            pcCaption = null;
            pcOverlay = null;
            PcSubtitleClear?.Invoke();
            PcWorldTextClear?.Invoke();
        });
        session?.Stop();
        session?.Dispose();
        session = null;
        var oldInworldTap = Interlocked.Exchange(ref inworldDiagnosticTap, null);
        if (oldInworldTap is not null)
        {
            oldInworldTap.SetFinalPlaybackStats(services.GameAudio?.AudioBufferStatistics);
            _ = Task.Run(async () =>
            {
                try { await oldInworldTap.DisposeAsync(); }
                catch { /* diagnostic shutdown must never interrupt PS5 audio */ }
            });
        }
        pulseAudioActive = false;
        services.GameAudio?.Dispose();
        services.NeuralSpeech?.Stop();
        services.SpeakerDetector?.Stop();
        speakerTurnGuard.Reset();
        multiSpeakerTracker.Reset();
        longSessionAudioMonitor.Reset();
        keeper?.Stop();
        keeper = null;
        pipeline?.Reset();
        pipeline = null;
        activeReader = null;
        screenWork?.Cancel();
        screenWork = null;
        screenSource = null;
        manualOverlay = null;
        if (DateTime.UtcNow < screenShownUntil) server.ClearScreen();
        screenShownUntil = default;
        SubtitlesPaused = false;
        ActiveSourceLanguage = null;
        StopDubber();
        if (isRunning) sleep.End();
        IsRunning = false;
        SessionState = SessionState.Disconnected(DisconnectKind.Stopped);
        UpdateTroubleClocks();
        firewallRuleSeen = false;
        ClearFirewallPrompt();
    }

    /// <summary>Bilgisayar uykudan uyanınca bağlantıları hemen yenile.</summary>
    public void SystemResumed()
    {
        // Sleep intervals are not real audio-buffer observation windows.
        longSessionAudioMonitor.Reset();
        session?.ReconnectNow();
        keeper?.Reconnect();
    }

    /// <summary>Çeviri başlamamışken bağlı olmamak bir sorun değildir; nokta gri kalır.</summary>
    public static StatusTone Ps5ToneFor(SessionState state, bool running) =>
        state.Phase == SessionPhase.Streaming ? StatusTone.Good : running ? StatusTone.Warning : StatusTone.Idle;

    public static StatusTone TvToneFor(int clients, bool running) =>
        clients > 0 ? StatusTone.Good : running ? StatusTone.Warning : StatusTone.Idle;

    /// <summary>PS5 durumunun kullanıcıya gösterilecek metni.</summary>
    public static string StatusText(SessionState state, bool running, Localizer text)
    {
        switch (state.Phase)
        {
            case SessionPhase.Streaming: return text["Connected"];
            case SessionPhase.Connecting: return text["Connecting…"];
        }
        if (!running) return text["Not connected"];
        return text[state.Reason?.Kind switch
        {
            DisconnectKind.ConsoleAsleep => "Turn on your PS5",
            DisconnectKind.InUse => "Remote Play is in use on another device",
            DisconnectKind.Refused => "The PS5 refused the connection. Set up the PS5 again.",
            DisconnectKind.LoginPasscode => "The second user has a login passcode on the PS5. Turn it off in Settings › Users and Accounts.",
            _ => "Reconnecting…",
        }];
    }

    public void Dispose()
    {
        Stop();
        server.Dispose();
    }

    // --- İç işleyiş ---

    /// <summary>Okuma hattını güncel dillerle (yeniden) kurar.</summary>
    private void MakePipeline()
    {
        var scope = CurrentTranslationMemoryScope;
        pipeline?.Reset();
        pipeline = null;
        worldWork.Cancel();
        worldWork = new CancellationTokenSource();
        worldReader = settings.AutoWorldText ? services.MakeReader(settings.SourceLanguage) : null;
        worldTranslator = settings.AutoWorldText ? MakeScreenTranslator(scope) : null;
        nextWorldScan = default;
        nextDetailedPcScan = default;
        pcScanPolicy.Reset();
        Volatile.Write(ref pcFullCaptureInterval, 500);
        changedWorldRegions.Clear();
        ActiveSourceLanguage = settings.SourceLanguage;
        var reader = services.MakeReader(settings.SourceLanguage);
        activeReader = reader;
        OcrMissing = reader is null || !services.HasOcrLanguage(settings.SourceLanguage);
        if (reader is null) return;
        var onlineKey = OnlineTranslationActive ? store.Load<string>(StoreKeys.DeepLKey) : null;
        OnlineKeyState? lastOnlineError = null;
        var built = new SubtitlePipeline(reader, WithMemory(MakeTranslator(), scope), server,
            IsPcMode ? new ReadScheduler(deepEvery: 6) : new ReadScheduler())
        {
            TimingMode = settings.Style.Timing,
            ShowInterfaceFallback = !(IsPcMode && worldReader is not null)
        };
        built.PairShown += (source, translation) =>
        {
            InspectionSubtitle?.Invoke(source, translation);
            var box = built.LastBox;
            post(() =>
            {
                if (!ReferenceEquals(pipeline, built)) return;
                LastSource = source;
                LastTranslation = translation;
                RecordRecentTranslation(new(scope, source, translation));
                AutoSelectRecognizedGame(source);
                DiscoverDialogueNames(source);
                if (box is not null) lastSubtitleBox = box;
                if (settings.Source == GameSource.Pc)
                    ShowPcCaption(source, translation, box);
            });
        };
        // Model bu arada inmiş olabilir; her çeviri sonucu durumu günceller.
        built.TranslationResult += error => post(() =>
        {
            if (!ReferenceEquals(pipeline, built)) return;
            TranslatorStatus = error ?? TranslatorStatus.Ready;
            var onlineError = onlineKey is null ? null : services.OnlineTranslationError(onlineKey);
            if (onlineError != lastOnlineError)
            {
                lastOnlineError = onlineError;
                Changed(nameof(DeepLNotice));
            }
        });
        built.TranslationStarting += () => post(() =>
        {
            if (ReferenceEquals(pipeline, built) && built.IsTranslating && worldScanWork is { IsCancellationRequested: false } scan)
            {
                scan.Cancel();
                if (!IsPcMode) nextWorldScan = default;
            }
        });
        built.SpeechReady += (lines, canStart) => post(() =>
        {
            if (ReferenceEquals(pipeline, built) && !subtitlesPaused) Speak(lines, canStart);
        });
        pipeline = built;
    }

    private ITextTranslator MakeTranslator()
    {
        var local = MakeLocalTranslator();
        if (OnlineTranslationActive && store.Load<string>(StoreKeys.DeepLKey) is { } key &&
            services.MakeOnlineTranslator(key, local is UnavailableTranslator ? null : local) is { } online)
            return online;
        return local;
    }

    private ITextTranslator MakeScreenTranslator(TranslationMemoryScope scope) =>
        WithMemory(ModelInstalled ? MakeLocalTranslator() : MakeTranslator(), scope);

    private ITextTranslator WithMemory(ITextTranslator translator, TranslationMemoryScope scope) =>
        services.TranslationMemory is { } memory ? new MemoryTranslator(translator, memory, scope) : translator;

    public bool HasTranslationMemory => services.TranslationMemory is not null;

    public TranslationMemoryScope CurrentTranslationMemoryScope => new(GameCastLibrary.Normalize(settings.Dubbing.ActiveGame),
        settings.SourceLanguage, settings.TargetLanguage);

    public IReadOnlyList<TranslationMemoryLine> RecentTranslations(TranslationMemoryScope scope) => recentTranslations
        .Where(line => line.Scope == scope).ToArray();

    public IReadOnlyList<TranslationMemoryLine> SavedTranslations(TranslationMemoryScope scope) =>
        services.TranslationMemory?.Saved(scope) ?? [];

    public string? SavedTranslation(TranslationMemoryLine line) =>
        services.TranslationMemory?.TryGet(line.Scope, line.Source, out var translation) == true ? translation : null;

    public Task SaveTranslationAsync(TranslationMemoryLine line) =>
        services.TranslationMemory?.SaveAsync(line) ?? throw new InvalidOperationException();

    public Task RemoveTranslationAsync(TranslationMemoryLine line) =>
        services.TranslationMemory?.RemoveAsync(line.Scope, line.Source) ?? throw new InvalidOperationException();

    private void RecordScreenTranslations(TranslationMemoryScope scope, IReadOnlyList<ScreenTranslation.Item> items)
    {
        foreach (var item in items) RecordRecentTranslation(new(scope, item.Source, item.Translation));
    }

    private void RecordRecentTranslation(TranslationMemoryLine line)
    {
        if (services.TranslationMemory is not { } memory || string.IsNullOrWhiteSpace(line.Translation)) return;
        memory.Record(line);
        var index = recentTranslations.FindIndex(previous => previous.Scope == line.Scope && previous.Source == line.Source);
        if (index >= 0)
        {
            recentTranslations[index] = line;
            return;
        }
        recentTranslations.Insert(0, line);
        if (recentTranslations.Count > 100) recentTranslations.RemoveAt(100);
    }

    private ITextTranslator MakeLocalTranslator()
    {
        var translation = services.Translation;
        var (source, target) = (settings.SourceLanguage, settings.TargetLanguage);
        if (!translation.IsSupported(source, target)) return new UnavailableTranslator(TranslatorStatus.Unsupported);
        return translation.IsInstalled(source, target) ? translation.Create(source, target) : new UnavailableTranslator(TranslatorStatus.NeedsDownload);
    }

    /// <summary>DeepL yalnız İngilizce→Türkçe için açılır (ayar ekranındaki dil seçenekleri de bunlar).</summary>
    private bool OnlineTranslationActive =>
        settings.OnlineTranslation && settings.SourceLanguage == "en" && settings.TargetLanguage == "tr" && HasDeepLKey;

    /// <summary>Kayıtlı bir DeepL anahtarı var mı (anahtarın kendisi arayüze hiç dönmez).</summary>
    public bool HasDeepLKey => store.Load<string>(StoreKeys.DeepLKey) is { Length: > 0 };

    public string DeepLNotice
    {
        get
        {
            if (!OnlineTranslationActive || store.Load<string>(StoreKeys.DeepLKey) is not { } key) return "";
            var reason = services.OnlineTranslationError(key) switch
            {
                OnlineKeyState.QuotaExceeded => "DeepL kotası doldu.",
                OnlineKeyState.Invalid => "DeepL anahtarı kabul edilmedi.",
                OnlineKeyState.Unreachable => "DeepL zamanında yanıt vermedi.",
                _ => ""
            };
            if (reason.Length == 0) return "";
            return reason + (ModelInstalled ? " Çeviri bilgisayarda devam ediyor." : " Devam etmek için yerel çeviri modelini indir.");
        }
    }

    /// <summary>Anahtar denenir; geçerliyse (kotası bitmiş olsa da) şifreli depoya yazılır ve çevrimiçi çeviri açılır.</summary>
    public async Task<OnlineKeyCheck> SetDeepLKeyAsync(string key)
    {
        key = key.Trim();
        var check = await services.CheckOnlineKeyAsync(key);
        if (check.State is OnlineKeyState.Valid or OnlineKeyState.QuotaExceeded)
        {
            store.Save(StoreKeys.DeepLKey, key);
            Changed(nameof(HasDeepLKey));
            if (settings.OnlineTranslation && isRunning) MakePipeline();
            else Settings = settings with { OnlineTranslation = true };
            RefreshTranslatorStatus();
        }
        return check;
    }

    /// <summary>Kayıtlı anahtarın bu ayki kullanımı.</summary>
    public async Task<OnlineKeyCheck> CheckDeepLKeyAsync()
    {
        if (store.Load<string>(StoreKeys.DeepLKey) is not { Length: > 0 } key)
            return new OnlineKeyCheck(OnlineKeyState.Invalid);
        var check = await services.CheckOnlineKeyAsync(key);
        Changed(nameof(DeepLNotice));
        return check;
    }

    public void RemoveDeepLKey()
    {
        store.Remove(StoreKeys.DeepLKey);
        Changed(nameof(HasDeepLKey));
        Settings = settings with { OnlineTranslation = false };
        RefreshTranslatorStatus();
    }

    private void RefreshTranslatorStatus()
    {
        var translation = services.Translation;
        TranslatorStatus = OnlineTranslationActive ? TranslatorStatus.Ready
            : !translation.IsSupported(settings.SourceLanguage, settings.TargetLanguage) ? TranslatorStatus.Unsupported
            : translation.IsInstalled(settings.SourceLanguage, settings.TargetLanguage) ? TranslatorStatus.Ready
            : TranslatorStatus.NeedsDownload;
        Changed(nameof(DeepLNotice));
    }

    /// <summary>PS5'in adresi değişti: sonraki açılışlar yeni adresle başlasın.</summary>
    private void ConsoleMoved(RegisteredConsole moved)
    {
        if (console is null) return;
        console = moved;
        store.Save(StoreKeys.Console, moved);
        Changed(nameof(Console));
    }

    private void SessionStateChanged(SessionState state)
    {
        SessionState = state;
        UpdateTroubleClocks();
        // Bağlantı koptuysa TV'de eski altyazı kalmasın.
        if (state.Phase == SessionPhase.Disconnected)
        {
            pipeline?.Reset();
            server.Clear();
            screenWork?.Cancel();
            screenWork = null;
            HideScreen();
            ClearWorldOverlay();
            dubber?.Stop();
        }
    }

    /// <summary>
    /// Ayrı OCR/çeviri hattı. Aynı anda tek menü taraması sürer, sonuçlar hazır oldukça gösterilir.
    /// </summary>
    private void ScheduleWorldText(Frame frame, Frame? previousFrame)
    {
        if (!settings.AutoWorldText || worldReader is null || worldTranslator is null) return;
        if (screenWork is not null || DateTime.UtcNow < screenShownUntil) return;
        if (pipeline?.IsTranslating == true) return;
        var adaptive = IsPcMode && settings.PcPerformanceProfile;
        var scanTick = Environment.TickCount64;
        if (adaptive ? !pcScanPolicy.ShouldScan(scanTick, changedWorldRegions.Count > 0, worldOverlay?.Detailed == true)
            : changedWorldRegions.Count == 0 && DateTime.UtcNow < nextWorldScan) return;
        if (Interlocked.CompareExchange(ref worldBusy, 1, 0) != 0) return;
        var priorityScan = adaptive && pcScanPolicy.PriorityScanPending;
        if (adaptive) pcScanPolicy.MarkScan(scanTick);
        // Several changed areas usually signal a new menu. A full read can also discover previously unseen panels.
        var regions = !priorityScan && changedWorldRegions.Count is > 0 and <= 4 ? changedWorldRegions.ToArray() : null;
        if (regions is null) nextWorldScan = DateTime.UtcNow.AddMilliseconds(1200);
        var scanWork = CancellationTokenSource.CreateLinkedTokenSource(worldWork.Token);
        worldScanWork = scanWork;
        var cancellation = scanWork.Token;
        var reader = worldReader;
        var translator = worldTranslator;
        var scope = CurrentTranslationMemoryScope;
        var subtitleRegion = settings.Region;
        var display = settings.PcDisplayId;
        var pc = settings.Source == GameSource.Pc;
        var now = DateTime.UtcNow;
        var quickRead = pc && regions is null && (priorityScan || (adaptive
            ? pcScanPolicy.Moving || previousFrame is null || now < nextDetailedPcScan
            : now < nextDetailedPcScan || previousFrame is null || !ScreenTranslation.SameScreen(previousFrame, frame)));
        if (pc && regions is null && !quickRead) nextDetailedPcScan = now.AddSeconds(6);
        var englishToTurkish = EnglishToTurkishScreen;
        var revision = Volatile.Read(ref worldRevision);
        var previousOverlay = worldOverlay;
        bool IsCurrentScan() => revision == Volatile.Read(ref worldRevision);
        TextLine? CurrentSubtitle() => pipeline?.CurrentSubtitle;
        _ = Task.Run(async () =>
        {
            try
            {
                var nextProgress = 0L;
                var results = await ScreenTranslation.TranslateOverlayAsync(
                    frame, reader, translator, subtitleRegion, progress: items =>
                    {
                        var ticks = Environment.TickCount64;
                        if (ticks < nextProgress) return;
                        nextProgress = ticks + 120;
                        post(() => ShowResults(items, complete: false));
                    },
                    cancellationToken: cancellation, isCurrent: IsCurrentScan,
                    englishToTurkish: englishToTurkish, currentSubtitle: CurrentSubtitle,
                    isRegionCurrent: box => IsCurrentTextRegion(frame, box), regions: regions,
                    waitForModel: WaitForDialogueAsync, quickRead: quickRead)
                    .ConfigureAwait(false);
                post(() => ShowResults(results, complete: true));
            }
            catch (OperationCanceledException) { }
            catch (Exception) { /* İkincil yazı okuma hatası konuşma dublajını durdurmaz. */ }
            finally
            {
                post(() =>
                {
                    if (ReferenceEquals(worldScanWork, scanWork))
                    {
                        worldScanWork = null;
                        if (adaptive && cancellation.IsCancellationRequested)
                            pcScanPolicy.RetryAfterCancellation();
                    }
                    scanWork.Dispose();
                    Volatile.Write(ref worldBusy, 0);
                });
            }

            void ShowResults(IReadOnlyList<ScreenTranslation.Item> items, bool complete)
            {
                if (cancellation.IsCancellationRequested || screenWork is not null ||
                    DateTime.UtcNow < screenShownUntil || !IsCurrentScan()) return;
                if (complete) changedWorldRegions.Clear();
                var visible = KeepCurrentItems(frame, items, CurrentSubtitle());
                RecordScreenTranslations(scope, visible);
                if ((!complete || regions is not null || quickRead) && previousOverlay is { } previous)
                {
                    var unchanged = KeepCurrentItems(previous.Frame, previous.Items, CurrentSubtitle())
                        .Where(item => ScreenTranslation.SameTextRegion(previous.Frame, frame, item.Box)).ToArray();
                    visible = ScreenTranslation.MergeOverlayItems(visible, unchanged);
                }
                if (visible.Count == 0)
                {
                    if (complete && worldOverlay is not null) ClearWorldOverlay();
                    return;
                }
                var sameOverlay = worldOverlay?.Items.SequenceEqual(visible) == true;
                worldOverlay = new ScreenOverlay(frame, visible, complete && regions is null && !quickRead);
                if (sameOverlay && pc && pcOverlay is { } displayed &&
                    displayed.Until - Environment.TickCount64 > WorldOverlayRenewalMargin) return;
                PublishOverlay(visible, WorldOverlayDuration, pc, display);
            }
        });
    }

    private async Task WaitForDialogueAsync(CancellationToken cancellation)
    {
        while (Volatile.Read(ref pipeline)?.IsTranslating == true)
            await Task.Delay(25, cancellation).ConfigureAwait(false);
    }

    private async void HandleFrame(Frame frame, int generation)
    {
        PublishFrame(frame, generation);
        await ProcessSubtitleFrameAsync(frame, settings.Region, generation, cropped: false);
    }

    private void PublishFrame(Frame frame, int generation)
    {
        bool queueUpdate;
        lock (frameGate)
        {
            if (!IsCurrentSession(generation)) return;
            Volatile.Write(ref latestScreenFrame, frame);
            queueUpdate = !frameUpdateQueued;
            frameUpdateQueued = true;
        }
        if (queueUpdate) post(UpdateLatestFrame);
    }

    private async Task ProcessSubtitleFrameAsync(Frame frame, CropRegion region, int generation, bool cropped)
    {
        // Duraklatıldıysa kare yalnız ek bölgeler ve serbest alan için saklanır; altyazı okunmaz.
        if (subtitlesPaused) return;
        // Önceki kare hâlâ işleniyorsa bu kare atlanır; okuma kareleri biriktirmez.
        var current = pipeline;
        if (current is null || !IsCurrentSession(generation) || Interlocked.Exchange(ref processing, 1) == 1) return;
        try
        {
            if (cropped) await current.ProcessAreaAsync(frame, region);
            else await current.ProcessAsync(frame, region);
        }
        catch
        {
            // Tek bir karenin okunamaması akışı durdurmaz.
        }
        finally
        {
            Volatile.Write(ref processing, 0);
        }
    }

    private void UpdateLatestFrame()
    {
        Frame? latest;
        lock (frameGate)
        {
            frameUpdateQueued = false;
            latest = latestScreenFrame;
        }
        if (latest is null) return;
        var previous = LastFrame;
        LastFrame = latest;
        if (IsPcMode && settings.PcPerformanceProfile && settings.AutoWorldText)
        {
            if (pcScanPolicy.Observe(latest, Environment.TickCount64)) worldScanWork?.Cancel();
            Volatile.Write(ref pcFullCaptureInterval, pcScanPolicy.FullCaptureInterval);
        }
        RefreshScreenOverlay();
        ScheduleWorldText(latest, previous);
    }

    private async Task StartServerAsync()
    {
        if (serverStarted) return;
        try
        {
            ServerPort = await server.StartAsync();
        }
        catch (SocketException)
        {
            // 8787 başka bir uygulamada; TV'ye adres her seferinde bildirildiği için herhangi bir boş port olur.
            server.ClientsChanged -= OnClientsChanged;
            server = new SubtitleServer(0);
            server.ClientsChanged += OnClientsChanged;
            ServerPort = await server.StartAsync();
        }
        serverStarted = true;
    }

    private void OnClientsChanged(int count) => post(() =>
    {
        TvClients = count;
        UpdateTroubleClocks();
    });

    /// <summary>TV ile aynı ağdaki bilgisayar adresi.</summary>
    private string ServerAddress(LgTv target) => $"http://{LocalAddress(target.Host) ?? "127.0.0.1"}:{ServerPort}";

    public static string? LocalAddress(string near)
    {
        if (!IPAddress.TryParse(near, out var target) || target.AddressFamily != AddressFamily.InterNetwork) return null;
        string? fallback = null;
        foreach (var network in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (network.OperationalStatus != OperationalStatus.Up || network.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            foreach (var unicast in network.GetIPProperties().UnicastAddresses)
            {
                if (unicast.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                var mask = unicast.IPv4Mask.GetAddressBytes();
                var address = unicast.Address.GetAddressBytes();
                var wanted = target.GetAddressBytes();
                if (Enumerable.Range(0, 4).All(i => (address[i] & mask[i]) == (wanted[i] & mask[i]))) return unicast.Address.ToString();
                fallback ??= unicast.Address.ToString();
            }
        }
        return fallback;
    }

    // --- Windows Güvenlik Duvarı ---

    /// <summary>PS5 bu kadar süre "Yeniden bağlanıyor…" kalırsa güvenlik duvarı kuralına bakılır.</summary>
    public static readonly TimeSpan Ps5TroubleDelay = TimeSpan.FromSeconds(20);
    /// <summary>TV uygulaması bu kadar süre bağlanmazsa güvenlik duvarı kuralına bakılır.</summary>
    public static readonly TimeSpan TvWaitDelay = TimeSpan.FromSeconds(60);

    private FirewallPrompt firewallPrompt;
    /// <summary>Bu çeviri oturumunda kural bulundu; her denetimde kurallar yeniden taranmaz.</summary>
    private bool firewallRuleSeen;
    private bool checkingFirewall;
    private DateTime? ps5TroubleSince;
    private DateTime? tvWaitingSince;

    /// <summary>Güvenlik duvarı kartı ya da eşleştirme adımındaki düğme; yalnız kural eksikken önerilir.</summary>
    public FirewallPrompt FirewallPrompt { get => firewallPrompt; private set => Set(ref firewallPrompt, value); }

    /// <summary>Zaman (testlerde ileri sarılır).</summary>
    public Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;

    /// <summary>
    /// Ana ekran birkaç saniyede bir çağırır: PS5 20 saniyedir yeniden bağlanıyorsa ya da TV 60 saniyedir bağlanmadıysa ve
    /// güvenlik duvarında CouchSubs'a izin yoksa kart önerilir. İkisi de bağlanınca kart kalkar.
    /// </summary>
    public async Task CheckConnectionsAsync()
    {
        UpdateTroubleClocks();
        if (!isRunning || services.Firewall is not { } firewall) return;
        if (ps5TroubleSince is null && tvWaitingSince is null)
        {
            ClearFirewallPrompt();
            return;
        }
        var now = Clock();
        var stuck = now - ps5TroubleSince >= Ps5TroubleDelay || now - tvWaitingSince >= TvWaitDelay;
        if (!stuck || firewallPrompt != FirewallPrompt.None || firewallRuleSeen || checkingFirewall) return;
        checkingFirewall = true;
        try
        {
            var hasRule = await HasFirewallRuleAsync(firewall);
            if (!isRunning) return;
            if (hasRule) firewallRuleSeen = true;
            else FirewallPrompt = FirewallPrompt.Offer;
        }
        finally
        {
            checkingFirewall = false;
        }
    }

    /// <summary>
    /// Kuralı yönetici izniyle ekler. Çeviri sürüyorsa PS5 ve TV hemen yeniden denenir; eşleştirmede kullanıcı yeni
    /// kodla yeniden bağlanır.
    /// </summary>
    public async Task<bool> AllowThroughFirewallAsync()
    {
        if (services.Firewall is not { } firewall || firewallPrompt == FirewallPrompt.Working) return false;
        FirewallPrompt = FirewallPrompt.Working;
        bool added;
        try { added = await firewall.AddRuleAsync(); }
        catch (Exception) { added = false; }
        FirewallPrompt = added ? FirewallPrompt.Allowed : FirewallPrompt.Failed;
        if (!added) return false;
        firewallRuleSeen = true;
        if (isRunning)
        {
            session?.ReconnectNow();
            keeper?.Reconnect();
        }
        return true;
    }

    /// <summary>Eşleştirme başarısız oldu: kural eksikse düğme önerilir.</summary>
    private async Task OfferFirewallAsync()
    {
        if (services.Firewall is not { } firewall || firewallPrompt == FirewallPrompt.Working) return;
        FirewallPrompt = await HasFirewallRuleAsync(firewall) ? FirewallPrompt.None : FirewallPrompt.Offer;
    }

    /// <summary>Kurallar arka planda taranır; okunamazsa kural var sayılır (yersiz öneri yapılmaz).</summary>
    private static async Task<bool> HasFirewallRuleAsync(IFirewall firewall)
    {
        try { return await Task.Run(firewall.HasRule); }
        catch (Exception) { return true; }
    }

    private void ClearFirewallPrompt()
    {
        if (firewallPrompt != FirewallPrompt.Working) FirewallPrompt = FirewallPrompt.None;
    }

    /// <summary>
    /// PS5'in ve TV'nin ne zamandan beri bağlanamadığı. PS5 uykudaysa, başka cihazdaysa ya da bağlantıyı reddettiyse
    /// sorun güvenlik duvarı değildir; sayılmaz.
    /// </summary>
    private void UpdateTroubleClocks()
    {
        var now = Clock();
        var ps5Trouble = isRunning && sessionState.Phase != SessionPhase.Streaming && sessionState.Reason?.Kind is not
            (DisconnectKind.ConsoleAsleep or DisconnectKind.InUse or DisconnectKind.Refused or DisconnectKind.LoginPasscode);
        ps5TroubleSince = ps5Trouble ? ps5TroubleSince ?? now : null;
        tvWaitingSince = isRunning && tvClients == 0 ? tvWaitingSince ?? now : null;
    }

    // --- Uygulama güncellemesi ---

    /// <summary>GitHub'da daha yeni sürüm varsa numarası.</summary>
    public string? UpdateVersion { get => updateVersion; private set => Set(ref updateVersion, value); }
    public bool Updating { get => updating; private set => Set(ref updating, value); }
    public double UpdateProgress { get => updateProgress; private set => Set(ref updateProgress, value); }
    public string? UpdateError { get => updateError; private set => Set(ref updateError, value); }
    /// <summary>Kurulum dosyasıyla kurulduysa tek tıkla güncellenir; değilse sürüm sayfası açılır.</summary>
    public bool CanUpdateItself => services.Updater?.CanUpdateItself == true;

    /// <summary>Açılışta bir kez; ağ yoksa sessizce geçer.</summary>
    public async Task CheckForUpdateAsync()
    {
        if (services.Updater is not { } updater) return;
        try { UpdateVersion = await updater.CheckAsync(); }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or IOException or InvalidOperationException) { }
    }

    public async Task InstallUpdateAsync()
    {
        if (services.Updater is not { } updater || updating) return;
        if (!updater.CanUpdateItself)
        {
            OpenInBrowser(updater.DownloadPage);
            return;
        }
        Updating = true;
        UpdateError = null;
        try
        {
            // Çeviri sürüyorsa önce durur; uygulama güncellenip yeniden açılır.
            Stop();
            await updater.InstallAsync(new ImmediateProgress(value => post(() => UpdateProgress = value)));
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or IOException or InvalidOperationException
                                          or UnauthorizedAccessException)
        {
            UpdateError = error.Message;
        }
        finally
        {
            Updating = false;
        }
    }

    // --- Türkçe dublaj ---

    /// <summary>Bu derlemede dublaj var mı (ses motoru uygulamayla geliyor mu).</summary>
    public bool DubbingAvailable => services.Dubbing is not null || services.FastDubbing is not null;
    public bool FastDubbingAvailable => services.FastDubbing is not null;
    /// <summary>Ayarlarda seçili motor; hızlı ses yoksa doğal sese düşer.</summary>
    private IDubbingModel? ActiveDubbing =>
        settings.Dubbing.Engine == DubbingEngine.Fast && services.FastDubbing is { } fast ? fast : services.Dubbing;
    public bool DubbingInstalled => ActiveDubbing?.IsInstalled == true;
    public long DubbingDownloadBytes => services.Dubbing?.DownloadBytes ?? 0;
    public bool DubbingDownloading { get => dubbingDownloading; private set => Set(ref dubbingDownloading, value); }
    public double DubbingProgress { get => dubbingProgress; private set => Set(ref dubbingProgress, value); }
    public string? DubbingError { get => dubbingError; private set => Set(ref dubbingError, value); }

    /// <summary>Ses modelini indirir (ilk kullanımda bir kez, ~440 MB).</summary>
    public async Task DownloadDubbingAsync()
    {
        if (services.Dubbing is not { } model || dubbingDownloading || model.IsInstalled) return;
        DubbingDownloading = true;
        DubbingError = null;
        try
        {
            await Task.Run(() => model.DownloadAsync(new ImmediateProgress(value => post(() => DubbingProgress = value)), CancellationToken.None));
            if (settings.Dubbing.Enabled && isRunning) MakeDubber()?.Prepare();
        }
        catch (Exception error) when (error is HttpRequestException or IOException or InvalidDataException or TaskCanceledException
                                          or UnauthorizedAccessException)
        {
            DubbingError = error.Message;
        }
        finally
        {
            DubbingDownloading = false;
            Changed(nameof(DubbingInstalled));
        }
    }

    /// <summary>Ayarlardaki "Dinle": sesi bilgisayardan çalar.</summary>
    public async Task PreviewVoiceAsync(DubbingVoice voice)
    {
        if (!DubbingInstalled) return;
        DubbingError = null;
        try { await StartServerAsync(); } catch (SocketException) { return; }
        MakeDubber()?.Preview(voice);
    }

    public IReadOnlyList<string> KnownGames => GameCastLibrary.Games(settings.Dubbing);

    public string InterruptionGuardSummary =>
        $"Şüpheli hızlı konuşmacı geçişi: {speakerTurnGuard.PossibleInterruptions} · " +
        $"güvenli ses atama geri çekilmesi: {speakerTurnGuard.Abstentions}. " +
        "Bu sayaçlar üst üste iki sesin kesin algılandığı anlamına gelmez.";

    /// <summary>Snapshot only: counts/timing, never game audio or dialogue text.</summary>
    public string AiPerformanceSummary
    {
        get
        {
            if (settings.Dubbing.SpeechDetection == SpeechDetectionEngine.TextOnly)
                return "Konuşma yapay zekâsı kapalı · Metin tabanlı dublaj etkin";
            var vad = services.NeuralSpeech?.Statistics ?? default;
            var speaker = services.SpeakerDetector?.Statistics ?? default;
            static string Show(string label, AiPerformanceSnapshot x) =>
                $"{label}: {x.InferenceCalls} işlem · ort. {x.AverageInferenceMs:F1} ms · " +
                $"en yüksek {x.PeakInferenceMs:F1} ms · hata {x.InferenceErrors}";
            return Show("Silero", vad) + Environment.NewLine + Show("Konuşmacı", speaker);
        }
    }


    /// <summary>Rule-based, passive diagnostics; never touches PS5 PCM or restarts the TTS engine.</summary>
    public string DubbingHealthSummary
    {
        get
        {
            var timing = dubber?.Timing ?? default;
            var latency = dubber?.SynthesisLatency ?? default;
            var neural = services.NeuralSpeech?.Statistics ?? default;
            var input = new DubbingHealthInputs(
                isRunning, settings.Dubbing.Enabled, DubbingInstalled,
                timing.PendingLines, Dubber.Backlog, timing.LastSynthesisMilliseconds,
                latency.Calls, latency.AverageMs, latency.SlowCalls,
                dubber?.LateLines ?? 0, dubber?.SuppressedDuplicates ?? 0,
                neural.InferenceCalls, neural.InferenceErrors, dubbingError);
            var health = DubbingHealthEvaluator.Evaluate(input);
            var audioHealth = services.GameAudio is { } output && pulseAudioActive
                ? longSessionAudioMonitor.Observe(output.AudioBufferStatistics, Environment.TickCount64)
                : default;
            var audioStatus = !pulseAudioActive ? "PULSE 3D ortak ses çıkışı kapalı"
                : !audioHealth.ValidWindow ? "Ses tamponu: ölçüm aralığı bekleniyor"
                : $"Ses tamponu: {audioHealth.DroppedSamples} atılan örnek · " +
                  $"{audioHealth.UnderrunSamples} boş okuma · " +
                  (audioHealth.BacklogWarning ? "gecikme riski" : "kuyruk normal");
            return $"{health.Level}: {health.Summary}" + Environment.NewLine +
                $"TTS ort.: {latency.AverageMs:F0} ms · en yüksek: {latency.PeakMs} ms · " +
                $"yavaş işlem: {latency.SlowCalls}" + Environment.NewLine +
                $"Kuyruk: {timing.PendingLines}/{Dubber.Backlog} · geciken: {dubber?.LateLines ?? 0} · " +
                $"tekrar bastırma: {dubber?.SuppressedDuplicates ?? 0}" + Environment.NewLine +
                $"Konuşma algılama: {neural.InferenceCalls} işlem · {neural.InferenceErrors} hata" +
                Environment.NewLine + audioStatus +
                Environment.NewLine + health.Recommendation;
        }
    }

    /// <summary>İsim verilmezse Genel; eski oyun sesleri saklanır.</summary>
    public void SelectGame(string name)
    {
        manualGamePinned = true;
        suppressedAutoGames.Remove(GameCastLibrary.Normalize(name));
        speakerTurnGuard.Reset();
        multiSpeakerTracker.Reset();
        var selected = GameCastLibrary.Switch(settings.Dubbing, name);
        if (ReferenceEquals(selected, settings.Dubbing)) return;
        Settings = settings with { Dubbing = selected };
        Changed(nameof(KnownGames));
    }

    /// <summary>Conservative OCR title-screen hint. Never assume game name from console IP.</summary>
    private void AutoSelectRecognizedGame(string text)
    {
        if (!settings.Dubbing.AutoGameLibrary || manualGamePinned) return;
        var game = GameTitleDetector.Detect(text);
        if (game is null || suppressedAutoGames.Contains(game) ||
            string.Equals(settings.Dubbing.ActiveGame, game, StringComparison.OrdinalIgnoreCase))
            return;
        var switched = GameCastLibrary.Switch(settings.Dubbing, game);
        Settings = settings with { Dubbing = switched };
        Changed(nameof(KnownGames));
    }

    private async Task PollPs5GameTitleAsync(CancellationToken stop)
    {
        string? previous = null;
        var confirmations = 0;
        while (!stop.IsCancellationRequested)
        {
            try
            {
                var running = await Ps5RunningGameDiscovery.ProbeAsync(console?.Host ?? "", stop).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(running))
                {
                    var title = GameTitleDetector.Detect(running) ?? GameCastLibrary.Normalize(running);
                    if (string.Equals(previous, title, StringComparison.OrdinalIgnoreCase))
                        confirmations++;
                    else
                    {
                        previous = title;
                        confirmations = 1;
                    }
                    // Require two independent console observations before switching.
                    if (confirmations >= 2)
                        post(() => { if (!stop.IsCancellationRequested) AutoSelectDiscoveredGame(title); });
                }
                else { previous = null; confirmations = 0; }
                await Task.Delay(confirmations >= 2 ? TimeSpan.FromSeconds(25) :
                    TimeSpan.FromSeconds(3), stop).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
            catch { /* Discovery is advisory; do not interrupt game audio. */ }
        }
    }

    private void AutoSelectDiscoveredGame(string game)
    {
        if (!isRunning || !settings.Dubbing.AutoGameLibrary || manualGamePinned) return;
        if (suppressedAutoGames.Contains(game) ||
            string.Equals(settings.Dubbing.ActiveGame, game, StringComparison.OrdinalIgnoreCase)) return;
        var switched = GameCastLibrary.Switch(settings.Dubbing, game);
        Settings = settings with { Dubbing = switched };
        Changed(nameof(KnownGames));
    }

    public string ExportGameLibrary(string game) => GameCastLibrary.ExportJson(settings.Dubbing, game);

    public string ImportGameLibrary(string json, bool overwrite)
    {
        var restored = GameCastLibrary.ImportJson(settings.Dubbing, json, overwrite);
        Settings = settings with { Dubbing = restored };
        Changed(nameof(KnownGames));
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var imported = doc.RootElement.GetProperty("Game").GetString() ?? GameCastLibrary.DefaultGame;
        suppressedAutoGames.Remove(imported);
        return imported;
    }

    public void DeleteGameLibrary(string game)
    {
        services.SpeakerDetector?.ForgetGame(game);
        var cleaned = GameCastLibrary.Delete(settings.Dubbing, game);
        suppressedAutoGames.Add(GameCastLibrary.Normalize(game));
        manualGamePinned = false; // Allow the detected game to become active again after deletion.
        Settings = settings with { Dubbing = cleaned };
        Changed(nameof(KnownGames));
    }

    /// <summary>
    /// Dialogue names are discovered once per shown line. A vocative such as
    /// "Lucia, run!" addresses Lucia; it does NOT identify who is talking.
    /// Only a clearly labelled speaker can be mapped to a character automatically.
    /// </summary>
    private void DiscoverDialogueNames(string english)
    {
        if (string.IsNullOrWhiteSpace(english)) return;
        var cast = ActiveVoiceCast(settings.Dubbing);
        var changed = false;
        var speaker = DialogueNameCandidates.ExplicitSpeaker(english);
        if (speaker is not null && !cast.Assignments.ContainsKey(VoiceCast.Key(speaker)))
        {
            cast.SetGroup(speaker, cast.GroupFor(speaker));
            changed = true;
        }
        foreach (var name in DialogueNameCandidates.Mentioned(english))
            changed |= cast.SuggestName(name);
        if (changed)
            Settings = settings with
            {
                Dubbing = GameCastLibrary.SaveCurrent(settings.Dubbing with { Cast = cast })
            };
    }

    public void RenameCharacter(string originalName, string newName)
    {
        var cast = settings.Dubbing.Cast.Copy();
        cast.Rename(originalName, newName);
        services.SpeakerDetector?.RenameSpeaker(originalName, newName);
        Settings = settings with { Dubbing = GameCastLibrary.SaveCurrent(settings.Dubbing with { Cast = cast }) };
    }

    public void AcceptCharacterSuggestion(string name)
    {
        var cast = settings.Dubbing.Cast.Copy();
        cast.AcceptSuggestion(name);
        Settings = settings with { Dubbing = GameCastLibrary.SaveCurrent(settings.Dubbing with { Cast = cast }) };
    }

    public void DismissCharacterSuggestion(string name)
    {
        var cast = settings.Dubbing.Cast.Copy();
        cast.DismissSuggestion(name);
        Settings = settings with { Dubbing = GameCastLibrary.SaveCurrent(settings.Dubbing with { Cast = cast }) };
    }

    public void AddCharacter(string name, CharacterGroup group)
    {
        name = name.Trim();
        if (name.Length is 0 or > 64) return;
        var cast = ActiveVoiceCast(settings.Dubbing);
        cast.SetGroup(name, group);
        Settings = settings with { Dubbing = GameCastLibrary.SaveCurrent(settings.Dubbing with { Cast = cast }) };
    }

    public void SetCharacterGroup(string name, CharacterGroup group)
    {
        var cast = ActiveVoiceCast(settings.Dubbing);
        cast.SetGroup(name, group);
        Settings = settings with { Dubbing = GameCastLibrary.SaveCurrent(settings.Dubbing with { Cast = cast }) };
    }

    public void RemoveCharacter(string name)
    {
        var cast = settings.Dubbing.Cast.Copy();
        cast.Remove(name);
        services.SpeakerDetector?.ForgetSpeaker(name);
        Settings = settings with { Dubbing = GameCastLibrary.SaveCurrent(settings.Dubbing with { Cast = cast }) };
    }

    public void AssignVoice(DubbingVoice voice, string name)
    {
        var cast = ActiveVoiceCast(settings.Dubbing);
        cast.Assign(voice, name);
        Settings = settings with { Dubbing = GameCastLibrary.SaveCurrent(settings.Dubbing with { Cast = cast }) };
    }

    public void ReassignCharacterVoice(string name)
    {
        var cast = ActiveVoiceCast(settings.Dubbing);
        cast.ReassignAutomatically(name);
        Settings = settings with { Dubbing = GameCastLibrary.SaveCurrent(settings.Dubbing with { Cast = cast }) };
    }

    private static VoiceCast ActiveVoiceCast(DubbingSettings settings) =>
        settings.Cast.Copy(GameTitleDetector.Detect(settings.ActiveGame) == "GTA 6");

    private Dubber? MakeDubber()
    {
        if (dubber is not null) return dubber;
        if (ActiveDubbing is not { } model || services.Audio is not { } fallbackAudio) return null;
        var audio = settings.Source != GameSource.Pc && settings.Dubbing.Output == DubbingOutput.Headset && pulseAudioActive && services.GameAudio is { } game
            ? (IAudioOutput)game : fallbackAudio;
        var made = new Dubber(model, audio, () => server, () => ServerPort, post)
        {
            Cast = ActiveVoiceCast(settings.Dubbing),
            Engine = settings.Dubbing.Engine,
            EmotionMode = settings.Dubbing.EmotionMode,
            ExpressiveSafety = settings.Dubbing.ExpressiveSafety,
            FinishCurrentLine = settings.Source == GameSource.Pc,
            SmartTiming = settings.Dubbing.SmartTiming,
            Prosody = gameProsody,
            NeuralSpeech = services.NeuralSpeech,
            Output = settings.Source == GameSource.Pc ? DubbingOutput.Computer : settings.Dubbing.Output,
        };
        made.CastChanged += cast =>
        {
            if (!ReferenceEquals(dubber, made)) return;
            // A concurrently discovered dialogue-name hint belongs to the
            // game library, not to the Dubber snapshot; never wipe it out.
            foreach (var (name, observations) in settings.Dubbing.Cast.SuggestedNames)
                if (!cast.SuggestedNames.ContainsKey(name) &&
                    !cast.Assignments.ContainsKey(name))
                    cast.SuggestedNames[name] = observations;
            Settings = settings with
            {
                Dubbing = GameCastLibrary.SaveCurrent(settings.Dubbing with { Cast = cast })
            };
        };
        made.Failed += message => DubbingError = message;
        made.PlaybackStarted += line => InspectionPlayback?.Invoke(line);
        dubber = made;
        return made;
    }

    private void Speak(IReadOnlyList<SpokenLine> lines, Func<bool> canStart)
    {
        if (!settings.Dubbing.Enabled || !DubbingInstalled) return;
        // Subtitle labels win. When absent, use only a fresh high-confidence
        // voice embedding result; never invent a real character name.
        var recent = services.SpeakerDetector?.Latest;
        string? candidate = null;
        if (settings.Source == GameSource.Ps5 && settings.Dubbing.AutoSpeakerTracking)
        {
            if (settings.Dubbing.MultiSpeakerSafety)
            {
                // Multiple sequential identities are tracked without overriding
                // explicit subtitle speakers or stopping the original game sound.
                candidate = multiSpeakerTracker.Evaluate(recent, Environment.TickCount64).Speaker;
            }
            else if (settings.Dubbing.InterruptionGuard)
            {
                // This is an uncertainty gate, not an actual overlap separation
                // model. Explicitly labelled subtitles bypass the gate below.
                var decision = speakerTurnGuard.Evaluate(recent, Environment.TickCount64);
                candidate = decision.Speaker;
            }
            else if (recent is
                { Name: not null, Source: SpeakerIdentitySource.VoiceEmbedding, Confidence: >= 0.83f })
                candidate = recent.Name;
        }

        if (candidate is not null)
            MakeDubber()?.Speak(lines.Select(line =>
                string.IsNullOrWhiteSpace(line.Speaker)
                    ? line with { Speaker = candidate } : line).ToArray(), canStart);
        else
            MakeDubber()?.Speak(lines, canStart);
    }

    /// <summary>
    /// Starts the PS5 headset mixer without changing the Remote Play session.
    /// Safe to call again after a failed device initialization. Does not
    /// interfere with native PS5/HDMI audio or TV output.
    /// </summary>
    /// <summary>
    /// Experimental *read-only* model probe for PS5 audio. It cannot modify
    /// the PULSE 3D PCM output and never starts without explicit env opt-in.
    /// Model loading is off the UI/native audio callback, and all faults
    /// are swallowed by this diagnostic startup.
    /// </summary>
    private void TryStartInworldDiagnosticTap()
    {
        if (Environment.GetEnvironmentVariable("COUCHSUBS_ENABLE_INWORLD_DIAGNOSTIC_TAP") != "1" ||
            inworldDiagnosticTap is not null)
            return;
        var python = Environment.GetEnvironmentVariable("COUCHSUBS_INWORLD_PYTHON");
        var script = Environment.GetEnvironmentVariable("COUCHSUBS_INWORLD_WORKER_SCRIPT");
        var device = Environment.GetEnvironmentVariable("COUCHSUBS_INWORLD_DEVICE");
        if (string.IsNullOrWhiteSpace(python) || !Path.IsPathFullyQualified(python) ||
            !File.Exists(python) || string.IsNullOrWhiteSpace(script) ||
            !Path.IsPathFullyQualified(script) || !File.Exists(script))
            return;
        var tap = new InworldLiveDiagnosticTap();
        Volatile.Write(ref inworldDiagnosticTap, tap);
        _ = Task.Run(async () =>
        {
            try { await tap.StartAsync(python, script, device == "cuda" ? "cuda" : "cpu"); }
            catch
            {
                if (ReferenceEquals(Interlocked.CompareExchange(
                    ref inworldDiagnosticTap, null, tap), tap))
                {
                    try { await tap.DisposeAsync(); }
                    catch { /* read-only model probe must not affect game audio */ }
                }
            }
        });
    }

    private void TryStartHeadsetAudio()
    {
        if (settings.Source != GameSource.Ps5 ||
            settings.Dubbing.Output != DubbingOutput.Headset ||
            services.GameAudio is not { } gameAudio || pulseAudioActive) return;
        try
        {
            gameAudio.SetAtmosphereMix(settings.Dubbing.GameAtmospherePercent,
                settings.Dubbing.TurkishVoicePercent);
            gameAudio.SetAtmosphereMode(settings.Dubbing.AtmosphereMode);
            gameAudio.Start();
            pulseAudioActive = true;
            DubbingError = null;
        }
        catch (Exception error)
        {
            DubbingError = "PULSE 3D ses çıkışı açılamadı: " + error.Message;
        }
    }

    private void DubbingSettingsChanged(DubbingSettings old)
    {
        var now = settings.Dubbing;
        if (now.Enabled != old.Enabled)
        {
            if (!now.Enabled)
            {
                // Stop only Turkish speech. Preserve the prepared Pocket TTS
                // engine (hundreds of MB) so re-enabling is immediate. Also
                // keep the PS5 PCM/PULSE mixer and original soundtrack alive.
                dubber?.Stop();
                return;
            }
            // Remote Play was created with audio capture regardless of the
            // checkbox state. Ensure the mixer is ready before a fresh dubber
            // chooses its audio output, even if initialization failed earlier.
            if (isRunning && settings.Source == GameSource.Ps5)
                TryStartHeadsetAudio();
            if (isRunning && DubbingInstalled)
            {
                DubbingError = null;
                // Reuse the existing model instead of spawning a second
                // pocket-tts process on every checkbox toggle.
                (dubber ?? MakeDubber())?.Prepare();
            }
            return;
        }
        if (now.Engine != old.Engine)
        {
            // Motor değişti: eski dublaj kapanır, yenisi hazırlanır.
            StopDubber();
            Changed(nameof(DubbingInstalled));
            if (now.Enabled && isRunning && DubbingInstalled) MakeDubber()?.Prepare();
            return;
        }
        if (dubber is null)
        {
            if (now.Enabled && !old.Enabled && isRunning && DubbingInstalled) MakeDubber()?.Prepare();
            return;
        }
        // Dublajın kendi atadığı ses (CastChanged) zaten dublajda; kullanıcının seçimi kopyalanır.
        if (!ReferenceEquals(now.Cast, old.Cast) || now.ActiveGame != old.ActiveGame) dubber.Cast = ActiveVoiceCast(now);
        if (now.GameAtmospherePercent != old.GameAtmospherePercent ||
            now.TurkishVoicePercent != old.TurkishVoicePercent)
            services.GameAudio?.SetAtmosphereMix(now.GameAtmospherePercent, now.TurkishVoicePercent);
        if (now.AtmosphereMode != old.AtmosphereMode)
            services.GameAudio?.SetAtmosphereMode(now.AtmosphereMode);
        if (now.ExpressiveSafety != old.ExpressiveSafety)
            dubber.ExpressiveSafety = now.ExpressiveSafety;
        if (now.SmartTiming != old.SmartTiming)
            dubber.SmartTiming = now.SmartTiming;
        if (now.MultiSpeakerSafety != old.MultiSpeakerSafety)
            multiSpeakerTracker.Reset();
        if ((now.AutoSpeakerTracking != old.AutoSpeakerTracking ||
             now.SpeechDetection != old.SpeechDetection) && isRunning &&
            settings.Source == GameSource.Ps5)
            DubbingError = "Konuşma algılama ayarı değişti. PS5 bağlantısını durdurup yeniden başlatın.";
        if (now.EmotionMode != old.EmotionMode)
        {
            dubber.EmotionMode = now.EmotionMode;
            if (isRunning && settings.Source == GameSource.Ps5 &&
                (now.EmotionMode is DubbingEmotionMode.Acoustic or DubbingEmotionMode.Neural ||
                 old.EmotionMode is DubbingEmotionMode.Acoustic or DubbingEmotionMode.Neural))
                DubbingError = "Ses destekli modu değiştirdiniz. PS5 ses yakalamayı yenilemek için Çeviriyi Durdur ve Başlat yapın.";
        }
        if (now.Output != old.Output)
        {
            StopDubber(); // Ses cihazı değiştiğinde eski çıkışa konuşma gönderilmesin.
            if (isRunning)
            {
                DubbingError = settings.Source == GameSource.Pc
                    ? null
                    : "Ses çıkışını değiştirdiniz. PS5 oyun sesi bağlantısını yenilemek için Çeviriyi Durdur ve Başlat yapın.";
                if (settings.Source == GameSource.Pc && now.Enabled && DubbingInstalled)
                    MakeDubber()?.Prepare();
            }
        }
        if (!now.Enabled) StopDubber();
    }

    private void StopDubber()
    {
        if (dubber is not { } stopping) return;
        dubber = null;
        _ = stopping.DisposeAsync().AsTask();
    }

    private AppException Error(AppError error) => new(error, AppException.Describe(error, text));

    /// <summary>Değer değiştiyse yazar ve özelliği (ve ona bağlı olanları) bildirir.</summary>
    private bool Set<T>(ref T field, T value, string[]? also = null, [CallerMemberName] string name = "")
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Changed([name, .. also ?? []]);
        return true;
    }

    private void Changed(params string[] names)
    {
        foreach (var name in names) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>Model eksikken ya da dil desteklenmezken kullanılan çevirmen: nedeni bildirir.</summary>
    private sealed class UnavailableTranslator(TranslatorStatus status) : ITextTranslator
    {
        public Task<string> TranslateAsync(string text, CancellationToken cancellationToken = default) => throw new TranslatorException(status);
    }

    /// <summary>İlerlemeyi bağlama yakalamadan doğrudan iletir (post zaten arayüze aktarır).</summary>
    private sealed class ImmediateProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}

/// <summary>Ek bölgenin tek seferlik çevirisinin sonucu.</summary>
public enum ExtraRegionOutcome { Shown, NoText, NotConfigured, NotRunning, Busy, TranslatorUnavailable, Failed }

/// <summary>"Ekranı çevir"in sonucu; <see cref="Cleared"/>: TV'deki çeviriler kaldırıldı ya da süren iş iptal edildi.</summary>
public enum ScreenOutcome { Shown, NoText, Cleared, NotRunning, TranslatorUnavailable, Failed }
