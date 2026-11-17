// SPDX-License-Identifier: AGPL-3.0-only

using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using CouchSubs.Core;
using CouchSubs.Core.Dubbing;
using CouchSubs.Shell;

namespace CouchSubs.App.Views;

/// <summary>Ayarlardaki Türkçe dublaj bölümü (Mac: DubbingCard.swift). Ses motoru pakette yoksa görünmez.</summary>
public partial class DubbingCard : UserControl
{
    private AppController? controller;
    private bool loading;
    private int characterPage;
    private const int PageSize = 24;

    public DubbingCard()
    {
        InitializeComponent();
        // Kesme işaretli metinler XAML biçimlendirme uzantısına yazılamıyor.
        SetupHint.Text = Localizer.Current["In the game's audio settings, turn the dialogue (voice) volume down; keep music and effects. If the game can show speaker names in subtitles, turn them on: every character then speaks with its own voice."];
        Credits.Text = Localizer.Current["Voices: Pocket TTS Turkish (wite-tech, CC BY 4.0) with Kyutai's pocket-tts. The voices are synthetic, not real people."];
        Unloaded += (_, _) =>
        {
            if (controller is not null) controller.PropertyChanged -= OnChanged;
        };
    }

    internal void Attach(AppController attached)
    {
        controller = attached;
        Visibility = attached.DubbingAvailable ? Visibility.Visible : Visibility.Collapsed;
        attached.PropertyChanged += OnChanged;
        Refresh();
    }

    private static string Label(DubbingVoice voice) => Localizer.Current[voice switch
    {
        DubbingVoice.Female1 => "Woman 1 (bright)",
        DubbingVoice.Female2 => "Woman 2 (warm)",
        DubbingVoice.Female3 => "Woman 3 (confident)",
        DubbingVoice.Male1 => "Man 1 (calm, deep)",
        DubbingVoice.Male2 => "Man 2 (energetic)",
        _ => "Man 3 (crisp)",
    }];

    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppController.Settings) or nameof(AppController.DubbingInstalled)
            or nameof(AppController.DubbingDownloading) or nameof(AppController.DubbingProgress) or nameof(AppController.DubbingError))
            Refresh();
    }

    private void Refresh()
    {
        if (controller is null) return;
        loading = true;
        var settings = controller.Settings.Dubbing;
        Enabled.IsChecked = settings.Enabled;
        foreach (ComboBoxItem item in EmotionModePicker.Items)
            item.IsSelected = string.Equals(item.Tag as string, settings.EmotionMode.ToString(), StringComparison.Ordinal);
        EnginePanel.Visibility = controller.FastDubbingAvailable ? Visibility.Visible : Visibility.Collapsed;
        var fast = controller.FastDubbingAvailable && settings.Engine == DubbingEngine.Fast;
        (fast ? FastEngine : NaturalEngine).IsChecked = true;
        EngineHint.Text = Localizer.Current[fast
            ? "Windows' own Turkish voice: speaks at once, nothing to download. Sounds more mechanical."
            : "Natural voices, downloaded once. Each line takes a moment to prepare, so the voice can come a little after the subtitle."];
        Credits.Visibility = fast ? Visibility.Collapsed : Visibility.Visible;
        Options.Visibility = settings.Enabled ? Visibility.Visible : Visibility.Collapsed;
        DownloadPanel.Visibility = controller.DubbingInstalled ? Visibility.Collapsed : Visibility.Visible;
        ReadyPanel.Visibility = controller.DubbingInstalled ? Visibility.Visible : Visibility.Collapsed;
        DownloadButton.IsEnabled = !controller.DubbingDownloading;
        Progress.Visibility = ProgressText.Visibility = controller.DubbingDownloading ? Visibility.Visible : Visibility.Collapsed;
        Progress.Value = controller.DubbingProgress;
        ProgressText.Text = Localizer.Current["Downloading voices… %lld%%"]
            .Replace("%lld", ((int)(controller.DubbingProgress * 100)).ToString()).Replace("%%", "%");
        (settings.Output switch
        {
            DubbingOutput.Tv => OnTv,
            DubbingOutput.Agon => OnAgon,
            DubbingOutput.Headset => OnHeadset,
            _ => OnComputer
        }).IsChecked = true;
        AtmospherePanel.Visibility = settings.Output == DubbingOutput.Headset
            ? Visibility.Visible : Visibility.Collapsed;
        GameAtmosphere.Value = Math.Clamp(settings.GameAtmospherePercent, 35, 100);
        TurkishVoiceVolume.Value = Math.Clamp(settings.TurkishVoicePercent, 0, 100);
        GameAtmosphereValue.Text = $"{(int)GameAtmosphere.Value}%";
        TurkishVoiceVolumeValue.Text = $"{(int)TurkishVoiceVolume.Value}%";
        OutputHint.Text = settings.Output switch
        {
            DubbingOutput.Tv => "LG webOS TV: CouchSubs TV uygulamasının bağlı ve açık olması gerekir. Oyun sesiyle Türkçe dublaj TV'den duyulur.",
            DubbingOutput.Agon => "Agon monitör: Türkçe ses Windows'un varsayılan ses çıkışından çalar. PS5'in HDMI kablosu tek başına PC sesini monitöre taşımaz; PC sesini monitöre veya hoparlöre ayrıca bağlayın.",
            DubbingOutput.Headset => "PULSE 3D (deneysel): USB adaptörünü MSI bilgisayara takın ve Windows varsayılan ses çıkışı olarak PULSE 3D seçin. Ardından CouchSubs çevirisini durdurup yeniden başlatın. PS5 Remote Play oyun sesi ve Türkçe dublaj aynı çıkışta karıştırılır. USB adaptör PS5 üzerinde kalırsa çalışmaz; gerçek cihazda henüz test edilmedi.",
            _ => "Türkçe dublaj bilgisayarın Windows varsayılan ses çıkışından çalar."
        };
        Error.Text = controller.DubbingError ?? "";
        Error.Visibility = controller.DubbingError is null ? Visibility.Collapsed : Visibility.Visible;
        BuildGames();
        BuildVoices(settings.Cast);
        loading = false;
    }

    private void BuildGames()
    {
        if (controller is null) return;
        var game = controller.Settings.Dubbing.ActiveGame;
        GameSelection.Items.Clear();
        foreach (var title in controller.KnownGames)
            GameSelection.Items.Add(new ComboBoxItem
            {
                Content = title,
                Tag = title,
                IsSelected = string.Equals(title, game, StringComparison.OrdinalIgnoreCase)
            });
        GameName.Text = game;
        GameInfo.Text = $"Oyun: {game} · {controller.Settings.Dubbing.Cast.Assignments.Count} kayıtlı karakter. Altı temel ses paylaşılır; karakter sayısı sınırı yok.";
    }

    private void OnGameSelection(object sender, SelectionChangedEventArgs e)
    {
        if (loading || GameSelection.SelectedItem is not ComboBoxItem { Tag: string game } || controller is null) return;
        controller.SelectGame(game);
    }

    private void OnSelectGame(object sender, RoutedEventArgs e)
    {
        if (controller is null) return;
        controller.SelectGame(GameName.Text);
    }

    private void OnAddCharacter(object sender, RoutedEventArgs e)
    {
        if (controller is null || string.IsNullOrWhiteSpace(CharacterName.Text)) return;
        var group = NewCharacterGroup.SelectedItem is ComboBoxItem { Tag: string tag }
            ? Enum.Parse<CharacterGroup>(tag) : CharacterGroup.Unknown;
        controller.AddCharacter(CharacterName.Text, group);
        CharacterName.Clear();
    }

    private static string GroupLabel(CharacterGroup group) => group switch
    {
        CharacterGroup.Male => "Erkek",
        CharacterGroup.Female => "Kadın",
        CharacterGroup.Child => "Çocuk",
        CharacterGroup.Creature => "Canlı / Fantastik",
        CharacterGroup.Narrator => "Anlatıcı",
        _ => "Bilinmeyen",
    };

    private Grid CharacterRow(string name, VoiceCast cast)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(145) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(195) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        row.Children.Add(new TextBlock
        {
            Text = DisplayName(name), VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 8, 0)
        });
        var kind = new ComboBox { Margin = new Thickness(0, 0, 6, 0) };
        foreach (var group in Enum.GetValues<CharacterGroup>())
            kind.Items.Add(new ComboBoxItem
            {
                Content = GroupLabel(group), Tag = group,
                IsSelected = group == cast.GroupFor(name)
            });
        kind.SelectionChanged += (_, _) =>
        {
            if (!loading && kind.SelectedItem is ComboBoxItem { Tag: CharacterGroup group })
                controller?.SetCharacterGroup(name, group);
        };
        Grid.SetColumn(kind, 1);
        row.Children.Add(kind);

        var voicePicker = new ComboBox { Margin = new Thickness(0, 0, 6, 0) };
        foreach (var voice in DubbingVoices.All)
            voicePicker.Items.Add(new ComboBoxItem
            {
                Content = Label(voice), Tag = voice,
                IsSelected = voice == cast.Assignments[name]
            });
        voicePicker.SelectionChanged += (_, _) =>
        {
            if (!loading && voicePicker.SelectedItem is ComboBoxItem { Tag: DubbingVoice voice })
                controller?.AssignVoice(voice, name);
        };
        Grid.SetColumn(voicePicker, 2);
        row.Children.Add(voicePicker);
        var remove = new Button { Content = "Sil", ToolTip = "Bu oyundan karakter kaydını sil" };
        remove.Click += (_, _) => controller?.RemoveCharacter(name);
        Grid.SetColumn(remove, 3);
        row.Children.Add(remove);
        return row;
    }

    private void OnCharacterSearch(object sender, TextChangedEventArgs e)
    {
        if (controller is null || loading) return;
        characterPage = 0;
        BuildVoices(controller.Settings.Dubbing.Cast);
    }

    private void OnPreviousCharacters(object sender, RoutedEventArgs e)
    {
        if (controller is null) return;
        characterPage = Math.Max(0, characterPage - 1);
        BuildVoices(controller.Settings.Dubbing.Cast);
    }

    private void OnNextCharacters(object sender, RoutedEventArgs e)
    {
        if (controller is null) return;
        var query = CharacterSearch.Text.Trim();
        var total = controller.Settings.Dubbing.Cast.Assignments.Keys.Count(name =>
            name.Contains(query, StringComparison.OrdinalIgnoreCase));
        characterPage = Math.Min(Math.Max(0, (total - 1) / PageSize), characterPage + 1);
        BuildVoices(controller.Settings.Dubbing.Cast);
    }

    private void BuildVoices(VoiceCast cast)
    {
        Voices.Children.Clear();
        Voices.Children.Add(VoiceRow(Localizer.Current["Narrator and unnamed lines"], cast.Narrator,
            voice => Update(s => s with { Dubbing = s.Dubbing with { Cast = WithRoles(s.Dubbing.Cast, narrator: voice) } })));
        Voices.Children.Add(VoiceRow(Localizer.Current["Second speaker in dialogue"], cast.Partner,
            voice => Update(s => s with { Dubbing = s.Dubbing with { Cast = WithRoles(s.Dubbing.Cast, partner: voice) } })));
        var filter = CharacterSearch.Text.Trim();
        var names = cast.Assignments.Keys.Where(name =>
            name.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(name => name, StringComparer.Ordinal).ToArray();
        characterPage = Math.Min(characterPage, Math.Max(0, (names.Length - 1) / PageSize));
        CharacterPageInfo.Text = names.Length == 0 ? "0 karakter" :
            $"{names.Length} karakter · {characterPage + 1}/{(names.Length - 1) / PageSize + 1}";
        if (names.Length == 0) return;
        Voices.Children.Add(new TextBlock
        {
            Text = Localizer.Current["Characters"], FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 12, 0, 4)
        });
        foreach (var name in names.Skip(characterPage * PageSize).Take(PageSize))
            Voices.Children.Add(CharacterRow(name, cast));
    }

    private static VoiceCast WithRoles(VoiceCast cast, DubbingVoice? narrator = null, DubbingVoice? partner = null)
    {
        var copy = cast.Copy();
        if (narrator is { } n) copy.Narrator = n;
        if (partner is { } p) copy.Partner = p;
        return copy;
    }

    /// <summary>"OLD MAN" → "Old Man"</summary>
    private static string DisplayName(string name) =>
        string.Join(' ', name.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => char.ToUpperInvariant(word[0]) + word[1..]));

    private void OnEmotionMode(object sender, SelectionChangedEventArgs e)
    {
        if (loading || controller is null || EmotionModePicker.SelectedItem is not ComboBoxItem { Tag: string tag }) return;
        if (Enum.TryParse<DubbingEmotionMode>(tag, out var mode))
            Update(s => s with { Dubbing = s.Dubbing with { EmotionMode = mode } });
    }

    private void OnAtmosphereChange(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (GameAtmosphere is null || TurkishVoiceVolume is null) return;
        if (GameAtmosphereValue is not null)
            GameAtmosphereValue.Text = $"{(int)GameAtmosphere.Value}%";
        if (TurkishVoiceVolumeValue is not null)
            TurkishVoiceVolumeValue.Text = $"{(int)TurkishVoiceVolume.Value}%";
        if (loading || controller is null) return;
        Update(s => s with { Dubbing = s.Dubbing with
        {
            GameAtmospherePercent = (int)GameAtmosphere.Value,
            TurkishVoicePercent = (int)TurkishVoiceVolume.Value
        } });
    }

    private void OnEngine(object sender, RoutedEventArgs e)
    {
        if (loading || sender is not RadioButton { Tag: string tag }) return;
        var engine = Enum.Parse<DubbingEngine>(tag);
        Update(s => s with { Dubbing = s.Dubbing with { Engine = engine } });
    }

    private Grid VoiceRow(string title, DubbingVoice selected, Action<DubbingVoice> choose)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var label = new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 12, 0) };
        var picker = new ComboBox { Margin = new Thickness(0, 0, 8, 0) };
        foreach (var voice in DubbingVoices.All)
            picker.Items.Add(new ComboBoxItem { Content = Label(voice), Tag = voice, IsSelected = voice == selected });
        picker.SelectionChanged += (_, _) =>
        {
            if (!loading && picker.SelectedItem is ComboBoxItem { Tag: DubbingVoice voice }) choose(voice);
        };
        var listen = new Button { Content = Localizer.Current["Listen"] };
        listen.Click += async (_, _) =>
        {
            if (controller is not null && picker.SelectedItem is ComboBoxItem { Tag: DubbingVoice voice }) await controller.PreviewVoiceAsync(voice);
        };
        Grid.SetColumn(picker, 1);
        Grid.SetColumn(listen, 2);
        row.Children.Add(label);
        row.Children.Add(picker);
        row.Children.Add(listen);
        return row;
    }

    private void Update(Func<UserSettings, UserSettings> change)
    {
        if (!loading && controller is not null) controller.Settings = change(controller.Settings);
    }

    private void OnEnabled(object sender, RoutedEventArgs e) =>
        Update(s => s with { Dubbing = s.Dubbing with { Enabled = Enabled.IsChecked == true } });

    private void OnOutput(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string tag }) Update(s => s with { Dubbing = s.Dubbing with { Output = Enum.Parse<DubbingOutput>(tag) } });
    }

    private async void OnDownload(object sender, RoutedEventArgs e)
    {
        if (controller is not null) await controller.DownloadDubbingAsync();
    }
}
