namespace ScribeVG.Services;

/// <summary>What happened while writing one kanji, used to rate it automatically.</summary>
public sealed record KanjiAttempt(
    IReadOnlyList<int> StrokeScores,
    IReadOnlyList<int> TriesPerStroke,
    bool GuideUsed,
    bool AnyReversed)
{
    public double Average => StrokeScores.Count == 0 ? 0 : StrokeScores.Average();
}

public static class PracticeRating
{
    /// <summary>
    /// Again: any stroke failed twice or more, or average below 60.
    /// Easy:  average 90+, every stroke right first try, no guide/peek.
    /// Hard:  average below 75, or the guide was shown/peeked, or a stroke was drawn backwards.
    /// Good:  otherwise (average 75+).
    /// </summary>
    public static Rating Rate(KanjiAttempt attempt)
    {
        var average = attempt.Average;
        if (attempt.TriesPerStroke.Any(t => t - 1 >= 2) || average < StrokeMatcherPass) return Rating.Again;
        var clean = !attempt.GuideUsed && !attempt.AnyReversed;
        if (average >= 90 && attempt.TriesPerStroke.All(t => t == 1) && clean) return Rating.Easy;
        if (average < 75 || !clean) return Rating.Hard;
        return Rating.Good;
    }

    private const int StrokeMatcherPass = 60; // matches StrokeMatcher.PassScore

    public static string Describe(Rating rating) => rating switch
    {
        Rating.Again => "Again",
        Rating.Hard => "Hard",
        Rating.Good => "Good",
        _ => "Easy"
    };
}
