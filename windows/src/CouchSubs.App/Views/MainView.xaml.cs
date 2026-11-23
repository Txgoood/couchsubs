// SPDX-License-Identifier: AGPL-3.0-only

using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CouchSubs.App.Controls;
using CouchSubs.App.Services;
using CouchSubs.Core;
using CouchSubs.Core.Dubbing;
using CouchSubs.Shell;

namespace CouchSubs.App.Views;

/// <summary>Ana ekran: başlat/durdur, PS5 ve TV durumu, uyarılar, son altyazı.</summary>
public partial class MainView : UserControl
{
    private readonly AppController controller;
    private bool selectingSource;
    private int screenActionRevision;
    private readonly TranslationClip inspection = new();
    private CancellationTokenSource? smoothVideoWork;
    private SmoothVideoWindow? smoothVideoWindow;
    private string? smoothDisplay;
    private readonly DispatcherTimer inspectionTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    // Çeviri sürerken PS5 ve TV bağlanamıyorsa güvenlik duvarı kartı önerilir.
    private readonly DispatcherTimer connectionWatch = new() { Interval = TimeSpan.FromSeconds(5) };

    public MainView(AppController controller)
    {
        this.controller = controller;
        DataContext = controller;
        InitializeComponent();
        inspectionTimer.Tick += SampleInspection;
        controller.InspectionSubtitle += RecordSubtitle;
        controller.InspectionPlayback += RecordPlayback;
        controller.PropertyChanged += OnChanged;
        connectionWatch.Tick += async (_, _) => await controller.CheckConnectionsAsync();
        Loaded += (_, _) => connectionWatch.Start();
        Unloaded += (_, _) =>
        {
            controller.PropertyChanged -= OnChanged;
            connectionWatch.Stop();
            StopSmoothVideo();
            inspectionTimer.Stop();
            inspection.Arm(false);
            controller.SetInspectionCapture(false);
            controller.InspectionSubtitle -= RecordSubtitle;
            controller.InspectionPlayback -= RecordPlayback;
        };
        selectingSource = true;
        SourceSelection.SelectedIndex = controller.IsPcMode ? 1 : 0;
        ReloadDisplays();
        selectingSource = false;
        Refresh();
        ShowUpdate();
        _ = controller.CheckForUpdateAsync();
    }

    private void OnSmoothVideo(object sender, RoutedEventArgs e) => ToggleSmoothVideo();

    internal void ToggleSmoothVideo()
    {
        if (smoothVideoWork is not null)
        {
            StopSmoothVideo();
            return;
        }
        if (!controller.IsPcMode || !controller.IsRunning) return;
        var display = controller.Settings.PcDisplayId;
        System.Drawing.Rectangle bounds;
        try { bounds = WindowsGameScreenCapture.BoundsFor(display); }
        catch (InvalidOperationException error)
        {
            SmoothVideoStatus.Text = error.Message;
            return;
        }
        var factor = Math.Min(1, Math.Min(1280.0 / bounds.Width, 720.0 / bounds.Height));
        var width = Math.Max(2, (int)(bounds.Width * factor) & ~1);
        var height = Math.Max(2, (int)(bounds.Height * factor) & ~1);
        var window = new SmoothVideoWindow(display, width, height);
        var work = new CancellationTokenSource();
        smoothDisplay = display;
        smoothVideoWindow = window;
        smoothVideoWork = work;
        window.Closed += (_, _) =>
        {
            if (!ReferenceEquals(smoothVideoWork, work)) return;
            smoothVideoWindow = null;
            StopSmoothVideo();
        };
        SmoothVideoButton.Content = "Akıcı görüntüyü kapat";
        SmoothVideoStatus.Text = "NVIDIA kare üretme hazırlanıyor…";
        _ = RunSmoothVideoAsync(window, bounds, work);
    }

    private async Task RunSmoothVideoAsync(SmoothVideoWindow window, System.Drawing.Rectangle bounds,
        CancellationTokenSource work)
    {
        try
        {
            await Task.Run(() => window.PlayAsync(bounds, () =>
            {
                if (ReferenceEquals(smoothVideoWork, work))
                    SmoothVideoStatus.Text = "Açık · Ctrl+Shift+F8 ile kapat";
            }, work.Token));
        }
        catch (OperationCanceledException) when (work.IsCancellationRequested) { }
        catch (Exception error) when (error is IOException or InvalidOperationException or
                                      Win32Exception or COMException or OperationCanceledException)
        {
            if (ReferenceEquals(smoothVideoWork, work))
            {
                var reason = error is InvalidOperationException &&
                    (error.Message.StartsWith("Akıcı görüntü", StringComparison.Ordinal) ||
                     error.Message.StartsWith("NVIDIA kare üretme bileşeni", StringComparison.Ordinal))
                    ? error.Message
                    : "NVIDIA kare üretme kullanılamadı. Normal görüntüye dönüldü.";
                StopSmoothVideo();
                SmoothVideoStatus.Text = reason;
            }
        }
        finally { work.Dispose(); }
    }

    private void StopSmoothVideo()
    {
        if (smoothVideoWork is not { } work) return;
        smoothVideoWork = null;
        smoothDisplay = null;
        work.Cancel();
        smoothVideoWindow?.Close();
        smoothVideoWindow = null;
        SmoothVideoButton.Content = "Akıcı görüntü · 2×";
        SmoothVideoStatus.Text = "Kapalı · Ctrl+Shift+F8";
    }

    private void OnInspectionToggle(object sender, RoutedEventArgs e)
    {
        if (SaveInspectionButton is null) return;
        var enabled = InspectionEnabled.IsChecked == true;
        inspection.Arm(enabled);
        controller.SetInspectionCapture(enabled);
        SaveInspectionButton.IsEnabled = enabled;
        if (enabled) inspectionTimer.Start();
        else inspectionTimer.Stop();
    }

    private void OnTranslationMemory(object sender, RoutedEventArgs e) =>
        new TranslationMemoryWindow(controller) { Owner = Window.GetWindow(this) }.ShowDialog();

    private void RecordSubtitle(string english, string turkish) =>
        inspection.Add("subtitle", english, turkish);

    private void RecordPlayback(SpokenLine line) =>
        inspection.Add("dubbing-start", line.English, line.Turkish, line.Speaker);

    private async void SampleInspection(object? sender, EventArgs e)
    {
        if (!controller.IsRunning || controller.LastFrame is not { } frame) return;
        try { await inspection.SampleAsync(frame); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or COMException)
        {
            InspectionStatus.Text = "Ekran karesi inceleme kaydı için hazırlanamadı.";
        }
    }

    private async void OnSaveInspection(object sender, RoutedEventArgs e) => await SaveInspectionAsync();

    internal async Task SaveInspectionAsync()
    {
        if (InspectionEnabled.IsChecked != true || !SaveInspectionButton.IsEnabled)
        {
            InspectionStatus.Text = "Önce son 20 saniyeyi bellekte tutma seçeneğini aç.";
            return;
        }
        SaveInspectionButton.IsEnabled = false;
        try
        {
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "CouchSubs İnceleme Kayıtları");
            var directory = await inspection.SaveAsync(root);
            InspectionStatus.Text = directory is null
                ? "Henüz görüntü yok. Çeviriyi başlatıp birkaç saniye bekle."
                : $"Kaydedildi: {directory}";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            InspectionStatus.Text = "İnceleme dosyası kaydedilemedi.";
        }
        finally
        {
            SaveInspectionButton.IsEnabled = InspectionEnabled.IsChecked == true;
        }
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppController.IsRunning) or nameof(AppController.IsStarting) or nameof(AppController.IsPcMode)
            or nameof(AppController.TranslatorStatus) or nameof(AppController.ModelInstalled) or nameof(AppController.SubtitlesPaused))
            Refresh();
        if (e.PropertyName is nameof(AppController.UpdateVersion) or nameof(AppController.Updating)
            or nameof(AppController.UpdateProgress) or nameof(AppController.UpdateError))
            ShowUpdate();
    }

    private void ShowUpdate()
    {
        var text = Localizer.Current;
        UpdateCard.Visibility = controller.UpdateVersion is null ? Visibility.Collapsed : Visibility.Visible;
        UpdateTitle.Text = text["A new version of CouchSubs is available: %@"].Replace("%@", controller.UpdateVersion ?? "");
        UpdateButton.Content = text[controller.CanUpdateItself ? "Update now" : "Download"];
        UpdateButton.IsEnabled = !controller.Updating;
        UpdateBar.Visibility = controller.Updating ? Visibility.Visible : Visibility.Collapsed;
        UpdateBar.Value = controller.UpdateProgress;
        UpdateErrorText.Text = controller.UpdateError ?? "";
        UpdateErrorText.Visibility = controller.UpdateError is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void OnUpdate(object sender, RoutedEventArgs e) => await controller.InstallUpdateAsync();

    private void Refresh()
    {
        if (!controller.IsRunning || !controller.IsPcMode || smoothDisplay != controller.Settings.PcDisplayId)
            StopSmoothVideo();
        SmoothVideoCard.Visibility = controller.IsPcMode ? Visibility.Visible : Visibility.Collapsed;
        var running = controller.IsRunning;
        var starting = controller.IsStarting;
        Toggle.Style = (Style)FindResource(running ? "DangerButton" : "PrimaryButton");
        ToggleGlyph.Text = running ? Glyphs.Stop : Glyphs.Play;
        ToggleText.Text = starting ? "Başlatılıyor…" : Localizer.Current[running ? "Stop translating" : "Start translating"];
        PauseButton.Content = controller.SubtitlesPaused ? "Altyazıyı sürdür" : "Altyazıyı duraklat";
        if (!running)
        {
            screenActionRevision++;
            ScreenActionStatus.Text = "";
        }
        ModelCard.Visibility = controller.ModelInstalled ? Visibility.Collapsed : Visibility.Visible;
        SourceSelection.IsEnabled = !running && !starting;
        PcDisplaySelection.IsEnabled = !running && !starting;
        RefreshDisplays.IsEnabled = !running && !starting;
        PcDisplayPanel.Visibility = controller.IsPcMode ? Visibility.Visible : Visibility.Collapsed;
        ConsoleStatusCard.Visibility = controller.IsPcMode ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnSourceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (selectingSource || SourceSelection.SelectedItem is not ComboBoxItem { Tag: string tag }) return;
        var source = Enum.Parse<GameSource>(tag);
        if (controller.Settings.Source == source) return;
        controller.Settings = controller.Settings with { Source = source };
        ReloadDisplays();
        Refresh();
    }

    private void ReloadDisplays()
    {
        selectingSource = true;
        try
        {
            PcDisplaySelection.Items.Clear();
            var displays = controller.PcDisplays;
            var saved = controller.Settings.PcDisplayId;
            var match = displays.FirstOrDefault(display => display.Id == saved);
            var selected = match ?? displays.FirstOrDefault(display => display.Primary) ?? displays.FirstOrDefault();
            foreach (var display in displays)
                PcDisplaySelection.Items.Add(new ComboBoxItem { Content = display.Name, Tag = display.Id, IsSelected = display == selected });
            DisplaySelectionStatus.Text = selected is null ? "Bağlı ekran bulunamadı."
                : match is null && !string.IsNullOrWhiteSpace(saved) ? "Kayıtlı ekran bulunamadı. Birincil ekran kullanılacak." : "";
        }
        catch (InvalidOperationException) { DisplaySelectionStatus.Text = "Windows ekranları okunamadı. Ekranları yenilemeyi dene."; }
        finally { selectingSource = false; }
    }

    private void OnRefreshDisplays(object sender, RoutedEventArgs e) => ReloadDisplays();

    private void OnPcDisplayChanged(object sender, SelectionChangedEventArgs e)
    {
        if (selectingSource || PcDisplaySelection.SelectedItem is not ComboBoxItem { Tag: string id }) return;
        if (controller.Settings.PcDisplayId != id)
            controller.Settings = controller.Settings with { PcDisplayId = id };
        DisplaySelectionStatus.Text = "";
    }

    private async void OnToggle(object sender, RoutedEventArgs e)
    {
        if (controller.IsRunning) controller.Stop(); else await controller.StartAsync();
    }

    private void OnPauseSubtitles(object sender, RoutedEventArgs e) => controller.ToggleSubtitles();

    private async void OnTranslateScreen(object sender, RoutedEventArgs e)
    {
        var revision = ++screenActionRevision;
        ScreenActionStatus.Text = "Ekrandaki yazılar okunuyor…";
        var outcome = await controller.TranslateScreenAsync();
        if (revision != screenActionRevision || !controller.IsRunning || !IsLoaded) return;
        ScreenActionStatus.Text = outcome switch
        {
            ScreenOutcome.NoText => "Ekranda çevrilecek yazı bulunamadı.",
            ScreenOutcome.NotRunning => "Oyun görüntüsü henüz gelmedi.",
            ScreenOutcome.TranslatorUnavailable => "Çeviri hazır değil. Yerel modelin durumunu kontrol et.",
            ScreenOutcome.Failed => "Ekran çevrilemedi. Yeniden dene.",
            _ => ""
        };
    }

    private void OnSettings(object sender, RoutedEventArgs e) => SettingsWindow.Open(Window.GetWindow(this), controller);

    private void OnDownload(object sender, RoutedEventArgs e) => controller.StartModelDownload();

    /// <summary>
    /// Oyun dilinin Windows yazı tanıma paketini yönetici izniyle kurar (Add-WindowsCapability), sonra okumayı
    /// yeni paketle yeniden başlatır.
    /// </summary>
    private async void OnInstallOcr(object sender, RoutedEventArgs e)
    {
        var language = controller.Settings.SourceLanguage;
        if (language.Length == 0 || !language.All(character => char.IsAsciiLetterOrDigit(character) || character == '-'))
        {
            OcrInstallStatus.Text = "Ayarlar’dan geçerli bir oyun dili seç.";
            return;
        }
        var script = $"$ErrorActionPreference = 'Stop'; Get-WindowsCapability -Online | Where-Object {{ $_.Name -like 'Language.OCR~~~{language}-*' -and $_.State -ne 'Installed' }} | Select-Object -First 1 | Add-WindowsCapability -Online";
        InstallOcr.IsEnabled = false;
        OcrInstallStatus.Text = "Windows yazı tanıma paketini kuruyor…";
        try
        {
            using var process = Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -WindowStyle Hidden -Command \"{script}\"")
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            });
            if (process is not null) await process.WaitForExitAsync();
            if (!IsLoaded) return;
            if (process?.ExitCode != 0)
            {
                OcrInstallStatus.Text = "Paket kurulamadı. Windows dil ayarlarından yazı tanımayı kontrol et.";
                return;
            }
            controller.RefreshTextRecognition();
            OcrInstallStatus.Text = controller.OcrMissing ? "Paket henüz kullanılamıyor. Windows dil ayarlarını kontrol et." : "";
        }
        catch (Win32Exception) { OcrInstallStatus.Text = "Windows kurulumu başlatılamadı veya iptal edildi."; }
        finally { InstallOcr.IsEnabled = true; }
    }

    private void OnLanguageSettings(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo("ms-settings:regionlanguage") { UseShellExecute = true });
}
