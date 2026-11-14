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

namespace ScribeVG;

public partial class MainWindow : Window
{
    // KanjiVG viewBox is 109×109; the design surface is 10× that for stylus room.
    private const double SourceSize = 109.0;
    private const double DesignSize = 1090.0;
    private const double Scale = DesignSize / SourceSize;

    private readonly KanjiStrokeLoader _loader = new();
    private readonly StrokeMatcher _matcher = new();
    private readonly List<KanjiCard> _deck = [];
    private int _cardIndex;
    private int _currentStrokeIndex;
    public MainWindow()
    {
        InitializeComponent();
        ConfigureInk();
        DrawBackgroundGrid();
        TryLoadBundledSamples();
        StatusLabel.Text =
            "Open a folder of JSON files produced by the Parser project, or use the bundled sample cards.";
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

        // Traditional 田 grid: midlines plus a light 3×3.
        void Line(double x1, double y1, double x2, double y2, double thickness)
        {
            GridCanvas.Children.Add(new Line
            {
                X1 = x1, Y1 = y1, X2 = x2, Y2 = y2,
                Stroke = brush,
                StrokeThickness = thickness
            });
        }

        Line(DesignSize / 2, 0, DesignSize / 2, DesignSize, 2);
        Line(0, DesignSize / 2, DesignSize, DesignSize / 2, 2);
        Line(DesignSize / 3, 0, DesignSize / 3, DesignSize, 1);
        Line(DesignSize * 2 / 3, 0, DesignSize * 2 / 3, DesignSize, 1);
        Line(0, DesignSize / 3, DesignSize, DesignSize / 3, 1);
        Line(0, DesignSize * 2 / 3, DesignSize, DesignSize * 2 / 3, 1);
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
        _deck.Clear();
        _deck.AddRange(cards);
        _cardIndex = 0;
        _currentStrokeIndex = 0;
        WritingCanvas.Strokes.Clear();

        if (_deck.Count == 0)
        {
            CharacterLabel.Text = "—";
            StrokeProgressLabel.Text = "No kanji JSON found.";
            StatusLabel.Text = "The Parser CLI writes one JSON file per SVG. Point this app at that output folder.";
            GuideCanvas.Children.Clear();
            return;
        }

        ShowCurrentCard();
        StatusLabel.Text = $"Loaded {_deck.Count} character(s). Trace the highlighted stroke.";
    }

    private void Previous_Click(object sender, RoutedEventArgs e) => MoveCard(-1);

    private void Next_Click(object sender, RoutedEventArgs e) => MoveCard(1);

    private void MoveCard(int delta)
    {
        if (_deck.Count == 0) return;
        _cardIndex = (_cardIndex + delta + _deck.Count) % _deck.Count;
        _currentStrokeIndex = 0;
        WritingCanvas.Strokes.Clear();
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

        ShowCurrentCard(keepStatus: true);
        StatusLabel.Text = "Undid the last stroke.";
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        WritingCanvas.Strokes.Clear();
        _currentStrokeIndex = 0;
        ShowCurrentCard(keepStatus: true);
        StatusLabel.Text = "Canvas cleared.";
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

        if (result.Passed)
        {
            _currentStrokeIndex++;
            StatusLabel.Text = result.Summary;
            if (_currentStrokeIndex >= card.Strokes.Count)
            {
                StatusLabel.Text = "All strokes accepted. Press Next when you are ready.";
            }
        }
        else
        {
            // Leave the ink visible so the user can Undo or Clear and retry.
            StatusLabel.Text = result.Summary + " Try that stroke again, or Undo and retry.";
        }

        ShowCurrentCard(keepStatus: true);
    }

    private void ShowCurrentCard(bool keepStatus = false)
    {
        if (_deck.Count == 0) return;

        var card = _deck[_cardIndex];
        CharacterLabel.Text = string.IsNullOrEmpty(card.Character) ? "—" : card.Character;
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

    private void RedrawGuide()
    {
        GuideCanvas.Children.Clear();
        if (_deck.Count == 0 || ShowGuideCheck.IsChecked != true)
        {
            return;
        }

        var card = _deck[_cardIndex];
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
        }
    }

    private static Point ToDesign(StrokePoint point) => new(point.X * Scale, point.Y * Scale);
}
