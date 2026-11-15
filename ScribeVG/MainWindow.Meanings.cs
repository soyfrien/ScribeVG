using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace ScribeVG;

/// <summary>
/// "Hide meanings until done": the meaning, readings and the sentences' readings/English stay hidden while writing,
/// so they have to be recalled. They show while the guide is on or peeked, once the kanji (or whole sheet) is
/// finished, or when the hidden meaning is tapped. Revealing never affects the rating.
/// </summary>
public partial class MainWindow
{
    private const string MeaningPlaceholder = "Meaning hidden — tap or hold Space";
    private static readonly Brush PlaceholderBrush = Frozen(Color.FromRgb(0x99, 0x99, 0x99));

    private string _meaningText = string.Empty;
    private string _readingsText = string.Empty;
    private string? _meaningTip;
    private bool _meaningRevealed; // tapped, or the kanji/sheet was finished; reset on the next card
    private readonly List<UIElement> _sentenceSecrets = [];
    private bool _meaningsReady;

    private void InitMeanings()
    {
        HideMeaningsCheck.IsChecked = _settings.HideMeanings;
        _meaningsReady = true;
        ApplyMeaningVisibility();
    }

    private void HideMeanings_Changed(object sender, RoutedEventArgs e)
    {
        if (!_meaningsReady) return;
        _settings.HideMeanings = HideMeaningsCheck.IsChecked == true;
        try { _settings.Save(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Failed to save settings: {ex.Message}"); }
        ApplyMeaningVisibility();
    }

    private bool CurrentKanjiFinished =>
        _sheet is not null
            ? _sheet.IsComplete
            : _deck.Count > 0 && _cardIndex < _deck.Count && _deck[_cardIndex].Strokes.Count > 0 &&
              _currentStrokeIndex >= _deck[_cardIndex].Strokes.Count;

    private bool MeaningsShown =>
        !_settings.HideMeanings || _meaningRevealed || _peeking || ShowGuideCheck.IsChecked == true || CurrentKanjiFinished;

    private void Meaning_Click(object sender, RoutedEventArgs e)
    {
        if (MeaningsShown || _meaningText.Length == 0) return;
        _meaningRevealed = true;
        ApplyMeaningVisibility();
        e.Handled = true;
    }

    private T Secret<T>(T element) where T : UIElement
    {
        _sentenceSecrets.Add(element);
        element.Visibility = MeaningsShown ? Visibility.Visible : Visibility.Collapsed;
        return element;
    }

    private void ApplyMeaningVisibility()
    {
        var shown = MeaningsShown;
        var hide = !shown && _meaningText.Length > 0;
        MeaningLabel.Text = hide ? MeaningPlaceholder : _meaningText;
        MeaningLabel.Foreground = hide ? PlaceholderBrush : Brushes.Black;
        MeaningLabel.FontStyle = hide ? FontStyles.Italic : FontStyles.Normal;
        MeaningLabel.FontWeight = hide ? FontWeights.Normal : FontWeights.SemiBold;
        MeaningLabel.Cursor = hide ? Cursors.Hand : null;
        MeaningLabel.ToolTip = hide ? "Tap to show the meaning, readings and translations" : _meaningTip;
        ReadingsLabel.Text = shown ? _readingsText : string.Empty;
        ReadingsLabel.Visibility = shown && _readingsText.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var secret in _sentenceSecrets)
        {
            secret.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        }
        if (_sheetReady && _settings.SheetMode)
        {
            UpdateSheetReading(_deck.Count > 0 && _cardIndex < _deck.Count ? _deck[_cardIndex].Character : null);
        }
    }
}
