// SPDX-License-Identifier: AGPL-3.0-only

using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using CouchSubs.Shell;

namespace CouchSubs.App.Views;

public partial class TranslationMemoryWindow : Window
{
    private readonly AppController controller;
    private readonly TranslationMemoryScope scope;
    private bool writing;

    public TranslationMemoryWindow(AppController controller)
    {
        this.controller = controller;
        scope = controller.CurrentTranslationMemoryScope;
        InitializeComponent();
        ScopeText.Text = $"{scope.Game} · {scope.SourceLanguage.ToUpperInvariant()} → {scope.TargetLanguage.ToUpperInvariant()}";
        RefreshEntries();
    }

    private void RefreshEntries(string? selectSource = null)
    {
        var entries = ListType.SelectedIndex == 1 ? controller.SavedTranslations(scope) : controller.RecentTranslations(scope);
        Entries.ItemsSource = entries;
        Entries.SelectedItem = entries.FirstOrDefault(line => line.Source == selectSource) ?? entries.FirstOrDefault();
        Status.Text = entries.Count == 0
            ? ListType.SelectedIndex == 1 ? "Bu oyun için henüz kayıt yok." : "Oyun sırasında görünen çeviriler burada listelenir."
            : "Bir satır seçip çevirisini onaylayabilir veya düzeltebilirsin.";
        UpdateButtons();
    }

    private void OnListTypeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Entries is not null) RefreshEntries();
    }

    private void OnRefresh(object sender, RoutedEventArgs e) => RefreshEntries((Entries.SelectedItem as TranslationMemoryLine)?.Source);

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var line = Entries.SelectedItem as TranslationMemoryLine;
        SourceText.Text = line?.Source ?? "";
        EditedText.Text = line is null ? "" : controller.SavedTranslation(line) ?? line.Translation;
        UpdateButtons();
    }

    private void OnEdited(object sender, TextChangedEventArgs e) => UpdateButtons();

    private void UpdateButtons()
    {
        if (SaveButton is null || RemoveButton is null) return;
        var selected = Entries.SelectedItem is TranslationMemoryLine;
        SaveButton.IsEnabled = !writing && selected && !string.IsNullOrWhiteSpace(EditedText.Text);
        RemoveButton.IsEnabled = !writing && selected && ListType.SelectedIndex == 1;
        EditedText.IsEnabled = !writing && selected;
        Entries.IsEnabled = !writing;
        ListType.IsEnabled = !writing;
    }

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        if (Entries.SelectedItem is not TranslationMemoryLine line) return;
        var updated = line with { Translation = EditedText.Text };
        await WriteAsync(() => controller.SaveTranslationAsync(updated), line.Source, "Hafızaya kaydedildi.");
    }

    private async void OnRemove(object sender, RoutedEventArgs e)
    {
        if (Entries.SelectedItem is not TranslationMemoryLine line) return;
        await WriteAsync(() => controller.RemoveTranslationAsync(line), null, "Hafızadan silindi.");
    }

    private async Task WriteAsync(Func<Task> write, string? selectSource, string success)
    {
        if (writing) return;
        writing = true;
        UpdateButtons();
        try
        {
            await write();
            RefreshEntries(selectSource);
            Status.Text = success;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            Status.Text = "Çeviri hafızası dosyası güncellenemedi.";
        }
        finally
        {
            writing = false;
            UpdateButtons();
        }
    }
}
