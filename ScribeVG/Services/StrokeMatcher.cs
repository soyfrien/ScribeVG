using System.Windows.Ink;
using ScribeVG.Models;

namespace ScribeVG.Services;

/// <summary>
/// Scores a user stroke against a KanjiVG reference stroke on a 0–100 scale.
///
/// Both strokes are resampled to the same number of evenly spaced points, then:
///  • Shape (50%)     – mean distance between matching points along the path.
///  • Placement (30%) – how close the start and end points are.
///  • Length (20%)    – user length vs. reference length.
/// A stroke that fits much better reversed is flagged as drawn backwards and fails.
/// All distances are in KanjiVG units (the viewBox is 109×109).
/// </summary>
public sealed class StrokeMatcher
{
    private const int SampleCount = 32;

    // Distance (KanjiVG units) at which a component's score drops to zero.
    private const double ShapeFalloff = 18.0;
    private const double PlacementFalloff = 24.0;

    public const int PassScore = 60;

    public StrokeMatchResult Compare(CanonicalStroke canonical, UserStroke user)
    {
        if (canonical.Points.Count < 2 || user.Points.Count < 2 || PathLength(user.Points) < 2.0)
        {
            return new StrokeMatchResult
            {
                Passed = false,
                Score = 0,
                Grade = "Try again",
                Tip = "That was more of a tap. Draw the whole stroke in one motion.",
                Summary = $"Stroke {canonical.StrokeNumber}: too short to score."
            };
        }

        var reference = Resample(canonical.Points, SampleCount);
        var drawn = Resample(user.Points, SampleCount);

        var forward = MeanDistance(reference, drawn);
        var backward = MeanDistance(reference, drawn.AsEnumerable().Reverse().ToList());
        var reversed = backward < forward * 0.6 && Distance(reference[0], reference[^1]) > 8.0;

        var startDistance = Distance(reference[0], drawn[0]);
        var endDistance = Distance(reference[^1], drawn[^1]);

        var shape = Falloff(forward, ShapeFalloff);
        var placement = (Falloff(startDistance, PlacementFalloff) + Falloff(endDistance, PlacementFalloff)) / 2.0;

        var lengthRatio = PathLength(user.Points) / Math.Max(PathLength(canonical.Points), 0.001);
        var length = Math.Clamp(1.0 - Math.Abs(Math.Log(lengthRatio)) / Math.Log(2.0), 0.0, 1.0);

        var score = (int)Math.Round(100 * (0.5 * shape + 0.3 * placement + 0.2 * length));
        if (reversed)
        {
            score = Math.Min(score, 40);
        }

        var passed = !reversed && score >= PassScore;
        var grade = GradeFor(score);
        var tip = TipFor(reversed, shape, placement, startDistance, endDistance, lengthRatio, score);

        var direction = Dot(Direction(reference[0], reference[^1]), Direction(drawn[0], drawn[^1]));

        return new StrokeMatchResult
        {
            Passed = passed,
            Score = score,
            ShapeScore = (int)Math.Round(100 * shape),
            PlacementScore = (int)Math.Round(100 * placement),
            LengthScore = (int)Math.Round(100 * length),
            Reversed = reversed,
            DirectionScore = direction,
            StartDistance = startDistance,
            EndDistance = endDistance,
            Grade = grade,
            Tip = tip,
            Summary = $"Stroke {canonical.StrokeNumber}: {score} ({grade})"
        };
    }

    public static string GradeFor(int score) => score switch
    {
        >= 90 => "Excellent",
        >= 75 => "Good",
        >= PassScore => "OK",
        _ => "Try again"
    };

    private static string TipFor(bool reversed, double shape, double placement,
        double startDistance, double endDistance, double lengthRatio, int score)
    {
        if (reversed) return "Wrong direction. Start from the other end, at the dot.";
        if (score >= 90) return "Clean stroke!";

        var lengthScore = Math.Clamp(1.0 - Math.Abs(Math.Log(lengthRatio)) / Math.Log(2.0), 0.0, 1.0);
        var weakest = Math.Min(shape, Math.Min(placement, lengthScore));

        if (weakest == placement)
        {
            return startDistance >= endDistance
                ? "Start closer to the dot at the beginning of the stroke."
                : "Finish closer to where the stroke ends.";
        }

        if (weakest == lengthScore)
        {
            return lengthRatio < 1 ? "A bit short. Carry the stroke further." : "A bit long. Stop sooner.";
        }

        return "Follow the curve of the guide more closely.";
    }

    public static UserStroke FromInkStroke(Stroke ink, double designToSourceScale)
    {
        // designToSourceScale is DesignSize/SourceSize (10). Convert ink (design px) back to SVG units.
        var points = ink.StylusPoints
            .Select(p => new StrokePoint(p.X / designToSourceScale, p.Y / designToSourceScale))
            .ToList();

        return new UserStroke { Points = points };
    }

    /// <summary>Resample a polyline to <paramref name="count"/> points spaced evenly by arc length.</summary>
    public static List<StrokePoint> Resample(IReadOnlyList<StrokePoint> points, int count)
    {
        var total = PathLength(points);
        var result = new List<StrokePoint>(count) { points[0] };
        if (total <= 0.0001)
        {
            while (result.Count < count) result.Add(points[0]);
            return result;
        }

        var interval = total / (count - 1);
        var accumulated = 0.0;
        var previous = points[0];
        var index = 1;

        while (result.Count < count - 1 && index < points.Count)
        {
            var next = points[index];
            var segment = Distance(previous, next);
            if (accumulated + segment >= interval && segment > 0)
            {
                var t = (interval - accumulated) / segment;
                var point = new StrokePoint(previous.X + t * (next.X - previous.X), previous.Y + t * (next.Y - previous.Y));
                result.Add(point);
                previous = point;
                accumulated = 0;
            }
            else
            {
                accumulated += segment;
                previous = next;
                index++;
            }
        }

        while (result.Count < count) result.Add(points[^1]);
        result[^1] = points[^1];
        return result;
    }

    private static double MeanDistance(IReadOnlyList<StrokePoint> a, IReadOnlyList<StrokePoint> b)
    {
        var sum = 0.0;
        for (int i = 0; i < a.Count; i++) sum += Distance(a[i], b[i]);
        return sum / a.Count;
    }

    private static double PathLength(IReadOnlyList<StrokePoint> points)
    {
        var length = 0.0;
        for (int i = 1; i < points.Count; i++) length += Distance(points[i - 1], points[i]);
        return length;
    }

    private static double Falloff(double distance, double zeroAt) => Math.Clamp(1.0 - distance / zeroAt, 0.0, 1.0);

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
        return length < 0.0001 ? new StrokePoint(0, 0) : new StrokePoint(dx / length, dy / length);
    }

    private static double Dot(StrokePoint a, StrokePoint b) => a.X * b.X + a.Y * b.Y;
}
