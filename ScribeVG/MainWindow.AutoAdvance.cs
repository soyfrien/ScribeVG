using System.Windows;
using System.Windows.Threading;

namespace ScribeVG;

/// <summary>Auto-advance: after a kanji is finished with a high enough average, show the grade briefly, then go to the next card.</summary>
public partial class MainWindow
{
    private static readonly int[] AutoAdvanceChoices = { 60, 70, 75, 80, 85, 90, 95 };
    private static readonly TimeSpan AutoAdvanceDelay = TimeSpan.FromMilliseconds(1200);
    private DispatcherTimer? _autoAdvanceTimer;
    private bool _autoAdvanceReady;

    private void InitAutoAdvance()
    {
        foreach (var choice in AutoAdvanceChoices) AutoAdvanceCombo.Items.Add(choice);
        var score = AutoAdvanceChoices.Contains(_settings.AutoAdvanceScore) ? _settings.AutoAdvanceScore : 75;
        AutoAdvanceCombo.SelectedItem = score;
        AutoAdvanceCheck.IsChecked = _settings.AutoAdvance;
        _autoAdvanceReady = true;
    }

    private void AutoAdvance_Changed(object sender, RoutedEventArgs e)
    {
        if (!_autoAdvanceReady) return;
        _settings.AutoAdvance = AutoAdvanceCheck.IsChecked == true;
        if (AutoAdvanceCombo.SelectedItem is int score) _settings.AutoAdvanceScore = score;
        if (!_settings.AutoAdvance) CancelAutoAdvance();
        try { _settings.Save(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Failed to save settings: {ex.Message}"); }
    }

    private void MaybeAutoAdvance()
    {
        CancelAutoAdvance();
        if (!_settings.AutoAdvance || _strokeScores.Count == 0) return;

        var average = (int)Math.Round(_strokeScores.Average());
        if (average < _settings.AutoAdvanceScore) return;

        var cardIndex = _cardIndex;
        StatusLabel.Text = $"Kanji average {average}. Moving on…";
        var timer = new DispatcherTimer { Interval = AutoAdvanceDelay };
        _autoAdvanceTimer = timer;
        timer.Tick += (_, _) =>
        {
            CancelAutoAdvance();
            // Only advance if nothing changed meanwhile (no Undo, Clear, or manual Next/Previous).
            if (_deck.Count == 0 || _cardIndex != cardIndex || _cardIndex >= _deck.Count) return;
            if (_currentStrokeIndex < _deck[_cardIndex].Strokes.Count) return;

            // If Emi is reading a sentence, let her finish (clips are short; give up waiting after 10 s).
            if (_audioPlayingSince is DateTime since && DateTime.UtcNow - since < TimeSpan.FromSeconds(10))
            {
                _autoAdvanceTimer = timer;
                timer.Interval = TimeSpan.FromMilliseconds(250);
                timer.Start();
                return;
            }
            Next_Click(this, new RoutedEventArgs());
        };
        timer.Start();
    }

    private void CancelAutoAdvance()
    {
        _autoAdvanceTimer?.Stop();
        _autoAdvanceTimer = null;
    }
}
