using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using ScribeVG.Models;
using ScribeVG.Services;
using Microsoft.Win32;
using System.Windows.Threading;

namespace ScribeVG;

public partial class MainWindow : Window
{
    // KanjiVG viewBox is 109×109; the design surface is 10× that for stylus room.
    private const double SourceSize = 109.0;
    private const double DesignSize = 1090.0;
    private const double Scale = DesignSize / SourceSize;

    private readonly KanjiStrokeLoader _loader = new();
    private readonly StrokeMatcher _matcher = new();
    private readonly KanjiDictionary? _dictionary = LoadDictionary();
    private readonly List<KanjiCard> _deck = [];
    private int _cardIndex;
    private int _currentStrokeIndex;
    private bool _peeking;

    // Scores for strokes accepted so far on the current card, and how many tries each took.
    private readonly List<int> _strokeScores = [];
    private readonly List<int> _strokeAttempts = [];
    private int _attemptsOnCurrentStroke;

    // Study mode: spaced repetition over the bundled KanjiVG set.
    private readonly ProgressStore _progress = ProgressStore.Load();
    private StudyPlanner? _planner;
    private string? _bundledDir;
    private bool _studyMode;
    private readonly List<string> _studyHistory = [];

    // What happened on the current card since it was shown (survives Undo/Clear) for the auto-rating.
    private bool _guideUsed;
    private bool _guideUsedThisPass; // guide shown since the canvas was last cleared
    private bool _anyReversed;
    private bool _rated;
    private readonly Dictionary<int, int> _failsByStroke = [];

    private static readonly Color ExcellentColor = Color.FromRgb(0x2E, 0x7D, 0x32);
    private static readonly Color GoodColor = Color.FromRgb(0x55, 0x8B, 0x2F);
    private static readonly Color OkColor = Color.FromRgb(0xE0, 0x8E, 0x0B);
    private static readonly Color MissColor = Color.FromRgb(0xC6, 0x28, 0x28);
    public MainWindow()
    {
        InitializeComponent();
        ConfigureInk();
        DrawBackgroundGrid();
        InitSentences();
        InitAutoAdvance();
        if (!TryStartStudy())
        {
            TryLoadBundledSamples();
            StatusLabel.Text =
                "Write the kanji from memory. Press Space (or tick the box) to show the stroke guide.";
        }
        UpdateTodayPanel();
        Closed += (_, _) => SaveProgress();
    }

    private static string? FindDataDir()
    {
        foreach (var dir in new[]
                 {
                     System.IO.Path.Combine(AppContext.BaseDirectory, "Data"),
                     System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Data")
                 })
        {
            if (File.Exists(System.IO.Path.Combine(dir, "components.json"))) return dir;
        }
        return null;
    }

    private bool TryStartStudy()
    {
        if (_planner is null)
        {
            var dataDir = FindDataDir();
            var graph = dataDir is null ? null : ComponentGraph.TryLoad(System.IO.Path.Combine(dataDir, "components.json"));
            if (graph is null || _dictionary is null) return false;
            _bundledDir = System.IO.Path.Combine(dataDir!, "kanjivg");
            _planner = new StudyPlanner(graph, _dictionary.Lookup, _progress.Data);
        }

        _studyMode = true;
        ShowNextStudyCard();
        return true;
    }

    private void Study_Click(object sender, RoutedEventArgs e)
    {
        if (!TryStartStudy())
        {
            StatusLabel.Text = "Study mode needs Data/components.json and Data/kanjidic.json next to the app.";
        }
    }

    private void ShowNextStudyCard(string? avoid = null)
    {
        if (_planner is null) return;
        var next = _planner.Next(DateTimeOffset.Now, avoid);
        if (next is null)
        {
            _deck.Clear();
            _cardIndex = 0;
            WritingCanvas.Strokes.Clear();
            ResetCardTracking();
            ResetScores();
            GuideCanvas.Children.Clear();
            CharacterLabel.Text = "✓";
            ShowDefinition(null);
            MeaningLabel.Text = "All done for today";
            StrokeProgressLabel.Text = "No reviews due and today's new kanji are done.";
            StatusLabel.Text = "Come back later for reviews, or open a kanji folder for free practice.";
            UpdateTodayPanel();
            return;
        }

        LoadStudyCard(next);
    }

    private bool LoadStudyCard(string kanji, bool remember = true)
    {
        if (_bundledDir is null) return false;
        var file = System.IO.Path.Combine(_bundledDir, $"{char.ConvertToUtf32(kanji, 0):x5}.svg");
        KanjiCard? card = null;
        try { card = _loader.LoadFile(file); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Failed to load {file}: {ex.Message}"); }
        if (card is null || card.Strokes.Count == 0)
        {
            StatusLabel.Text = $"Couldn't load {kanji}.";
            return false;
        }

        if (remember && (_studyHistory.Count == 0 || _studyHistory[^1] != kanji)) _studyHistory.Add(kanji);
        _deck.Clear();
        _deck.Add(card);
        _cardIndex = 0;
        _currentStrokeIndex = 0;
        WritingCanvas.Strokes.Clear();
        ResetCardTracking();
        ResetScores();
        ShowCurrentCard(keepStatus: true);
        StatusLabel.Text = StudyCardIntro(kanji);
        UpdateTodayPanel();
        return true;
    }

    private string StudyCardIntro(string kanji)
    {
        var state = _planner?.StateOf(kanji);
        if (!WillRate(kanji)) return "Practice only: this kanji isn't due, so it won't be scheduled.";
        return state is null || state.State == CardPhase.New
            ? "New kanji. Press Space for the stroke guide the first time."
            : $"Review ({state.Reps} so far). Write it from memory.";
    }

    /// <summary>Whether finishing this kanji now should count as a scheduled review.</summary>
    private bool WillRate(string? kanji)
    {
        if (!_studyMode || _planner is null || kanji is null || !_planner.IsStudyCard(kanji)) return false;
        var now = DateTimeOffset.Now;
        var state = _planner.StateOf(kanji);
        if (state is null || state.State == CardPhase.New)
        {
            return _planner.IsUnlocked(kanji) && _planner.NewQuotaLeft(now) > 0;
        }
        return state.Due <= now + StudyPlanner.LearnAhead;
    }

    private void ResetCardTracking()
    {
        _guideUsed = false;
        _guideUsedThisPass = false;
        _anyReversed = false;
        _rated = false;
        _failsByStroke.Clear();
    }

    private void SaveProgress()
    {
        try { _progress.Save(); }
        catch (Exception ex) { StatusLabel.Text = $"Couldn't save progress: {ex.Message}"; }
    }

    private void FinishStudyCard(KanjiCard card)
    {
        if (_planner is null || _rated || !WillRate(card.Character)) return;
        _rated = true;

        var tries = Enumerable.Range(0, card.Strokes.Count)
            .Select(i => 1 + _failsByStroke.GetValueOrDefault(i))
            .ToList();
        var attempt = new KanjiAttempt(_strokeScores.ToList(), tries, _guideUsed, _anyReversed);
        var rating = PracticeRating.Rate(attempt);
        var outcome = _planner.Record(card.Character!, rating, DateTimeOffset.Now);
        SaveProgress();

        var reasons = new List<string>();
        if (_guideUsed) reasons.Add("used the guide");
        if (_anyReversed) reasons.Add("a stroke went backwards");
        if (tries.Any(t => t >= 3)) reasons.Add("a stroke took 3+ tries");
        var why = reasons.Count > 0 ? $" ({string.Join(", ", reasons)})" : string.Empty;
        var when = outcome.Card.Due is DateTimeOffset due ? DescribeWait(due - DateTimeOffset.Now) : "later";
        StatusLabel.Text = $"{PracticeRating.Describe(rating)}{why}. Next review {when}. Press Next for the next kanji.";

        if (outcome.Unlocked.Count > 0)
        {
            var shown = outcome.Unlocked.Take(3)
                .Select(u => $"{u.Kanji} (you learned {string.Join(" ", u.Parts)})");
            var more = outcome.Unlocked.Count > 3 ? $" and {outcome.Unlocked.Count - 3} more" : string.Empty;
            UnlockLabel.Text = "Unlocked " + string.Join(", ", shown) + more;
        }

        UpdateTodayPanel();
    }

    private static string DescribeWait(TimeSpan wait)
    {
        if (wait.TotalMinutes < 60) return $"in {Math.Max(1, (int)Math.Round(wait.TotalMinutes))} min";
        var days = (int)Math.Round(wait.TotalDays);
        return days <= 1 ? "tomorrow" : $"in {days} days";
    }

    private void UpdateTodayPanel()
    {
        if (_planner is null)
        {
            TodayLabel.Text = "Study mode unavailable (Data/components.json missing).";
            WeakLabel.Text = string.Empty;
            return;
        }

        var now = DateTimeOffset.Now;
        var due = _planner.DueCards(now).Count;
        var newLeft = Math.Min(_planner.NewQuotaLeft(now), _planner.NewCandidates().Take(StudyPlanner.NewCardsPerDay).Count());
        var learned = _progress.Data.Cards.Values.Count(c => c.Learned);
        TodayLabel.Text = $"{due} due · {newLeft} new left today · {learned} learned" +
                          (_studyMode ? string.Empty : "\n(free practice: press Study to go back)");

        var weak = _planner.WeakestStrokeTypes();
        WeakLabel.Text = weak.Count == 0
            ? "Weak spots appear after a few strokes of each type."
            : "Weakest strokes:\n" + string.Join("\n", weak.Select(w =>
                $"{w.Key} {StrokeTypes.Name(w.Key)}: {w.Value.Average:F0} avg over {w.Value.Attempts}"));
    }

    private void ConfigureInk()
    {
        var brush = (Brush)FindResource("InkBrush");
        WritingCanvas.DefaultDrawingAttributes = new DrawingAttributes
        {
            Color = ((SolidColorBrush)brush).Color,
            Width = 14,
            Height = 14,
            FitToCurve = true,
            IgnorePressure = false,
            StylusTip = StylusTip.Ellipse
        };
    }

    private void DrawBackgroundGrid()
    {
        GridCanvas.Children.Clear();
        var brush = (Brush)FindResource("GridLineBrush");

        // Kanji practice notebook (漢字練習帳): a dotted cross through the middle, dividing the square into four.
        void Line(double x1, double y1, double x2, double y2)
        {
            GridCanvas.Children.Add(new Line
            {
                X1 = x1, Y1 = y1, X2 = x2, Y2 = y2,
                Stroke = brush,
                StrokeThickness = 4,
                StrokeDashArray = new DoubleCollection { 0.1, 3.5 }, // round dots, in units of thickness
                StrokeDashCap = PenLineCap.Round
            });
        }

        Line(DesignSize / 2, 0, DesignSize / 2, DesignSize);
        Line(0, DesignSize / 2, DesignSize, DesignSize / 2);
    }

    private void TryLoadBundledSamples()
    {
        var sampleDir = System.IO.Path.Combine(AppContext.BaseDirectory, "Data");
        if (!Directory.Exists(sampleDir))
        {
            // In the project tree during development the files live next to the csproj.
            sampleDir = System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Data");
        }

        if (Directory.Exists(sampleDir))
        {
            LoadDeck(_loader.LoadDirectory(sampleDir));
        }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose a folder of Parser JSON files",
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == true)
        {
            LoadDeck(_loader.LoadDirectory(dialog.FolderName));
        }
    }

    private void OpenFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open a parsed KanjiVG JSON file",
            Filter = "KanjiVG SVG or parsed JSON (*.svg;*.json)|*.svg;*.json|All files (*.*)|*.*",
            Multiselect = true
        };

        if (dialog.ShowDialog(this) == true)
        {
            LoadDeck(_loader.LoadFiles(dialog.FileNames));
        }
    }

    private void LoadDeck(IReadOnlyList<KanjiCard> cards)
    {
        _studyMode = false;
        ResetCardTracking();
        _deck.Clear();
        _deck.AddRange(cards);
        _cardIndex = 0;
        _currentStrokeIndex = 0;
        WritingCanvas.Strokes.Clear();
        ResetScores();

        if (_deck.Count == 0)
        {
            CharacterLabel.Text = "—";
            ShowDefinition(null);
            StrokeProgressLabel.Text = "No kanji JSON found.";
            StatusLabel.Text = "The Parser CLI writes one JSON file per SVG. Point this app at that output folder.";
            GuideCanvas.Children.Clear();
            return;
        }

        ShowCurrentCard();
        StatusLabel.Text = $"Loaded {_deck.Count} character(s) for free practice. Press Space to show or hide the stroke guide.";
        UpdateTodayPanel();
    }

    private void Previous_Click(object sender, RoutedEventArgs e)
    {
        if (!_studyMode)
        {
            MoveCard(-1);
            return;
        }

        // Study mode: step back through the kanji shown this session.
        if (_studyHistory.Count < 2)
        {
            StatusLabel.Text = "No earlier kanji this session.";
            return;
        }
        _studyHistory.RemoveAt(_studyHistory.Count - 1);
        LoadStudyCard(_studyHistory[^1], remember: false);
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        CancelAutoAdvance();
        if (!_studyMode)
        {
            MoveCard(1);
            return;
        }

        // Study mode: go to the next queued kanji. Skipping an unfinished one leaves it queued.
        var current = _deck.Count > 0 ? _deck[_cardIndex].Character : null;
        ShowNextStudyCard(avoid: _rated ? null : current);
    }

    private void MoveCard(int delta)
    {
        if (_deck.Count == 0) return;
        _cardIndex = (_cardIndex + delta + _deck.Count) % _deck.Count;
        _currentStrokeIndex = 0;
        WritingCanvas.Strokes.Clear();
        ResetCardTracking();
        ResetScores();
        ShowCurrentCard();
    }

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (WritingCanvas.Strokes.Count == 0) return;
        WritingCanvas.Strokes.RemoveAt(WritingCanvas.Strokes.Count - 1);
        if (_currentStrokeIndex > 0)
        {
            _currentStrokeIndex--;
        }

        if (_strokeScores.Count > 0)
        {
            _strokeScores.RemoveAt(_strokeScores.Count - 1);
            _strokeAttempts.RemoveAt(_strokeAttempts.Count - 1);
        }
        _attemptsOnCurrentStroke = 0;
        UpdateKanjiTally();

        ShowCurrentCard(keepStatus: true);
        StatusLabel.Text = "Undid the last stroke.";
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        CancelAutoAdvance();
        WritingCanvas.Strokes.Clear();
        _currentStrokeIndex = 0;
        _guideUsedThisPass = false;
        ResetScores();
        ShowCurrentCard(keepStatus: true);
        StatusLabel.Text = "Canvas cleared.";
    }

    private void Peek_Down(object sender, RoutedEventArgs e)
    {
        if (_peeking) return;
        _peeking = true;
        RedrawGuide();
        e.Handled = true;
    }

    private void Peek_Up(object sender, RoutedEventArgs e)
    {
        if (!_peeking) return;
        _peeking = false;
        RedrawGuide();
    }

    // Space: a quick tap toggles the guide; holding it shows the guide only while held.
    private static readonly TimeSpan SpaceTapThreshold = TimeSpan.FromMilliseconds(300);
    private DateTime? _spaceDownAt;

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // ` / ~ key (Oem3 on US layouts): undo the last stroke, same as the Undo button.
        if (e.Key == Key.Oem3)
        {
            e.Handled = true;
            if (!e.IsRepeat) Undo_Click(sender, new RoutedEventArgs());
            return;
        }

        if (e.Key != Key.Space) return;
        e.Handled = true; // so a focused button doesn't also "click"
        if (e.IsRepeat || _spaceDownAt is not null) return;

        _spaceDownAt = DateTime.UtcNow;
        _peeking = true;
        RedrawGuide();
    }

    private void Window_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space) return;
        e.Handled = true;
        if (_spaceDownAt is not DateTime downAt) return;

        _spaceDownAt = null;
        _peeking = false;
        if (DateTime.UtcNow - downAt < SpaceTapThreshold)
        {
            ShowGuideCheck.IsChecked = ShowGuideCheck.IsChecked != true; // redraws via Changed
        }
        else
        {
            RedrawGuide();
        }
    }

    private void ShowGuideCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (IsLoaded)
        {
            RedrawGuide();
        }
    }

    private void WritingCanvas_StrokeCollected(object sender, InkCanvasStrokeCollectedEventArgs e)
    {
        if (_deck.Count == 0 || _cardIndex >= _deck.Count)
        {
            return;
        }

        var card = _deck[_cardIndex];
        if (_currentStrokeIndex >= card.Strokes.Count)
        {
            StatusLabel.Text = "This character is complete. Press Next for another one.";
            return;
        }

        var user = StrokeMatcher.FromInkStroke(e.Stroke, Scale);
        var canonical = card.Strokes[_currentStrokeIndex];
        var result = _matcher.Compare(canonical, user);
        _attemptsOnCurrentStroke++;
        if (result.Reversed) _anyReversed = true;
        if (!result.Passed) _failsByStroke[_currentStrokeIndex] = _failsByStroke.GetValueOrDefault(_currentStrokeIndex) + 1;
        _planner?.RecordStroke(canonical.StrokeType, result.Score);

        ShowStrokeFeedback(result);

        if (result.Passed)
        {
            e.Stroke.DrawingAttributes.Color = ColorFor(result.Score);
            var end = e.Stroke.StylusPoints[^1];
            PoofAt(new Point(end.X, end.Y), ColorFor(result.Score), result.Score);
            _strokeScores.Add(result.Score);
            _strokeAttempts.Add(_attemptsOnCurrentStroke);
            _attemptsOnCurrentStroke = 0;
            _currentStrokeIndex++;
            StatusLabel.Text = result.Summary;
            if (_currentStrokeIndex >= card.Strokes.Count)
            {
                StatusLabel.Text = "All strokes done! Press Next for another kanji, or Clear to try again.";
                FinishStudyCard(card);
                CelebrateKanji((int)Math.Round(_strokeScores.Average()));
                MaybeAutoAdvance();
            }
        }
        else
        {
            // Show the miss in red briefly, then remove it so the canvas only holds accepted strokes.
            var missed = e.Stroke;
            missed.DrawingAttributes.Color = MissColor;
            ShakeMiss();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                WritingCanvas.Strokes.Remove(missed);
            };
            timer.Start();
            StatusLabel.Text = $"{result.Summary}. Try stroke {canonical.StrokeNumber} again.";
        }

        UpdateKanjiTally();
        ShowCurrentCard(keepStatus: true);
        UpdateTodayPanel();
    }

    private void ShowCurrentCard(bool keepStatus = false)
    {
        if (_deck.Count == 0) return;

        var card = _deck[_cardIndex];
        CharacterLabel.Text = string.IsNullOrEmpty(card.Character) ? "—" : card.Character;
        ShowDefinition(card.Character);
        var total = card.Strokes.Count;
        var current = Math.Min(_currentStrokeIndex + 1, Math.Max(total, 1));
        StrokeProgressLabel.Text = total == 0
            ? "No strokes in this file"
            : $"Stroke {Math.Min(current, total)} of {total}" +
              (_currentStrokeIndex >= total ? " — complete" : string.Empty);

        if (!keepStatus)
        {
            StatusLabel.Text = string.IsNullOrEmpty(card.SourceFile)
                ? $"Character {card.Character}"
                : $"{card.SourceFile}";
        }

        RedrawGuide();
    }

    private static KanjiDictionary? LoadDictionary()
    {
        foreach (var dir in new[]
                 {
                     System.IO.Path.Combine(AppContext.BaseDirectory, "Data"),
                     System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Data")
                 })
        {
            var dictionary = KanjiDictionary.TryLoad(System.IO.Path.Combine(dir, "kanjidic.json"));
            if (dictionary is not null) return dictionary;
        }

        return null;
    }

    private void ShowDefinition(string? character)
    {
        ShowSentences(character);
        var info = _dictionary?.Lookup(character);
        if (info is null)
        {
            MeaningLabel.Text = _dictionary is null ? "(dictionary not found)" : string.Empty;
            ReadingsLabel.Text = string.Empty;
            KanjiMetaLabel.Text = string.Empty;
            RadicalLabel.Text = string.Empty;
            return;
        }

        MeaningLabel.Text = string.Join(", ", info.Meanings.Take(4));

        var readings = new List<string>();
        if (info.OnReadings.Count > 0) readings.Add("On: " + string.Join("、", info.OnReadings));
        if (info.KunReadings.Count > 0) readings.Add("Kun: " + string.Join("、", info.KunDisplay));
        ReadingsLabel.Text = string.Join("\n", readings);

        var meta = new List<string>();
        if (info.StrokeCount is int strokes) meta.Add($"{strokes} stroke{(strokes == 1 ? "" : "s")}");
        if (info.GradeDisplay is string grade) meta.Add(grade);
        if (info.JlptDisplay is string jlpt) meta.Add(jlpt);
        KanjiMetaLabel.Text = string.Join(" · ", meta);

        var radical = _dictionary!.Radical(info.RadicalNumber);
        RadicalLabel.Text = radical is null
            ? string.Empty
            : $"Radical {info.RadicalNumber}: {radical.Character} ({radical.Name})";

        MeaningLabel.ToolTip = info.Meanings.Count > 4 ? string.Join(", ", info.Meanings) : null;
    }

    private void ResetScores()
    {
        _strokeScores.Clear();
        _strokeAttempts.Clear();
        _attemptsOnCurrentStroke = 0;
        ScoreLabel.Text = "–";
        ScoreLabel.Foreground = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));
        GradeLabel.Text = string.Empty;
        ScoreBar.Value = 0;
        BreakdownLabel.Text = string.Empty;
        TipLabel.Text = ShowGuideCheck.IsChecked == true
            ? "Trace the red stroke, starting at the dot."
            : "Write from memory, or press Space for the guide.";
        UpdateKanjiTally();
    }

    private void ShowStrokeFeedback(StrokeMatchResult result)
    {
        var brush = new SolidColorBrush(result.Passed ? ColorFor(result.Score) : MissColor);
        ScoreLabel.Text = result.Score.ToString();
        ScoreLabel.Foreground = brush;
        GradeLabel.Text = result.Grade;
        GradeLabel.Foreground = brush;
        ScoreBar.Value = result.Score;
        ScoreBar.Foreground = brush;
        BreakdownLabel.Text = result.Reversed
            ? "Drawn in the wrong direction"
            : $"Shape {result.ShapeScore} · Placement {result.PlacementScore} · Length {result.LengthScore}";
        TipLabel.Text = result.Tip;
    }

    private void UpdateKanjiTally()
    {
        StrokeChips.Children.Clear();
        if (_deck.Count == 0)
        {
            KanjiScoreLabel.Text = "–";
            return;
        }

        var total = _deck[_cardIndex].Strokes.Count;
        for (int i = 0; i < total; i++)
        {
            var done = i < _strokeScores.Count;
            var color = done ? ColorFor(_strokeScores[i]) : Color.FromRgb(0xDD, 0xDD, 0xDD);
            StrokeChips.Children.Add(new Border
            {
                Background = new SolidColorBrush(color),
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(0, 4, 6, 0),
                Padding = new Thickness(8, 3, 8, 3),
                ToolTip = done ? $"Stroke {i + 1}: {_strokeScores[i]} ({_strokeAttempts[i]} tr{(_strokeAttempts[i] == 1 ? "y" : "ies")})" : $"Stroke {i + 1}",
                Child = new TextBlock
                {
                    Text = done ? _strokeScores[i].ToString() : (i + 1).ToString(),
                    Foreground = done ? Brushes.White : Brushes.Gray,
                    FontSize = 14,
                    FontWeight = FontWeights.SemiBold
                }
            });
        }

        if (_strokeScores.Count == 0)
        {
            KanjiScoreLabel.Text = "–";
            return;
        }

        var average = (int)Math.Round(_strokeScores.Average());
        var firstTry = _strokeAttempts.Count(a => a == 1);
        KanjiScoreLabel.Text = _strokeScores.Count >= total
            ? $"{average} · {StrokeMatcher.GradeFor(average)} · {firstTry}/{total} first try"
            : $"{average} so far ({_strokeScores.Count}/{total} strokes)";
    }

    private static Color ColorFor(int score) => score switch
    {
        >= 90 => ExcellentColor,
        >= 75 => GoodColor,
        >= StrokeMatcher.PassScore => OkColor,
        _ => MissColor
    };

    private void RedrawGuide()
    {
        GuideCanvas.Children.Clear();
        if (_deck.Count == 0 || (ShowGuideCheck.IsChecked != true && !_peeking))
        {
            return;
        }

        var card = _deck[_cardIndex];
        if (_currentStrokeIndex < card.Strokes.Count)
        {
            _guideUsed = true; // the guide was on screen while this kanji was being written
            _guideUsedThisPass = true;
        }

        var completed = (Brush)FindResource("GuideCompletedBrush");
        var current = (Brush)FindResource("GuideCurrentBrush");
        var upcoming = (Brush)FindResource("GuideUpcomingBrush");

        for (int i = 0; i < card.Strokes.Count; i++)
        {
            var stroke = card.Strokes[i];
            if (stroke.Points.Count < 2) continue;

            var figure = new PathFigure
            {
                StartPoint = ToDesign(stroke.Points[0]),
                IsClosed = false
            };
            for (int p = 1; p < stroke.Points.Count; p++)
            {
                figure.Segments.Add(new LineSegment(ToDesign(stroke.Points[p]), true));
            }

            Brush brush;
            double thickness;
            if (i < _currentStrokeIndex)
            {
                brush = completed;
                thickness = 10;
            }
            else if (i == _currentStrokeIndex)
            {
                brush = current;
                thickness = 14;
            }
            else
            {
                brush = upcoming;
                thickness = 8;
            }

            GuideCanvas.Children.Add(new System.Windows.Shapes.Path
            {
                Data = new PathGeometry(new[] { figure }),
                Stroke = brush,
                StrokeThickness = thickness,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                Fill = Brushes.Transparent
            });

            if (i == _currentStrokeIndex)
            {
                // Start dot so it's clear where (and which way) to begin.
                const double dot = 26;
                var start = ToDesign(stroke.Points[0]);
                var marker = new Ellipse { Width = dot, Height = dot, Fill = current };
                Canvas.SetLeft(marker, start.X - dot / 2);
                Canvas.SetTop(marker, start.Y - dot / 2);
                GuideCanvas.Children.Add(marker);
            }
        }
    }

    private static Point ToDesign(StrokePoint point) => new(point.X * Scale, point.Y * Scale);
}
