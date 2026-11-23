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
    private AudioCache? _cache;
    private AudioPack? _pack;
    private CancellationTokenSource? _playCts;
    private CancellationTokenSource? _prefetchCts;
    private readonly MediaPlayer _player = new();
    private DateTime? _audioPlayingSince; // set while one of Emi's clips is playing
    private DateTime? _audioFetchingSince; // set while a clip is being unpacked
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
        _cache = new AudioCache();
        _pack = AudioPack.TryOpen();
        _player.MediaFailed += (_, e) =>
        {
            _audioPlayingSince = null;
            _audioFetchingSince = null;
            AudioStatusLabel.Text = $"Couldn't play that clip: {e.ErrorException?.Message}";
        };
        _player.MediaEnded += (_, _) => _audioPlayingSince = null;
        Closed += (_, _) =>
        {
            CancelAudioFetches();
            _player.Close();
        };
    }

    /// <summary>Called whenever the shown kanji is (re)drawn; only rebuilds when it changes.</summary>
    private void ShowSentences(string? kanji, bool force = false)
    {
        if (!force && kanji == _sentenceKanji) return;
        if (kanji != _sentenceKanji) _sentenceOffset = 0;
        _sentenceKanji = kanji;
        SentencePanel.Children.Clear();
        _sentenceSecrets.Clear();

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
        PrefetchVisible(all);
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

        var play = new Button
        {
            Content = "▶",
            Width = 40,
            Height = 40,
            FontSize = 16,
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Top,
            IsEnabled = sentence.HasAudio,
            ToolTip = sentence.HasAudio ? "Play Emi Asano's reading" : "No recording for this sentence",
            Tag = sentence.Id,
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
            text.Children.Add(Secret(new TextBlock
            {
                Text = sentence.Reading, FontSize = 13, Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)),
                TextWrapping = TextWrapping.Wrap
            }));
        }
        if (!string.IsNullOrEmpty(sentence.English))
        {
            text.Children.Add(Secret(new TextBlock
            {
                Text = sentence.English, FontSize = 14, FontStyle = FontStyles.Italic,
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 0)
            }));
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
        if (_audio?.Folder is not null)
        {
            var missing = shown.Take(SentencesShown).Count(s => s.HasAudio && _audio.Find(s.Id) is null);
            var source = _audio.FromSettings ? "" : " (found in OneDrive)";
            AudioStatusLabel.Text = missing > 0 && _pack is not null
                ? $"Audio: {_audio.Folder}{source}. Missing clips come from the built-in recordings."
                : $"Audio: {_audio.Folder}{source}";
            return;
        }

        if (_pack is null)
        {
            AudioStatusLabel.Text = "The built-in recordings are missing from this install. Use Set audio folder to pick them.";
            return;
        }

        AudioStatusLabel.Text = _settings.AudioFolder is not null
            ? $"No .m4a clips in {_settings.AudioFolder}. Using the built-in recordings."
            : "Recordings by Emi Asano are built in and work offline.";
    }

    private async void Play_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: int id } button) return;
        var previous = button.Content;
        button.Content = "…";
        button.IsEnabled = false;
        try
        {
            await PlaySentenceAudioAsync(id, announceErrors: true);
        }
        finally
        {
            button.Content = previous;
            button.IsEnabled = true;
        }
    }

    /// <summary>Stop any clip that's playing, then play this sentence's recording from the audio folder or the built-in pack.</summary>
    private void PlaySentenceAudio(ExampleSentence sentence)
    {
        _ = PlaySentenceAudioAsync(sentence.Id, announceErrors: false);
    }

    private async Task PlaySentenceAudioAsync(int sentenceId, bool announceErrors)
    {
        StopSentenceAudio();
        var cts = new CancellationTokenSource();
        _playCts = cts;
        _audioFetchingSince = DateTime.UtcNow;
        try
        {
            var path = _audio?.Find(sentenceId);
            if (path is null && _pack is not null && _cache is not null)
            {
                path = await _pack.GetAsync(sentenceId, _cache, cts.Token);
            }
            if (cts.IsCancellationRequested) return;
            _audioFetchingSince = null;
            if (path is null || !File.Exists(path))
            {
                if (announceErrors)
                {
                    AudioStatusLabel.Text = "Couldn't find a recording for that sentence.";
                }
                return;
            }
            PlayClip(path, announceErrors);
        }
        catch (OperationCanceledException)
        {
            _audioFetchingSince = null;
        }
        catch (Exception ex)
        {
            _audioFetchingSince = null;
            if (announceErrors)
            {
                AudioStatusLabel.Text = $"Couldn't play that clip: {ex.Message}";
            }
        }
    }

    private void PlayClip(string? path, bool announceErrors)
    {
        try { _player.Stop(); }
        catch (Exception) { /* nothing was open */ }
        if (path is null || !File.Exists(path)) return;
        try
        {
            _player.Open(new Uri(path, UriKind.Absolute));
            _player.Play();
            _audioPlayingSince = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            _audioPlayingSince = null;
            if (announceErrors)
            {
                AudioStatusLabel.Text = $"Couldn't play {System.IO.Path.GetFileName(path)}: {ex.Message}";
            }
        }
    }

    private void StopSentenceAudio()
    {
        CancelPlayFetch();
        _audioFetchingSince = null;
        try { _player.Stop(); }
        catch (Exception) { /* nothing was open */ }
        _audioPlayingSince = null;
    }

    private void CancelPlayFetch()
    {
        try { _playCts?.Cancel(); }
        catch (Exception) { /* already cancelled */ }
        _playCts?.Dispose();
        _playCts = null;
    }

    private void CancelAudioFetches()
    {
        CancelPlayFetch();
        try { _prefetchCts?.Cancel(); }
        catch (Exception) { /* already cancelled */ }
        _prefetchCts?.Dispose();
        _prefetchCts = null;
        _audioFetchingSince = null;
    }

    private void PrefetchVisible(IReadOnlyList<ExampleSentence> shown)
    {
        try { _prefetchCts?.Cancel(); }
        catch (Exception) { /* already cancelled */ }
        _prefetchCts?.Dispose();
        var cts = new CancellationTokenSource();
        _prefetchCts = cts;
        if (_pack is null || _cache is null) return;

        var ids = shown.Take(SentencesShown)
            .Where(s => s.HasAudio && _audio?.Find(s.Id) is null && _pack.Contains(s.Id))
            .Select(s => s.Id)
            .ToList();
        if (ids.Count == 0) return;
        _ = PrefetchSafeAsync(ids, cts.Token);
    }

    private async Task PrefetchSafeAsync(IReadOnlyList<int> ids, CancellationToken cancellationToken)
    {
        try
        {
            foreach (var id in ids)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await _pack!.GetAsync(id, _cache!, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // The user moved on; leftover unpacking can stop.
        }
        catch (Exception)
        {
            // Prefetch is best-effort.
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

    private void ClearAudioCache_Click(object sender, RoutedEventArgs e)
    {
        StopSentenceAudio();
        try { _player.Close(); }
        catch (Exception) { /* nothing was open */ }
        try
        {
            _cache?.Clear();
        }
        catch (Exception ex)
        {
            AudioStatusLabel.Text = $"Couldn't clear the audio cache: {ex.Message}";
            return;
        }
        ShowSentences(_sentenceKanji, force: true);
    }
}
