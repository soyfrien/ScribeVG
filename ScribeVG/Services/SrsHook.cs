namespace ScribeVG.Services;

/// <summary>
/// Placeholder for spaced-repetition integration.
/// The private <c>soyfrien/UmaiKanji</c> repo already implements FSRS v6
/// (<c>UmaiKanji/Services/FsrsScheduler.cs</c>) plus JSON state stores.
/// Wire practice results from <see cref="StrokeMatcher"/> into that scheduler
/// (or a shared class library extracted from it) rather than inventing a second algorithm.
/// </summary>
public interface IPracticeSrs
{
    DateTimeOffset? NextDue(string character);
    void RecordResult(string character, bool passed);
}

/// <remarks>No-op stand-in so the practice window can be wired without a store yet.</remarks>
public sealed class NullPracticeSrs : IPracticeSrs
{
    public DateTimeOffset? NextDue(string character) => null;

    public void RecordResult(string character, bool passed)
    {
        // Intentionally empty until an FSRS (or SM-2) store is attached.
    }
}
