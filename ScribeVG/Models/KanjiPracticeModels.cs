namespace ScribeVG.Models;

/// <summary>
/// One sampled point from a parsed KanjiVG stroke (SVG units, typically 109×109).
/// </summary>
public readonly record struct StrokePoint(double X, double Y);

/// <summary>
/// Canonical reference stroke produced by <c>Parser</c>.
/// </summary>
public sealed class CanonicalStroke
{
    public int StrokeNumber { get; init; }
    public IReadOnlyList<StrokePoint> Points { get; init; } = [];
    public string? StrokeType { get; init; }
    public string? Element { get; init; }
    public string? OriginalPath { get; init; }
}

/// <summary>
/// User-drawn stroke, captured from the <c>InkCanvas</c> and converted into SVG-space points.
/// </summary>
public sealed class UserStroke
{
    public int StrokeNumber { get; init; }
    public IReadOnlyList<StrokePoint> Points { get; init; } = [];
}

/// <summary>
/// One practice card: a kanji and its ordered canonical strokes.
/// </summary>
public sealed class KanjiCard
{
    public string? Character { get; init; }
    public string? Codepoint { get; init; }
    public string? SourceFile { get; init; }
    public IReadOnlyList<CanonicalStroke> Strokes { get; init; } = [];
}

public sealed class StrokeMatchResult
{
    public bool Passed { get; init; }

    /// <summary>Overall 0–100 score for the stroke.</summary>
    public int Score { get; init; }

    /// <summary>How closely the path follows the reference (0–100).</summary>
    public int ShapeScore { get; init; }

    /// <summary>How close the start and end points are (0–100).</summary>
    public int PlacementScore { get; init; }

    /// <summary>How close the stroke length is to the reference (0–100).</summary>
    public int LengthScore { get; init; }

    /// <summary>True when the stroke matches much better drawn the other way round.</summary>
    public bool Reversed { get; init; }

    public double DirectionScore { get; init; }
    public double StartDistance { get; init; }
    public double EndDistance { get; init; }

    /// <summary>Short label such as "Excellent" or "Try again".</summary>
    public required string Grade { get; init; }

    /// <summary>One actionable tip for the weakest part of the stroke.</summary>
    public required string Tip { get; init; }

    public required string Summary { get; init; }
}
