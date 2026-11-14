using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScribeVG.Services;

/// <summary>One kanji's dictionary entry, trimmed from KANJIDIC2.</summary>
public sealed class KanjiInfo
{
    [JsonPropertyName("m")] public List<string> Meanings { get; init; } = [];
    [JsonPropertyName("on")] public List<string> OnReadings { get; init; } = [];
    [JsonPropertyName("kun")] public List<string> KunReadings { get; init; } = [];
    [JsonPropertyName("nanori")] public List<string>? Nanori { get; init; }
    [JsonPropertyName("grade")] public int? Grade { get; init; }
    [JsonPropertyName("jlpt")] public int? OldJlpt { get; init; }
    [JsonPropertyName("freq")] public int? Frequency { get; init; }
    [JsonPropertyName("strokes")] public int? StrokeCount { get; init; }
    [JsonPropertyName("rad")] public int? RadicalNumber { get; init; }

    /// <summary>Kun readings with okurigana in parentheses, e.g. "なが.い" → "なが(い)".</summary>
    public IEnumerable<string> KunDisplay => KunReadings.Select(k =>
    {
        var dot = k.IndexOf('.');
        return dot < 0 ? k : $"{k[..dot]}({k[(dot + 1)..]})";
    });

    public string? GradeDisplay => Grade switch
    {
        >= 1 and <= 6 => $"Grade {Grade}",
        8 => "Jōyō (secondary school)",
        9 or 10 => "Jinmeiyō (used in names)",
        _ => null
    };

    /// <summary>The original 4-level JLPT (1 hardest, 4 easiest) that KANJIDIC2 records.</summary>
    public string? JlptDisplay => OldJlpt is int level ? $"JLPT {level}" : null;
}

/// <summary>One of the 214 classical (Kangxi) radicals.</summary>
public sealed class RadicalInfo
{
    [JsonPropertyName("c")] public string Character { get; init; } = "";
    [JsonPropertyName("name")] public string Name { get; init; } = "";
}

/// <summary>
/// Looks up meanings and readings from <c>Data/kanjidic.json</c>, built from KANJIDIC2
/// (© EDRDG, CC BY-SA 4.0) by <c>tools/build_kanjidic.py</c>.
/// </summary>
public sealed class KanjiDictionary
{
    private sealed class FileDto
    {
        [JsonPropertyName("radicals")] public Dictionary<string, RadicalInfo> Radicals { get; init; } = [];
        [JsonPropertyName("source")] public string? Source { get; init; }
        [JsonPropertyName("version")] public string? Version { get; init; }
        [JsonPropertyName("kanji")] public Dictionary<string, KanjiInfo> Kanji { get; init; } = [];
    }

    private readonly Dictionary<string, KanjiInfo> _entries;
    private readonly Dictionary<string, RadicalInfo> _radicals;

    public string? Source { get; }
    public string? Version { get; }
    public int Count => _entries.Count;

    private KanjiDictionary(FileDto dto)
    {
        _entries = dto.Kanji;
        _radicals = dto.Radicals;
        Source = dto.Source;
        Version = dto.Version;
    }

    public static KanjiDictionary? TryLoad(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var stream = File.OpenRead(path);
            var dto = JsonSerializer.Deserialize<FileDto>(stream);
            return dto is null ? null : new KanjiDictionary(dto);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load dictionary {path}: {ex.Message}");
            return null;
        }
    }

    /// <summary>Classical (Kangxi) radical 1–214.</summary>
    public RadicalInfo? Radical(int? number) =>
        number is int n && _radicals.TryGetValue(n.ToString(), out var info) ? info : null;

    public KanjiInfo? Lookup(string? character) =>
        character is not null && _entries.TryGetValue(character, out var info) ? info : null;
}
