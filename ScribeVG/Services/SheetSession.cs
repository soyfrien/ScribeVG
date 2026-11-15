namespace ScribeVG.Services;

/// <summary>What happened when one rep of a sheet was finished.</summary>
public enum SheetStep
{
    /// <summary>The rep counted; move on to the next rep.</summary>
    NextRep,

    /// <summary>A from-memory rep was written with the guide showing: clear it and write it again.</summary>
    Redo,

    /// <summary>Rep 6 is done without the guide: the whole sheet is finished.</summary>
    Complete
}

/// <summary>One finished rep: stroke scores, tries per stroke, and whether the guide was used or a stroke went backwards.</summary>
public sealed record SheetRepResult(
    IReadOnlyList<int> StrokeScores,
    IReadOnlyList<int> TriesPerStroke,
    bool GuideUsed,
    bool AnyReversed)
{
    public int Average => StrokeScores.Count == 0 ? 0 : (int)Math.Round(StrokeScores.Average());
}

/// <param name="Step">What to do next.</param>
/// <param name="Rep">The rep (1-based) that was just finished.</param>
/// <param name="Result">That rep's scores.</param>
/// <param name="RatingAttempt">Set once per sheet, the first time rep 6 is finished: the memory reps to rate the card with.</param>
public sealed record SheetStepResult(SheetStep Step, int Rep, SheetRepResult Result, KanjiAttempt? RatingAttempt);

/// <summary>
/// Sheet mode, like a row in a kids' kanji practice notebook (漢字練習帳): each kanji is written
/// <see cref="RepCount"/> times. Rep 1 traces a solid model, reps 2–3 trace a fading model,
/// and reps 4–6 are written from memory. Only the memory reps count toward the review rating.
/// </summary>
public sealed class SheetSession
{
    public const int RepCount = 6;
    public const int FirstMemoryRep = 4;

    private readonly SheetRepResult?[] _results = new SheetRepResult?[RepCount];
    private bool _guideThisRep;
    private bool _reversedThisRep;

    /// <summary>The rep being written now, 1-based. Stays at <see cref="RepCount"/> once complete.</summary>
    public int CurrentRep { get; private set; } = 1;

    public bool IsComplete { get; private set; }

    /// <summary>The guide was shown during some memory rep at some point (survives redos), so the rating can't be clean.</summary>
    public bool MemoryGuideEverUsed { get; private set; }

    /// <summary>A stroke was drawn backwards during some memory rep (survives redos).</summary>
    public bool MemoryReversedEver { get; private set; }

    /// <summary>The rating attempt has already been handed out for this sheet.</summary>
    public bool RatingTaken { get; private set; }

    public bool GuideUsedThisRep => _guideThisRep;

    /// <summary>How strongly the model is drawn under the writing for a rep: solid, 35%, 15%, then nothing.</summary>
    public static double ModelOpacity(int rep) => rep switch
    {
        1 => 1.0,
        2 => 0.35,
        3 => 0.15,
        _ => 0.0
    };

    /// <summary>Reps 4–6 are written from memory and count toward the review rating.</summary>
    public static bool IsMemoryRep(int rep) => rep >= FirstMemoryRep && rep <= RepCount;

    public bool CurrentIsMemory => !IsComplete && IsMemoryRep(CurrentRep);

    public double CurrentModelOpacity => IsComplete ? 0.0 : ModelOpacity(CurrentRep);

    /// <summary>The kept result for a rep (1-based), or null if it isn't done (or was cleared for a redo).</summary>
    public SheetRepResult? ResultFor(int rep) => rep is >= 1 and <= RepCount ? _results[rep - 1] : null;

    /// <summary>The stroke guide (toggle or peek) was on screen while the current rep was being written.</summary>
    public void MarkGuideUsed()
    {
        if (IsComplete) return;
        _guideThisRep = true;
        if (CurrentIsMemory) MemoryGuideEverUsed = true;
    }

    public void MarkReversed()
    {
        if (IsComplete) return;
        _reversedThisRep = true;
        if (CurrentIsMemory) MemoryReversedEver = true;
    }

    /// <summary>Clear pressed mid-rep: start the rep over, forgetting this pass's guide use (the sticky flags remain).</summary>
    public void ClearCurrentRep()
    {
        _guideThisRep = false;
        _reversedThisRep = false;
    }

    /// <summary>Record the current rep as finished and decide what comes next.</summary>
    public SheetStepResult FinishRep(IReadOnlyList<int> strokeScores, IReadOnlyList<int> triesPerStroke)
    {
        if (IsComplete) throw new InvalidOperationException("The sheet is already complete.");

        var rep = CurrentRep;
        var result = new SheetRepResult(strokeScores.ToList(), triesPerStroke.ToList(), _guideThisRep, _reversedThisRep);
        _guideThisRep = false;
        _reversedThisRep = false;

        KanjiAttempt? rating = null;
        if (rep == RepCount && !RatingTaken)
        {
            // Rate on the first finish of the last rep, even if it gets redone, like the single-box flow.
            RatingTaken = true;
            rating = BuildMemoryAttempt(result);
        }

        if (IsMemoryRep(rep) && result.GuideUsed)
        {
            _results[rep - 1] = null; // traced instead of remembered: write this rep again
            return new SheetStepResult(SheetStep.Redo, rep, result, rating);
        }

        _results[rep - 1] = result;
        if (rep == RepCount)
        {
            IsComplete = true;
            return new SheetStepResult(SheetStep.Complete, rep, result, rating);
        }

        CurrentRep++;
        return new SheetStepResult(SheetStep.NextRep, rep, result, rating);
    }

    /// <summary>Kept memory-rep results so far.</summary>
    public IReadOnlyList<SheetRepResult> MemoryResults() =>
        Enumerable.Range(FirstMemoryRep, RepCount - FirstMemoryRep + 1)
            .Select(ResultFor)
            .OfType<SheetRepResult>()
            .ToList();

    /// <summary>Average over every stroke of the kept memory reps, or null if none are done.</summary>
    public int? MemoryAverage
    {
        get
        {
            var scores = MemoryResults().SelectMany(r => r.StrokeScores).ToList();
            return scores.Count == 0 ? null : (int)Math.Round(scores.Average());
        }
    }

    /// <summary>The memory reps combined, as one attempt for <see cref="PracticeRating"/>.</summary>
    public KanjiAttempt MemoryAttempt() => BuildMemoryAttempt(null);

    /// <summary>Auto-advance only after the full sheet, with the memory average at the cutoff and no guide in the kept memory reps.</summary>
    public bool ShouldAutoAdvance(bool enabled, int cutoff) =>
        enabled && IsComplete && MemoryAverage is int average && average >= cutoff &&
        MemoryResults().All(r => !r.GuideUsed);

    private KanjiAttempt BuildMemoryAttempt(SheetRepResult? lastRep)
    {
        var reps = new List<SheetRepResult>();
        for (var rep = FirstMemoryRep; rep <= RepCount; rep++)
        {
            var kept = rep == RepCount && lastRep is not null ? lastRep : ResultFor(rep);
            if (kept is not null) reps.Add(kept);
        }

        return new KanjiAttempt(
            reps.SelectMany(r => r.StrokeScores).ToList(),
            reps.SelectMany(r => r.TriesPerStroke).ToList(),
            MemoryGuideEverUsed || reps.Any(r => r.GuideUsed),
            MemoryReversedEver || reps.Any(r => r.AnyReversed));
    }
}

/// <summary>The reading shown in the sheet's thin reading column.</summary>
public static class SheetReading
{
    /// <summary>First kun reading in hiragana (okurigana dot removed), else first on reading in katakana.</summary>
    public static string For(KanjiInfo? info)
    {
        if (info is null) return string.Empty;
        var kun = Pick(info.KunReadings);
        if (kun is not null) return kun.Replace(".", string.Empty);
        return Pick(info.OnReadings) ?? string.Empty;
    }

    /// <summary>One character per line for vertical display; the long-vowel mark turns upright.</summary>
    public static string Vertical(string reading) =>
        string.Join("\n", reading.Select(c => c == 'ー' ? '｜' : c));

    // Prefer whole readings over prefix/suffix forms like "-ぎ" or "お-".
    private static string? Pick(IEnumerable<string> readings)
    {
        var list = readings.Where(r => !string.IsNullOrWhiteSpace(r.Trim('-'))).ToList();
        var whole = list.FirstOrDefault(r => !r.StartsWith('-') && !r.EndsWith('-'));
        return (whole ?? list.FirstOrDefault())?.Trim('-');
    }
}
