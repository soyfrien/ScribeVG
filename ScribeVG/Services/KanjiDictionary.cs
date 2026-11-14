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

    /// <summary>KANJIDIC2 uses the pre-2010 4-level JLPT; map it to the closest current level.</summary>
    public string? JlptDisplay => OldJlpt switch
    {
        4 => "JLPT N5",
        3 => "JLPT N4",
        2 => "JLPT N3/N2",
        1 => "JLPT N1",
        _ => null
    };
}

/// <summary>
/// Looks up meanings and readings from <c>Data/kanjidic.json</c>, built from KANJIDIC2
/// (© EDRDG, CC BY-SA 4.0) by <c>tools/build_kanjidic.py</c>.
/// </summary>
public sealed class KanjiDictionary
{
    private sealed class FileDto
    {
        [JsonPropertyName("source")] public string? Source { get; init; }
        [JsonPropertyName("version")] public string? Version { get; init; }
        [JsonPropertyName("kanji")] public Dictionary<string, KanjiInfo> Kanji { get; init; } = [];
    }

    private readonly Dictionary<string, KanjiInfo> _entries;

    public string? Source { get; }
    public string? Version { get; }
    public int Count => _entries.Count;

    private KanjiDictionary(FileDto dto)
    {
        _entries = dto.Kanji;
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

    public KanjiInfo? Lookup(string? character) =>
        character is not null && _entries.TryGetValue(character, out var info) ? info : null;
}
