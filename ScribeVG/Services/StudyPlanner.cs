namespace ScribeVG.Services;

/// <summary>A kanji that became available, and the parts that unlocked it.</summary>
public sealed record UnlockedKanji(string Kanji, IReadOnlyList<string> Parts);

public sealed record ReviewOutcome(Rating Rating, CardState Card, bool WasNew, IReadOnlyList<UnlockedKanji> Unlocked);

/// <summary>
/// Decides what to study next. Due reviews come first (most overdue first), then new kanji.
/// A new kanji is only offered once every part it's built from has been learned
/// (rated Good or Easy at least once, i.e. reached Review), and at most
/// <see cref="NewCardsPerDay"/> new kanji are introduced per day.
/// New kanji are ordered by old JLPT level (4, the easiest, first), then school grade,
/// then frequency, then stroke count. A part that has no level of its own (亻, 宀, 吾 …)
/// borrows the place of the earliest kanji that needs it, so it shows up just before.
/// </summary>
public sealed class StudyPlanner
{
    public const int NewCardsPerDay = 10;

    /// <summary>When nothing else is left, a learning step due this soon may be shown early.</summary>
    public static readonly TimeSpan LearnAhead = TimeSpan.FromMinutes(20);

    private readonly ComponentGraph _graph;
    private readonly Func<string, KanjiInfo?> _lookup;
    private readonly FsrsScheduler _scheduler;
    private readonly Dictionary<string, SortKey> _keys = [];

    public StudyPlanner(ComponentGraph graph, Func<string, KanjiInfo?> lookup, ProgressData progress,
        FsrsScheduler? scheduler = null)
    {
        _graph = graph;
        _lookup = lookup;
        Progress = progress;
        _scheduler = scheduler ?? new FsrsScheduler();
    }

    public ProgressData Progress { get; }

    public CardState? StateOf(string kanji) => Progress.Cards.GetValueOrDefault(kanji);

    public bool IsLearned(string kanji) => StateOf(kanji)?.Learned == true;

    public bool IsNew(string kanji) => StateOf(kanji) is null or { State: CardPhase.New };

    public bool IsUnlocked(string kanji) => _graph.PartsOf(kanji).All(IsLearned);

    public bool IsStudyCard(string kanji) => _graph.Contains(kanji);

    public int NewIntroducedToday(DateTimeOffset now) =>
        Progress.NewCardsDate == DayOf(now) ? Progress.NewCardsToday : 0;

    public int NewQuotaLeft(DateTimeOffset now) => Math.Max(0, NewCardsPerDay - NewIntroducedToday(now));

    /// <summary>Started kanji whose review time has come, most overdue first.</summary>
    public List<string> DueCards(DateTimeOffset now) => Progress.Cards
        .Where(kv => kv.Value.State != CardPhase.New && kv.Value.Due <= now && _graph.Contains(kv.Key))
        .OrderBy(kv => kv.Value.Due)
        .Select(kv => kv.Key)
        .ToList();

    /// <summary>Unlocked, never-studied kanji in study order.</summary>
    public IEnumerable<string> NewCandidates() => _graph.All
        .Where(k => IsNew(k) && IsUnlocked(k))
        .OrderBy(KeyOf)
        .ThenBy(k => char.ConvertToUtf32(k, 0));

    /// <summary>Up to <paramref name="count"/> kanji in the order they'll be offered.</summary>
    public List<string> Queue(DateTimeOffset now, int count = 20)
    {
        var queue = DueCards(now);
        queue.AddRange(NewCandidates().Take(NewQuotaLeft(now)));
        return queue.Take(count).ToList();
    }

    /// <summary>The next kanji to study, or null when today's work is done.</summary>
    public string? Next(DateTimeOffset now, string? avoid = null)
    {
        var due = DueCards(now);
        var pick = due.FirstOrDefault(k => k != avoid);
        if (pick is not null) return pick;

        if (NewQuotaLeft(now) > 0)
        {
            pick = NewCandidates().FirstOrDefault(k => k != avoid);
            if (pick is not null) return pick;
        }

        if (due.Count > 0) return due[0]; // only the avoided card is due; show it again

        // Nothing due: pull a learning step forward rather than leave a short wait.
        return Progress.Cards
            .Where(kv => kv.Value.State is CardPhase.Learning or CardPhase.Relearning
                         && kv.Value.Due <= now + LearnAhead && kv.Key != avoid && _graph.Contains(kv.Key))
            .OrderBy(kv => kv.Value.Due)
            .Select(kv => kv.Key)
            .FirstOrDefault();
    }

    /// <summary>Schedule a finished kanji and report anything it unlocked.</summary>
    public ReviewOutcome Record(string kanji, Rating rating, DateTimeOffset now)
    {
        if (!Progress.Cards.TryGetValue(kanji, out var card))
        {
            Progress.Cards[kanji] = card = new CardState();
        }

        var wasNew = card.State == CardPhase.New;
        var users = _graph.UsedBy(kanji).Where(IsNew).ToList();
        var lockedBefore = users.Where(u => !IsUnlocked(u)).ToHashSet();

        _scheduler.Review(card, rating, now);

        if (wasNew)
        {
            var day = DayOf(now);
            if (Progress.NewCardsDate != day)
            {
                Progress.NewCardsDate = day;
                Progress.NewCardsToday = 0;
            }
            Progress.NewCardsToday++;
        }

        var unlocked = lockedBefore
            .Where(IsUnlocked)
            .OrderBy(KeyOf)
            .Select(u => new UnlockedKanji(u, _graph.PartsOf(u)))
            .ToList();
        return new ReviewOutcome(rating, card, wasNew, unlocked);
    }

    /// <summary>Add one stroke attempt's score to its stroke type's running average.</summary>
    public void RecordStroke(string? kvgType, int score)
    {
        var type = StrokeTypes.Normalize(kvgType);
        if (type is null) return;
        if (!Progress.StrokeTypes.TryGetValue(type, out var stat))
        {
            Progress.StrokeTypes[type] = stat = new StrokeTypeStat();
        }
        stat.Add(score);
    }

    /// <summary>The lowest-scoring stroke types that have at least <paramref name="minAttempts"/> tries.</summary>
    public List<KeyValuePair<string, StrokeTypeStat>> WeakestStrokeTypes(int count = 3, int minAttempts = 5) =>
        Progress.StrokeTypes
            .Where(kv => kv.Value.Attempts >= minAttempts)
            .OrderBy(kv => kv.Value.Average)
            .Take(count)
            .ToList();

    private static string DayOf(DateTimeOffset now) => now.ToLocalTime().ToString("yyyy-MM-dd");

    private readonly record struct SortKey(int Jlpt, int Grade, int Frequency, int Strokes) : IComparable<SortKey>
    {
        public int CompareTo(SortKey other)
        {
            var c = Jlpt.CompareTo(other.Jlpt);
            if (c == 0) c = Grade.CompareTo(other.Grade);
            if (c == 0) c = Frequency.CompareTo(other.Frequency);
            if (c == 0) c = Strokes.CompareTo(other.Strokes);
            return c;
        }

        public static SortKey Min(SortKey a, SortKey b) => a.CompareTo(b) <= 0 ? a : b;
    }

    private SortKey OwnKey(string kanji)
    {
        var info = _lookup(kanji);
        return new SortKey(
            info?.OldJlpt is int j ? 4 - j : 9,   // old JLPT 4 is the easiest level
            info?.Grade ?? 99,
            info?.Frequency ?? 99999,
            info?.StrokeCount ?? 99);
    }

    private SortKey KeyOf(string kanji) => KeyOf(kanji, 0);

    private SortKey KeyOf(string kanji, int depth)
    {
        if (_keys.TryGetValue(kanji, out var cached)) return cached;
        var key = OwnKey(kanji);
        if (depth < 32) // the graph is acyclic; the cap is just a guard
        {
            foreach (var user in _graph.UsedBy(kanji))
            {
                key = SortKey.Min(key, KeyOf(user, depth + 1));
            }
        }
        return _keys[kanji] = key;
    }
}
