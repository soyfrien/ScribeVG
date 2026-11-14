using System.Text.Json.Serialization;

namespace ScribeVG.Services;

public enum Rating { Again = 1, Hard = 2, Good = 3, Easy = 4 }

public enum CardPhase { New, Learning, Review, Relearning }

/// <summary>Spaced-repetition state for one kanji, saved in progress.json.</summary>
public sealed class CardState
{
    public CardPhase State { get; set; } = CardPhase.New;
    public double Stability { get; set; }
    public double Difficulty { get; set; }
    public DateTimeOffset? Due { get; set; }
    public DateTimeOffset? LastReview { get; set; }
    public int Reps { get; set; }
    public int Lapses { get; set; }
    public Rating? LastRating { get; set; }

    /// <summary>True once the card has graduated to Review at least once (a later lapse doesn't undo that).</summary>
    [JsonIgnore]
    public bool Learned => State is CardPhase.Review or CardPhase.Relearning;
}

/// <summary>
/// FSRS-5 (Free Spaced Repetition Scheduler) with its published default parameters,
/// targeting 90% recall. New and lapsed cards go through short in-session steps
/// (1, 5 or 10 minutes) until rated Good or Easy, then get day-based intervals.
/// See https://github.com/open-spaced-repetition/fsrs4anki/wiki/The-Algorithm
/// </summary>
public sealed class FsrsScheduler
{
    public static readonly double[] DefaultWeights =
    [
        0.40255, 1.18385, 3.173, 15.69105, 7.1949, 0.5345, 1.4604, 0.0046, 1.54575, 0.1192,
        1.01925, 1.9395, 0.11, 0.29605, 2.2698, 0.2315, 2.9898, 0.51655, 0.6621
    ];

    private const double Decay = -0.5;
    private const double Factor = 19.0 / 81.0; // so that R(S, S) = 0.9
    public const int MaximumInterval = 36500;

    private static readonly TimeSpan AgainStep = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan HardStep = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RelearnStep = TimeSpan.FromMinutes(10);

    private readonly double[] _w;

    public FsrsScheduler(double[]? weights = null, double desiredRetention = 0.9)
    {
        _w = weights ?? DefaultWeights;
        DesiredRetention = desiredRetention;
    }

    public double DesiredRetention { get; }

    /// <summary>Apply a review. Returns the updated state (the same instance).</summary>
    public CardState Review(CardState card, Rating rating, DateTimeOffset now)
    {
        var g = (int)rating;
        var elapsedDays = card.LastReview is DateTimeOffset last ? Math.Max(0, (now - last).TotalDays) : 0;

        switch (card.State)
        {
            case CardPhase.New:
                card.Stability = InitialStability(rating);
                card.Difficulty = InitialDifficulty(g);
                if (rating >= Rating.Good) Graduate(card, now);
                else Step(card, CardPhase.Learning, now, rating == Rating.Again ? AgainStep : HardStep);
                break;

            case CardPhase.Learning:
            case CardPhase.Relearning:
                card.Stability = ShortTermStability(card.Stability, g);
                card.Difficulty = NextDifficulty(card.Difficulty, g);
                if (rating >= Rating.Good) Graduate(card, now);
                else Step(card, card.State, now, rating == Rating.Again ? AgainStep : HardStep);
                break;

            case CardPhase.Review:
                var r = Retrievability(elapsedDays, card.Stability);
                var oldDifficulty = card.Difficulty;
                card.Difficulty = NextDifficulty(card.Difficulty, g);
                if (rating == Rating.Again)
                {
                    card.Stability = ForgetStability(oldDifficulty, card.Stability, r);
                    card.Lapses++;
                    Step(card, CardPhase.Relearning, now, RelearnStep);
                }
                else
                {
                    card.Stability = elapsedDays < 1
                        ? ShortTermStability(card.Stability, g)
                        : RecallStability(oldDifficulty, card.Stability, r, rating);
                    Graduate(card, now);
                }
                break;
        }

        card.Reps++;
        card.LastReview = now;
        card.LastRating = rating;
        return card;
    }

    public double Retrievability(double elapsedDays, double stability) =>
        stability <= 0 ? 0 : Math.Pow(1 + Factor * elapsedDays / stability, Decay);

    /// <summary>Days until recall probability drops to the desired retention.</summary>
    public int NextIntervalDays(double stability)
    {
        var days = stability / Factor * (Math.Pow(DesiredRetention, 1 / Decay) - 1);
        return (int)Math.Clamp(Math.Round(days), 1, MaximumInterval);
    }

    private void Graduate(CardState card, DateTimeOffset now)
    {
        card.State = CardPhase.Review;
        card.Due = now.AddDays(NextIntervalDays(card.Stability));
    }

    private static void Step(CardState card, CardPhase phase, DateTimeOffset now, TimeSpan delay)
    {
        card.State = phase;
        card.Due = now + delay;
    }

    private double InitialStability(Rating rating) => Math.Max(_w[(int)rating - 1], 0.1);

    private double InitialDifficulty(int g) => Math.Clamp(_w[4] - Math.Exp(_w[5] * (g - 1)) + 1, 1, 10);

    private double NextDifficulty(double d, int g)
    {
        var delta = -_w[6] * (g - 3);
        var damped = d + delta * (10 - d) / 9;                     // linear damping
        var reverted = _w[7] * InitialDifficulty(4) + (1 - _w[7]) * damped; // mean reversion
        return Math.Clamp(reverted, 1, 10);
    }

    private double ShortTermStability(double s, int g) => Math.Max(s * Math.Exp(_w[17] * (g - 3 + _w[18])), 0.1);

    private double RecallStability(double d, double s, double r, Rating rating)
    {
        var hardPenalty = rating == Rating.Hard ? _w[15] : 1;
        var easyBonus = rating == Rating.Easy ? _w[16] : 1;
        return s * (1 + Math.Exp(_w[8]) * (11 - d) * Math.Pow(s, -_w[9])
                        * (Math.Exp(_w[10] * (1 - r)) - 1) * hardPenalty * easyBonus);
    }

    private double ForgetStability(double d, double s, double r)
    {
        var forget = _w[11] * Math.Pow(d, -_w[12]) * (Math.Pow(s + 1, _w[13]) - 1) * Math.Exp(_w[14] * (1 - r));
        var cap = s / Math.Exp(_w[17] * _w[18]);
        return Math.Max(Math.Min(forget, cap), 0.1);
    }
}
