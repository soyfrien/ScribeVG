using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ScribeVG.Services;

namespace ScribeVG;

/// <summary>
/// Speech bubble on the big kanji: one example sentence, Emi's voice when a clip is available,
/// and a quiet text-only look when it isn't.
/// </summary>
public partial class MainWindow
{
    private const string BubbleHiddenHint = "Reading hidden — tap to show";
    private static readonly TimeSpan BubbleEmptyLinger = TimeSpan.FromSeconds(2);

    private readonly List<ExampleSentence> _bubbleSentences = [];
    private int _bubbleIndex;
    private string? _bubbleKanji;
    private DispatcherTimer? _bubbleDismissTimer;
    private bool _bubbleOpen;

    private void InitBubble()
    {
        SentenceBubble.CustomPopupPlacementCallback = PlaceBubble;
        SentenceBubble.PlacementTarget = CharacterLabel;
        PreviewMouseDown += Window_PreviewDismissBubble;
        PreviewStylusDown += Window_PreviewDismissBubble;
        PreviewTouchDown += Window_PreviewDismissBubble;
    }

    private static CustomPopupPlacement[] PlaceBubble(Size popupSize, Size targetSize, Point offset)
    {
        // Sit to the left of the kanji, with the tail aimed at its vertical center.
        const double tailFromTop = 44;
        var x = -popupSize.Width + 8 + offset.X;
        var y = targetSize.Height / 2 - tailFromTop + offset.Y;
        return [new CustomPopupPlacement(new Point(x, y), PopupPrimaryAxis.Horizontal)];
    }

    private void CharacterLabel_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (_bubbleOpen)
        {
            CloseSentenceBubble(stopAudio: false);
            return;
        }

        var kanji = CurrentBubbleKanji();
        if (kanji is null) return;
        OpenSentenceBubble(kanji);
    }

    private string? CurrentBubbleKanji()
    {
        if (_deck.Count == 0 || _cardIndex >= _deck.Count) return null;
        var kanji = _deck[_cardIndex].Character;
        return string.IsNullOrEmpty(kanji) || kanji is "—" or "✓" ? null : kanji;
    }

    private void OpenSentenceBubble(string kanji)
    {
        CancelBubbleDismiss();
        _bubbleKanji = kanji;
        _bubbleSentences.Clear();
        _bubbleSentences.AddRange(SentencesForBubble(kanji));
        _bubbleIndex = 0;
        _bubbleOpen = true;

        FillBubble();
        SentenceBubble.IsOpen = true;
        PlayBubbleOpenAnimation();

        if (_bubbleSentences.Count == 0)
        {
            ScheduleBubbleDismiss();
            return;
        }

        PlayCurrentBubbleClip();
    }

    /// <summary>Shortest Japanese first; if two are the same length, prefer one that has a clip.</summary>
    private List<ExampleSentence> SentencesForBubble(string? kanji)
    {
        var all = (_sentences?.For(kanji) ?? []).ToList();
        all.Sort((a, b) =>
        {
            var length = a.Japanese.Length.CompareTo(b.Japanese.Length);
            if (length != 0) return length;
            if (a.HasAudio != b.HasAudio) return a.HasAudio ? -1 : 1;
            var aLocal = _audio?.Find(a.Id) is not null;
            var bLocal = _audio?.Find(b.Id) is not null;
            if (aLocal != bLocal) return aLocal ? -1 : 1;
            return 0;
        });
        return all;
    }

    private void FillBubble()
    {
        if (_bubbleSentences.Count == 0)
        {
            BubbleEmptyLabel.Text = _bubbleKanji is null
                ? "No example sentences."
                : $"No example sentences for {_bubbleKanji} yet.";
            BubbleEmptyLabel.Visibility = Visibility.Visible;
            BubbleBody.Visibility = Visibility.Collapsed;
            return;
        }

        BubbleEmptyLabel.Visibility = Visibility.Collapsed;
        BubbleBody.Visibility = Visibility.Visible;

        var sentence = _bubbleSentences[_bubbleIndex];
        var kanji = _bubbleKanji ?? "";

        BubbleJapanese.Inlines.Clear();
        foreach (var piece in SplitKeeping(sentence.Japanese, kanji))
        {
            BubbleJapanese.Inlines.Add(piece == kanji
                ? new System.Windows.Documents.Run(piece) { Foreground = HighlightBrush, FontWeight = FontWeights.Bold }
                : new System.Windows.Documents.Run(piece));
        }
        BubbleJapanese.Cursor = _bubbleSentences.Count > 1 ? Cursors.Hand : Cursors.Arrow;
        BubbleJapanese.ToolTip = _bubbleSentences.Count > 1 ? "Tap for the next sentence" : null;

        BubbleReading.Text = sentence.Reading ?? "";
        BubbleEnglish.Text = sentence.English ?? "";

        BubbleIndexLabel.Text = $"{_bubbleIndex + 1}/{_bubbleSentences.Count}";
        BubbleNextButton.Visibility = _bubbleSentences.Count > 1 ? Visibility.Visible : Visibility.Collapsed;

        if (sentence.HasAudio)
        {
            BubbleReplayButton.Visibility = Visibility.Visible;
            BubbleReplayButton.IsEnabled = true;
            BubbleReplayButton.Opacity = 1;
            BubbleReplayButton.ToolTip = "Play Emi's reading";
            BubbleHint.Visibility = Visibility.Collapsed;
        }
        else
        {
            BubbleReplayButton.Visibility = Visibility.Collapsed;
            BubbleHint.Text = "No recording for this one";
            BubbleHint.Visibility = Visibility.Visible;
        }

        ApplyBubbleSecrets();
    }

    private void ApplyBubbleSecrets()
    {
        if (!_bubbleOpen || _bubbleSentences.Count == 0) return;

        var shown = MeaningsShown;
        var sentence = _bubbleSentences[_bubbleIndex];
        var hasReading = !string.IsNullOrEmpty(sentence.Reading);
        var hasEnglish = !string.IsNullOrEmpty(sentence.English);
        var hide = !shown && (hasReading || hasEnglish);

        BubbleReading.Visibility = shown && hasReading ? Visibility.Visible : Visibility.Collapsed;
        BubbleEnglish.Visibility = shown && hasEnglish ? Visibility.Visible : Visibility.Collapsed;
        BubblePlaceholder.Visibility = hide ? Visibility.Visible : Visibility.Collapsed;
        BubblePlaceholder.Text = BubbleHiddenHint;
    }

    private void BubblePlaceholder_Click(object sender, RoutedEventArgs e)
    {
        if (MeaningsShown) return;
        _meaningRevealed = true;
        ApplyMeaningVisibility();
        e.Handled = true;
    }

    private void BubbleReplay_Click(object sender, RoutedEventArgs e)
    {
        PlayCurrentBubbleClip();
        e.Handled = true;
    }

    private void BubbleNext_Click(object sender, RoutedEventArgs e)
    {
        ShowNextBubbleSentence();
        e.Handled = true;
    }

    private void BubbleJapanese_Click(object sender, RoutedEventArgs e)
    {
        if (_bubbleSentences.Count <= 1) return;
        ShowNextBubbleSentence();
        e.Handled = true;
    }

    private void ShowNextBubbleSentence()
    {
        if (_bubbleSentences.Count == 0) return;
        _bubbleIndex = (_bubbleIndex + 1) % _bubbleSentences.Count;
        FillBubble();
        PlayCurrentBubbleClip();
    }

    private void PlayCurrentBubbleClip()
    {
        if (_bubbleSentences.Count == 0) return;
        PlaySentenceAudio(_bubbleSentences[_bubbleIndex]);
    }

    private void CloseSentenceBubble(bool stopAudio)
    {
        CancelBubbleDismiss();
        if (!_bubbleOpen && !SentenceBubble.IsOpen) return;
        _bubbleOpen = false;
        SentenceBubble.IsOpen = false;
        _bubbleKanji = null;
        _bubbleSentences.Clear();
        if (stopAudio) StopSentenceAudio();
    }

    /// <summary>Close the bubble when the shown kanji changes (Next, Previous, auto-advance, new deck).</summary>
    private void DismissBubbleIfKanjiChanged(string? kanji)
    {
        if (!_bubbleOpen) return;
        if (kanji == _bubbleKanji) return;
        CloseSentenceBubble(stopAudio: true);
    }

    private void PlayBubbleOpenAnimation()
    {
        BubbleScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        BubbleScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        BubbleRoot.BeginAnimation(OpacityProperty, null);
        BubbleScale.ScaleX = 1;
        BubbleScale.ScaleY = 1;
        BubbleRoot.Opacity = 1;
        if (_settings.ReduceMotion) return;

        var duration = TimeSpan.FromMilliseconds(180);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        BubbleScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.86, 1, duration) { EasingFunction = ease });
        BubbleScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.86, 1, duration) { EasingFunction = ease });
        BubbleRoot.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));
    }

    private void Window_PreviewDismissBubble(object? sender, InputEventArgs e)
    {
        if (!_bubbleOpen) return;
        if (IsOverBubbleOrAnchor(e.OriginalSource as DependencyObject)) return;

        CloseSentenceBubble(stopAudio: false);
        // Swallow only taps that would otherwise become ink on the writing surface.
        if (IsOverWritingSurface(e.OriginalSource as DependencyObject)) e.Handled = true;
    }

    private bool IsOverBubbleOrAnchor(DependencyObject? src)
    {
        while (src is not null)
        {
            if (ReferenceEquals(src, CharacterLabel) ||
                ReferenceEquals(src, BubbleRoot) ||
                ReferenceEquals(src, SentenceBubble.Child))
            {
                return true;
            }
            src = VisualTreeHelper.GetParent(src) ?? LogicalTreeHelper.GetParent(src);
        }
        return false;
    }

    private bool IsOverWritingSurface(DependencyObject? src)
    {
        while (src is not null)
        {
            if (ReferenceEquals(src, WritingCanvas) || ReferenceEquals(src, WritingSurface)) return true;
            src = VisualTreeHelper.GetParent(src) ?? LogicalTreeHelper.GetParent(src);
        }
        return false;
    }

    private void ScheduleBubbleDismiss()
    {
        CancelBubbleDismiss();
        var timer = new DispatcherTimer { Interval = BubbleEmptyLinger };
        _bubbleDismissTimer = timer;
        timer.Tick += (_, _) =>
        {
            CancelBubbleDismiss();
            CloseSentenceBubble(stopAudio: false);
        };
        timer.Start();
    }

    private void CancelBubbleDismiss()
    {
        _bubbleDismissTimer?.Stop();
        _bubbleDismissTimer = null;
    }
}
