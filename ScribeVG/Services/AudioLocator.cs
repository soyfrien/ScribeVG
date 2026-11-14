using System.IO;

namespace ScribeVG.Services;

/// <summary>
/// Finds Emi Asano's recording <c>{sentenceId}.m4a</c> from the UmaiKanji project. The clips
/// aren't bundled with ScribeVG; they're read from a local folder (normally Louis's OneDrive).
/// </summary>
public sealed class AudioLocator
{
    /// <summary>Where the clips live inside OneDrive, from the UmaiKanji backups.</summary>
    public static readonly string[] OneDriveSubfolders =
    [
        Path.Combine("Backups", "Umai Kanji Archive", "Umai Kanji Resources", "Audio"),
        Path.Combine("Backups", "Umai Kanji Archive 2", "Umai Kanji Resources", "Audio"),
    ];

    private readonly Func<string, string?> _env;

    public AudioLocator(string? chosenFolder, Func<string, string?>? environment = null)
    {
        _env = environment ?? Environment.GetEnvironmentVariable;
        ChosenFolder = chosenFolder;
        Folder = Resolve(chosenFolder) ?? DefaultCandidates().Select(Resolve).FirstOrDefault(f => f is not null);
    }

    /// <summary>The folder saved in settings (may be missing or wrong).</summary>
    public string? ChosenFolder { get; }

    /// <summary>The folder actually in use, or null if no clips were found anywhere.</summary>
    public string? Folder { get; }

    public bool FromSettings => Folder is not null && ChosenFolder is not null &&
                                string.Equals(Folder, Resolve(ChosenFolder), StringComparison.OrdinalIgnoreCase);

    /// <summary>Path to the clip, or null if there's no folder or the clip isn't in it.</summary>
    public string? Find(int sentenceId)
    {
        if (Folder is null) return null;
        var path = Path.Combine(Folder, $"{sentenceId}.m4a");
        return File.Exists(path) ? path : null;
    }

    /// <summary>OneDrive roots from the environment, then the usual %UserProfile%\OneDrive.</summary>
    public IEnumerable<string> DefaultCandidates()
    {
        var roots = new List<string>();
        foreach (var name in new[] { "OneDriveConsumer", "OneDrive", "OneDriveCommercial" })
        {
            if (_env(name) is { Length: > 0 } root) roots.Add(root);
        }
        if (_env("USERPROFILE") is { Length: > 0 } profile)
        {
            roots.Add(Path.Combine(profile, "OneDrive"));
            roots.Add(Path.Combine(profile, "OneDrive - Personal"));
        }

        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var sub in OneDriveSubfolders) yield return Path.Combine(root, sub);
        }
    }

    /// <summary>
    /// Accepts the Audio folder itself, or a parent such as "Umai Kanji Archive" or
    /// "Umai Kanji Resources", and returns the folder that holds the .m4a files.
    /// </summary>
    public static string? Resolve(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return null;
        foreach (var candidate in new[]
                 {
                     folder,
                     Path.Combine(folder, "Audio"),
                     Path.Combine(folder, "Umai Kanji Resources", "Audio"),
                 })
        {
            if (Directory.Exists(candidate) && HasClips(candidate)) return candidate;
        }
        return null;
    }

    private static bool HasClips(string folder)
    {
        try { return Directory.EnumerateFiles(folder, "*.m4a").Any(); }
        catch (Exception) { return false; }
    }
}
