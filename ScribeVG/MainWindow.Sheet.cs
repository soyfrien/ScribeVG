using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using ScribeVG.Models;
using ScribeVG.Services;

namespace ScribeVG;

/// <summary>
/// Sheet mode: each kanji is written 6 times like a row in a practice notebook. Every rep is written in the
/// big box; when it's done the ink moves into the next small box of the strip under it.
/// </summary>
public partial class MainWindow
{
    private const double SheetInnerSize = 1066; // the InkCanvas area inside the 12-px frame
    private static readonly TimeSpan RepPause = TimeSpan.FromMilliseconds(550);
    private static readonly TimeSpan RedoPause = TimeSpan.FromMilliseconds(1200);
    private static readonly Brush SheetModelBrush = Frozen(Color.FromArgb(0x99, 0x44, 0x44, 0x44));
    private static readonly Brush FadedModelBrush = Frozen(Color.FromRgb(0x44, 0x44, 0x44));

    private SheetSession? _sheet;
    private readonly StrokeCollection?[] _repInk = new StrokeCollection?[SheetSession.RepCount];
    private DispatcherTimer? _repTimer;
    private Action? _pendingRep;
    private bool _sheetReady;

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private void InitSheet()
    {
        SheetModeCheck.IsChecked = _settings.SheetMode;
        _sheetReady = true;
        UpdateSheetStrip();
    }

    private void SheetMode_Changed(object sender, RoutedEventArgs e)
    {
        if (!_sheetReady) return;
        _settings.SheetMode = SheetModeCheck.IsChecked == true;
        try { _settings.Save(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Failed to save settings: {ex.Message}"); }

        // Start the current kanji over in the new mode (a rating already given stays given).
        CancelAutoAdvance();
        StartSheet();
        WritingCanvas.Strokes.Clear();
        _currentStrokeIndex = 0;
        _failsByStroke.Clear();
        _guideUsedThisPass = false;
        ResetScores();
        if (_deck.Count > 0) ShowCurrentCard(keepStatus: true);
        UpdateSheetStrip();
        StatusLabel.Text = _settings.SheetMode
            ? "Sheet mode: trace the model, then a fading model, then write it 3 times from memory."
            : "Sheet mode off: write each kanji once.";
    }

    /// <summary>A fresh sheet for the current kanji (or none when sheet mode is off).</summary>
    private void StartSheet()
    {
        CancelRepTransition();
        _sheet = _settings.SheetMode ? new SheetSession() : null;
        Array.Clear(_repInk);
    }

    private void FinishSheetRep(KanjiCard card)
    {
        var sheet = _sheet!;
        var tries = Enumerable.Range(0, card.Strokes.Count)
            .Select(i => 1 + _failsByStroke.GetValueOrDefault(i))
            .ToList();
        var step = sheet.FinishRep(_strokeScores.ToList(), tries);
        var average = step.Result.Average;
        if (step.Step != SheetStep.Redo) _repInk[step.Rep - 1] = ThumbnailInk(WritingCanvas.Strokes);

        var ratingText = step.RatingAttempt is not null ? FinishStudyCard(card, step.RatingAttempt) : null;
        var rated = ratingText is null ? string.Empty : " " + ratingText;

        switch (step.Step)
        {
            case SheetStep.NextRep:
                RepPoof(average);
                StatusLabel.Text = $"Rep {step.Rep} done ({average}). {RepIntro(sheet.CurrentRep)}";
                ScheduleRepTransition(RepPause, SheetSession.IsMemoryRep(sheet.CurrentRep));
                break;

            case SheetStep.Redo:
                RepPoof(Math.Min(average, StrokeMatcher.PassScore));
                StatusLabel.Text = $"Rep {step.Rep}: {average} with the guide. Write it again without the guide.{rated}";
                ScheduleRepTransition(RedoPause, memory: true);
                break;

            case SheetStep.Complete:
                _meaningRevealed = true;
                var memory = sheet.MemoryAverage ?? average;
                CelebrateKanji(memory);
                StatusLabel.Text = $"Sheet done! From memory you averaged {memory}.{rated} Press Next, or Clear for another sheet.";
                if (sheet.ShouldAutoAdvance(_settings.AutoAdvance, _settings.AutoAdvanceScore))
                {
                    ScheduleAutoAdvance($"Sheet done: memory average {memory}. Moving on…", redo: false);
                }
                break;
        }
        UpdateSheetStrip();
    }

    private static string RepIntro(int rep) => rep switch
    {
        1 => "Rep 1: trace the model.",
        2 or 3 => $"Rep {rep}: trace the fainter model.",
        _ => $"Rep {rep} of {SheetSession.RepCount}: from memory."
    };

    /// <summary>After a short look at the finished rep: clear the big box for the next rep (or the redo).</summary>
    private void ScheduleRepTransition(TimeSpan delay, bool memory)
    {
        CancelRepTransition();
        _pendingRep = () =>
        {
            // From-memory reps start with the guide off (showing it counts as guide use).
            if (memory && ShowGuideCheck.IsChecked == true) ShowGuideCheck.IsChecked = false;
            WritingCanvas.Strokes.Clear();
            _currentStrokeIndex = 0;
            _failsByStroke.Clear();
            _guideUsedThisPass = false;
            ResetScores();
            ShowCurrentCard(keepStatus: true);
        };
        var timer = new DispatcherTimer { Interval = delay };
        timer.Tick += (_, _) => FlushRepTransition();
        _repTimer = timer;
        timer.Start();
    }

    /// <summary>Do a pending rep change right now (before Undo/Clear act on the new rep).</summary>
    private void FlushRepTransition()
    {
        var pending = _pendingRep;
        CancelRepTransition();
        pending?.Invoke();
    }

    private void CancelRepTransition()
    {
        _repTimer?.Stop();
        _repTimer = null;
        _pendingRep = null;
    }

    private string SheetProgressPrefix() => _sheet switch
    {
        null => string.Empty,
        { IsComplete: true } => "Sheet complete · ",
        var s => $"Rep {s.CurrentRep} of {SheetSession.RepCount}{(s.CurrentIsMemory ? " (memory)" : string.Empty)} · "
    };

    private string? SheetTip() => _sheet switch
    {
        null => null,
        { IsComplete: true } => "Sheet done. Next for another kanji, Clear to write the sheet again.",
        { CurrentRep: 1 } => "Rep 1: trace the model, starting at the red dot.",
        { CurrentIsMemory: false } s => $"Rep {s.CurrentRep}: trace the fainter model.",
        var s => $"Rep {s.CurrentRep}: from memory. Space peeks, but then the rep is written again."
    };

    /// <summary>Copy of a rep's ink in plain notebook pencil, thick enough to read when shrunk.</summary>
    private StrokeCollection ThumbnailInk(StrokeCollection strokes)
    {
        var copy = strokes.Clone();
        var ink = ((SolidColorBrush)FindResource("InkBrush")).Color;
        foreach (var stroke in copy)
        {
            stroke.DrawingAttributes.Color = ink;
            stroke.DrawingAttributes.Width = 34;
            stroke.DrawingAttributes.Height = 34;
        }
        return copy;
    }

    private void UpdateSheetStrip()
    {
        SheetStrip.Visibility = _settings.SheetMode ? Visibility.Visible : Visibility.Collapsed;
        SheetBoxes.Children.Clear();
        if (!_settings.SheetMode) return;

        var card = _deck.Count > 0 && _cardIndex < _deck.Count ? _deck[_cardIndex] : null;
        UpdateSheetReading(card?.Character);
        for (var rep = 1; rep <= SheetSession.RepCount; rep++)
        {
            SheetBoxes.Children.Add(BuildSheetBox(rep, card));
        }
    }

    private void UpdateSheetReading(string? character)
    {
        var reading = SheetReading.For(_dictionary?.Lookup(character));
        var hidden = reading.Length > 0 && !MeaningsShown;
        SheetReadingLabel.Text = hidden ? "？" : SheetReading.Vertical(reading);
        SheetReadingLabel.Foreground = hidden ? Brushes.Gray : new SolidColorBrush(Color.FromRgb(0x2F, 0x6F, 0x47));
    }

    private FrameworkElement BuildSheetBox(int rep, KanjiCard? card)
    {
        var result = _sheet?.ResultFor(rep);
        var isCurrent = card is not null && _sheet is { IsComplete: false } && _sheet.CurrentRep == rep;
        var frame = (Brush)FindResource("NotebookFrameBrush");

        // Everything is drawn at full writing-box scale and shrunk by the Viewbox.
        var design = new Grid { Width = SheetInnerSize, Height = SheetInnerSize, Background = (Brush)FindResource("CanvasBackground") };
        if (result is not null)
        {
            design.Children.Add(new Border { Background = new SolidColorBrush(ColorFor(result.Average)), Opacity = 0.12 });
        }

        var cross = new Canvas();
        foreach (var (x1, y1, x2, y2) in new[] { (SheetInnerSize / 2, 0.0, SheetInnerSize / 2, SheetInnerSize), (0.0, SheetInnerSize / 2, SheetInnerSize, SheetInnerSize / 2) })
        {
            cross.Children.Add(new Line
            {
                X1 = x1, Y1 = y1, X2 = x2, Y2 = y2,
                Stroke = (Brush)FindResource("GridLineBrush"),
                StrokeThickness = 14,
                StrokeDashArray = new DoubleCollection { 0.1, 3 },
                StrokeDashCap = PenLineCap.Round
            });
        }
        design.Children.Add(cross);

        var ink = _repInk[rep - 1];
        if (ink is not null && result is not null)
        {
            design.Children.Add(new InkPresenter { Strokes = ink });
        }
        else if (card is not null && SheetSession.ModelOpacity(rep) > 0)
        {
            // Not written yet: preview the model like the printed boxes in a notebook (solid, fainter, blank).
            var model = new Canvas();
            DrawModel(model, card, SheetSession.ModelOpacity(rep) * (rep == 1 ? 0.6 : 1.0));
            design.Children.Add(model);
        }

        var outer = new Grid();
        outer.Children.Add(new Viewbox { Stretch = Stretch.Uniform, Child = design });

        outer.Children.Add(new TextBlock
        {
            Text = rep.ToString(),
            FontSize = 13,
            Foreground = SheetSession.IsMemoryRep(rep) ? frame : Brushes.Gray,
            FontWeight = SheetSession.IsMemoryRep(rep) ? FontWeights.Bold : FontWeights.Normal,
            Margin = new Thickness(5, 2, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        });

        if (result is not null)
        {
            outer.Children.Add(new Border
            {
                Background = new SolidColorBrush(ColorFor(result.Average)),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(6, 1, 6, 1),
                Margin = new Thickness(0, 0, 4, 4),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Child = new TextBlock { Text = result.Average.ToString(), Foreground = Brushes.White, FontSize = 14, FontWeight = FontWeights.SemiBold }
            });
        }

        var kind = rep == 1 ? "trace the model" : SheetSession.IsMemoryRep(rep) ? "from memory (counts for the review)" : "trace the fainter model";
        return new Border
        {
            Margin = new Thickness(6),
            BorderBrush = isCurrent ? (Brush)FindResource("GuideCurrentBrush") : frame,
            BorderThickness = new Thickness(isCurrent ? 6 : 3),
            Background = (Brush)FindResource("CanvasBackground"),
            ToolTip = $"Rep {rep}: {kind}" + (result is null ? string.Empty : $" · {result.Average}"),
            Child = outer
        };
    }

    /// <summary>All strokes in one gray at the given strength, for tracing (no stroke-order hint).</summary>
    private static void DrawModel(Canvas target, KanjiCard card, double opacity)
    {
        foreach (var stroke in card.Strokes)
        {
            if (stroke.Points.Count < 2) continue;
            target.Children.Add(new System.Windows.Shapes.Path
            {
                Data = StrokeGeometry(stroke),
                Stroke = FadedModelBrush,
                Opacity = opacity,
                StrokeThickness = 22,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                Fill = Brushes.Transparent
            });
        }
    }
}
