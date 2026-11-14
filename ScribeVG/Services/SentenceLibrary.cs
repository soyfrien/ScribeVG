using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScribeVG.Services;

/// <summary>One example sentence (Tatoeba, CC BY 2.0 FR) with its kana reading and English.</summary>
public sealed class ExampleSentence
{
    [JsonIgnore] public int Id { get; set; }
    [JsonPropertyName("j")] public string Japanese { get; init; } = "";
    [JsonPropertyName("r")] public string? Reading { get; init; }
    [JsonPropertyName("e")] public string? English { get; init; }
    [JsonPropertyName("a")] public int Audio { get; init; }

    /// <summary>Whether Emi Asano's recording <c>{Id}.m4a</c> exists in the UmaiKanji audio set.</summary>
    [JsonIgnore] public bool HasAudio => Audio != 0;
}

/// <summary>
/// Example sentences per kanji from <c>Data/sentences.json</c>, built from Louis's UmaiKanji
/// database by <c>tools/build_sentences.py</c>. Each kanji's list is already ranked best-first
/// (has audio, short, other kanji easy).
/// </summary>
public sealed class SentenceLibrary
{
    private sealed class FileDto
    {
        [JsonPropertyName("source")] public string? Source { get; init; }
        [JsonPropertyName("k")] public Dictionary<string, List<int>> Kanji { get; init; } = [];
        [JsonPropertyName("s")] public Dictionary<string, ExampleSentence> Sentences { get; init; } = [];
    }

    private readonly Dictionary<string, List<int>> _byKanji;
    private readonly Dictionary<int, ExampleSentence> _sentences = [];

    private SentenceLibrary(FileDto dto)
    {
        Source = dto.Source;
        _byKanji = dto.Kanji;
        foreach (var (key, sentence) in dto.Sentences)
        {
            if (!int.TryParse(key, out var id)) continue;
            sentence.Id = id;
            _sentences[id] = sentence;
        }
    }

    public string? Source { get; }
    public int KanjiCount => _byKanji.Count;
    public int SentenceCount => _sentences.Count;

    public static SentenceLibrary? TryLoad(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var stream = File.OpenRead(path);
            var dto = JsonSerializer.Deserialize<FileDto>(stream);
            return dto is null ? null : new SentenceLibrary(dto);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load sentences {path}: {ex.Message}");
            return null;
        }
    }

    /// <summary>Sentences that use <paramref name="kanji"/>, best first.</summary>
    public IReadOnlyList<ExampleSentence> For(string? kanji)
    {
        if (kanji is null || !_byKanji.TryGetValue(kanji, out var ids)) return [];
        return ids.Select(id => _sentences.GetValueOrDefault(id)).OfType<ExampleSentence>().ToList();
    }

    public ExampleSentence? Get(int id) => _sentences.GetValueOrDefault(id);
}
