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
    private readonly TranslationClip inspection = new();
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
            inspectionTimer.Stop();
            inspection.Arm(false);
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

    private void OnInspectionToggle(object sender, RoutedEventArgs e)
    {
        if (SaveInspectionButton is null) return;
        var enabled = InspectionEnabled.IsChecked == true;
        inspection.Arm(enabled);
        SaveInspectionButton.IsEnabled = enabled;
        if (enabled) inspectionTimer.Start();
        else inspectionTimer.Stop();
    }

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
        if (e.PropertyName is nameof(AppController.IsRunning) or nameof(AppController.IsPcMode) or nameof(AppController.TranslatorStatus) or nameof(AppController.ModelInstalled))
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
        var running = controller.IsRunning;
        Toggle.Style = (Style)FindResource(running ? "DangerButton" : "PrimaryButton");
        ToggleGlyph.Text = running ? Glyphs.Stop : Glyphs.Play;
        ToggleText.Text = Localizer.Current[running ? "Stop translating" : "Start translating"];
        ModelCard.Visibility = controller.ModelInstalled ? Visibility.Collapsed : Visibility.Visible;
        SourceSelection.IsEnabled = !running;
        PcDisplaySelection.IsEnabled = !running;
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
        PcDisplaySelection.Items.Clear();
        var displays = controller.PcDisplays;
        foreach (var display in displays)
            PcDisplaySelection.Items.Add(new ComboBoxItem
            {
                Content = display.Name,
                Tag = display.Id,
                IsSelected = display.Id == controller.Settings.PcDisplayId ||
                    (string.IsNullOrWhiteSpace(controller.Settings.PcDisplayId) && display.Primary)
            });
        selectingSource = false;
    }

    private void OnRefreshDisplays(object sender, RoutedEventArgs e) => ReloadDisplays();

    private void OnPcDisplayChanged(object sender, SelectionChangedEventArgs e)
    {
        if (selectingSource || PcDisplaySelection.SelectedItem is not ComboBoxItem { Tag: string id }) return;
        if (controller.Settings.PcDisplayId != id)
            controller.Settings = controller.Settings with { PcDisplayId = id };
    }

    private async void OnToggle(object sender, RoutedEventArgs e)
    {
        if (controller.IsRunning) controller.Stop(); else await controller.StartAsync();
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
        var script = $"Get-WindowsCapability -Online | Where-Object {{ $_.Name -like 'Language.OCR~~~{language}-*' -and $_.State -ne 'Installed' }} | Select-Object -First 1 | Add-WindowsCapability -Online";
        InstallOcr.IsEnabled = false;
        try
        {
            using var process = Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -WindowStyle Hidden -Command \"{script}\"")
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            });
            if (process is not null) await process.WaitForExitAsync();
            if (!controller.IsRunning) return;
            controller.Stop();
            await controller.StartAsync();
        }
        catch (System.ComponentModel.Win32Exception) { /* izin verilmedi */ }
        finally { InstallOcr.IsEnabled = true; }
    }

    private void OnLanguageSettings(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo("ms-settings:regionlanguage") { UseShellExecute = true });
}
