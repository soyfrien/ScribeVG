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
        ReduceMotionCheck.IsChecked = _settings.ReduceMotion;
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

    private void ReduceMotion_Changed(object sender, RoutedEventArgs e)
    {
        if (!_autoAdvanceReady) return;
        _settings.ReduceMotion = ReduceMotionCheck.IsChecked == true;
        try { _settings.Save(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Failed to save settings: {ex.Message}"); }
    }

    private void MaybeAutoAdvance()
    {
        CancelAutoAdvance();
        if (!_settings.AutoAdvance || _strokeScores.Count == 0) return;

        var average = (int)Math.Round(_strokeScores.Average());
        var redo = _guideUsedThisPass; // traced with the stencil: write it again from memory instead of moving on
        if (!redo && average < _settings.AutoAdvanceScore) return;

        ScheduleAutoAdvance(redo
            ? $"Kanji average {average} with the guide. Now try it without…"
            : $"Kanji average {average}. Moving on…", redo);
    }

    /// <summary>Show the status, wait a moment (and for Emi to finish), then go to the next card or redo without the guide.</summary>
    private void ScheduleAutoAdvance(string status, bool redo)
    {
        CancelAutoAdvance();
        var cardIndex = _cardIndex;
        StatusLabel.Text = status;
        var timer = new DispatcherTimer { Interval = AutoAdvanceDelay };
        _autoAdvanceTimer = timer;
        timer.Tick += (_, _) =>
        {
            CancelAutoAdvance();
            // Only advance if nothing changed meanwhile (no Undo, Clear, or manual Next/Previous).
            if (_deck.Count == 0 || _cardIndex != cardIndex || _cardIndex >= _deck.Count) return;
            if (_currentStrokeIndex < _deck[_cardIndex].Strokes.Count) return;
            if (_sheet is { IsComplete: false }) return; // sheet restarted meanwhile

            // If Emi is reading a sentence, let her finish (clips are short; give up waiting after 10 s).
            if (_audioPlayingSince is DateTime since && DateTime.UtcNow - since < TimeSpan.FromSeconds(10))
            {
                _autoAdvanceTimer = timer;
                timer.Interval = TimeSpan.FromMilliseconds(250);
                timer.Start();
                return;
            }
            if (redo)
            {
                ShowGuideCheck.IsChecked = false; // redraws via Changed
                Clear_Click(this, new RoutedEventArgs());
                StatusLabel.Text = "Now write it without the guide.";
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
