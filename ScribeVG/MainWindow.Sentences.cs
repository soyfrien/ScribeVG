using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Microsoft.Win32;
using ScribeVG.Services;

namespace ScribeVG;

/// <summary>Example sentences for the current kanji, with Emi Asano's recordings.</summary>
public partial class MainWindow
{
    private const int SentencesShown = 3;
    private static readonly Brush HighlightBrush = new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28));

    private readonly SentenceLibrary? _sentences = LoadSentences();
    private readonly AppSettings _settings = AppSettings.Load();
    private AudioLocator? _audio;
    private readonly MediaPlayer _player = new();
    private DateTime? _audioPlayingSince; // set while one of Emi's clips is playing
    private string? _sentenceKanji;
    private int _sentenceOffset;

    private static SentenceLibrary? LoadSentences()
    {
        foreach (var dir in new[]
                 {
                     System.IO.Path.Combine(AppContext.BaseDirectory, "Data"),
                     System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Data")
                 })
        {
            var library = SentenceLibrary.TryLoad(System.IO.Path.Combine(dir, "sentences.json"));
            if (library is not null) return library;
        }
        return null;
    }

    private void InitSentences()
    {
        _audio = new AudioLocator(_settings.AudioFolder);
        _player.MediaFailed += (_, e) =>
        {
            _audioPlayingSince = null;
            AudioStatusLabel.Text = $"Couldn't play that clip: {e.ErrorException?.Message}";
        };
        _player.MediaEnded += (_, _) => _audioPlayingSince = null;
        Closed += (_, _) => _player.Close();
    }

    /// <summary>Called whenever the shown kanji is (re)drawn; only rebuilds when it changes.</summary>
    private void ShowSentences(string? kanji, bool force = false)
    {
        if (!force && kanji == _sentenceKanji) return;
        if (kanji != _sentenceKanji) _sentenceOffset = 0;
        _sentenceKanji = kanji;
        SentencePanel.Children.Clear();

        var all = _sentences?.For(kanji) ?? [];
        MoreSentencesButton.Visibility = all.Count > SentencesShown ? Visibility.Visible : Visibility.Collapsed;

        if (_sentences is null)
        {
            SentencePanel.Children.Add(Note("(Data/sentences.json not found)"));
        }
        else if (kanji is null)
        {
            // Nothing to show (e.g. "All done for today").
        }
        else if (all.Count == 0)
        {
            SentencePanel.Children.Add(Note($"No example sentences use {kanji} yet."));
        }
        else
        {
            var start = _sentenceOffset % all.Count;
            var take = Math.Min(SentencesShown, all.Count);
            for (var i = 0; i < take; i++)
            {
                SentencePanel.Children.Add(SentenceRow(all[(start + i) % all.Count], kanji));
            }
        }

        UpdateAudioStatus(all);
    }

    private static TextBlock Note(string text) => new()
    {
        Text = text, FontSize = 13, Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap
    };

    private UIElement SentenceRow(ExampleSentence sentence, string kanji)
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var clip = _audio?.Find(sentence.Id);
        var play = new Button
        {
            Content = "▶",
            Width = 40,
            Height = 40,
            FontSize = 16,
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Top,
            IsEnabled = clip is not null,
            ToolTip = clip is not null ? "Play Emi Asano's reading" :
                sentence.HasAudio ? "Recording not found: set the audio folder" : "No recording for this sentence",
            Tag = clip,
        };
        play.Click += Play_Click;
        Grid.SetColumn(play, 0);
        grid.Children.Add(play);

        var text = new StackPanel();
        var japanese = new TextBlock { FontSize = 18, TextWrapping = TextWrapping.Wrap };
        foreach (var piece in SplitKeeping(sentence.Japanese, kanji))
        {
            japanese.Inlines.Add(piece == kanji
                ? new Run(piece) { Foreground = HighlightBrush, FontWeight = FontWeights.Bold }
                : new Run(piece));
        }
        text.Children.Add(japanese);
        if (!string.IsNullOrEmpty(sentence.Reading))
        {
            text.Children.Add(new TextBlock
            {
                Text = sentence.Reading, FontSize = 13, Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)),
                TextWrapping = TextWrapping.Wrap
            });
        }
        if (!string.IsNullOrEmpty(sentence.English))
        {
            text.Children.Add(new TextBlock
            {
                Text = sentence.English, FontSize = 14, FontStyle = FontStyles.Italic,
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 0)
            });
        }
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        return grid;
    }

    /// <summary>"水を飲む水" split on "水" → 水, を飲む, 水 (the separators are kept).</summary>
    internal static IEnumerable<string> SplitKeeping(string text, string token)
    {
        var start = 0;
        while (true)
        {
            var i = text.IndexOf(token, start, StringComparison.Ordinal);
            if (i < 0) break;
            if (i > start) yield return text[start..i];
            yield return token;
            start = i + token.Length;
        }
        if (start < text.Length) yield return text[start..];
    }

    private void UpdateAudioStatus(IReadOnlyList<ExampleSentence> shown)
    {
        if (_audio?.Folder is null)
        {
            AudioStatusLabel.Text = _settings.AudioFolder is not null
                ? $"No .m4a clips in {_settings.AudioFolder}. Choose the Umai Kanji Audio folder."
                : "Audio folder not found. Sync OneDrive's Backups\\Umai Kanji Archive or choose the folder.";
            return;
        }

        var missing = shown.Take(SentencesShown).Count(s => s.HasAudio && _audio.Find(s.Id) is null);
        var source = _audio.FromSettings ? "" : " (found in OneDrive)";
        AudioStatusLabel.Text = missing > 0
            ? $"Audio: {_audio.Folder}{source}. {missing} clip(s) not found there."
            : $"Audio: {_audio.Folder}{source}";
    }

    private void Play_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string path } || !File.Exists(path)) return;
        try
        {
            _player.Stop();
            _player.Open(new Uri(path, UriKind.Absolute));
            _player.Play();
            _audioPlayingSince = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            AudioStatusLabel.Text = $"Couldn't play {System.IO.Path.GetFileName(path)}: {ex.Message}";
        }
    }

    private void MoreSentences_Click(object sender, RoutedEventArgs e)
    {
        _sentenceOffset += SentencesShown;
        ShowSentences(_sentenceKanji, force: true);
    }

    private void SetAudioFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose the folder with Emi Asano's recordings (…\\Umai Kanji Resources\\Audio)",
            Multiselect = false,
        };
        if (_audio?.Folder is string current) dialog.InitialDirectory = current;
        if (dialog.ShowDialog(this) != true) return;

        var resolved = AudioLocator.Resolve(dialog.FolderName);
        if (resolved is null)
        {
            AudioStatusLabel.Text = $"No .m4a clips in {dialog.FolderName}. Pick the Audio folder itself.";
            return;
        }

        _settings.AudioFolder = resolved;
        try { _settings.Save(); }
        catch (Exception ex) { StatusLabel.Text = $"Couldn't save settings: {ex.Message}"; }
        _audio = new AudioLocator(resolved);
        ShowSentences(_sentenceKanji, force: true);
    }
}
