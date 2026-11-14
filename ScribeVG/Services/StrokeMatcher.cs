using System.Windows.Ink;
using ScribeVG.Models;

namespace ScribeVG.Services;

/// <summary>
/// First-pass directional matcher: start/end proximity plus overall stroke direction
/// against a <see cref="CanonicalStroke"/>. A later pass can add DTW / path-length
/// similarity and hook this into an SRS scheduler (UmaiKanji already has FSRS).
/// </summary>
public sealed class StrokeMatcher
{
    // KanjiVG units (viewBox is typically 109×109).
    private const double StartEndTolerance = 18.0;
    private const double DirectionThreshold = 0.55;

    public StrokeMatchResult Compare(CanonicalStroke canonical, UserStroke user)
    {
        if (canonical.Points.Count < 2 || user.Points.Count < 2)
        {
            return new StrokeMatchResult
            {
                Passed = false,
                Summary = "Need a longer stroke."
            };
        }

        var cStart = canonical.Points[0];
        var cEnd = canonical.Points[^1];
        var uStart = user.Points[0];
        var uEnd = user.Points[^1];

        var startDistance = Distance(cStart, uStart);
        var endDistance = Distance(cEnd, uEnd);
        var direction = Dot(Direction(cStart, cEnd), Direction(uStart, uEnd));

        var passed = startDistance <= StartEndTolerance
                     && endDistance <= StartEndTolerance
                     && direction >= DirectionThreshold;

        var summary = passed
            ? $"Stroke {canonical.StrokeNumber} looks good (direction {direction:0.00})."
            : $"Stroke {canonical.StrokeNumber} didn't match — start {startDistance:0.0}, end {endDistance:0.0}, direction {direction:0.00}.";

        return new StrokeMatchResult
        {
            Passed = passed,
            DirectionScore = direction,
            StartDistance = startDistance,
            EndDistance = endDistance,
            Summary = summary
        };
    }

    public static UserStroke FromInkStroke(Stroke ink, double designToSourceScale)
    {
        // designToSourceScale is DesignSize/SourceSize (10). Convert ink (design px) back to SVG units.
        var points = ink.StylusPoints
            .Select(p => new StrokePoint(p.X / designToSourceScale, p.Y / designToSourceScale))
            .ToList();

        return new UserStroke { Points = points };
    }

    private static double Distance(StrokePoint a, StrokePoint b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static StrokePoint Direction(StrokePoint from, StrokePoint to)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 0.0001)
        {
            return new StrokePoint(0, 0);
        }

        return new StrokePoint(dx / length, dy / length);
    }

    private static double Dot(StrokePoint a, StrokePoint b) => a.X * b.X + a.Y * b.Y;
}
