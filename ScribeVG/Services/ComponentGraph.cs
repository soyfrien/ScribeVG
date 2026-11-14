using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScribeVG.Services;

/// <summary>
/// Which kanji each bundled kanji is built from (<c>Data/components.json</c>, from KanjiVG
/// <c>kvg:element</c> groups via <c>tools/build_kanjivg_bundle.py</c>).
/// </summary>
public sealed class ComponentGraph
{
    private sealed class FileDto
    {
        [JsonPropertyName("kanji")] public Dictionary<string, List<string>> Kanji { get; init; } = [];
    }

    private readonly Dictionary<string, List<string>> _parts;
    private readonly Dictionary<string, List<string>> _usedBy = [];

    public ComponentGraph(Dictionary<string, List<string>> parts)
    {
        _parts = parts;
        foreach (var (kanji, list) in parts)
        {
            foreach (var part in list)
            {
                if (!_usedBy.TryGetValue(part, out var users)) _usedBy[part] = users = [];
                users.Add(kanji);
            }
        }
    }

    public static ComponentGraph? TryLoad(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var stream = File.OpenRead(path);
            var dto = JsonSerializer.Deserialize<FileDto>(stream);
            return dto is null ? null : new ComponentGraph(dto.Kanji);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load components {path}: {ex.Message}");
            return null;
        }
    }

    /// <summary>Every bundled kanji, in codepoint order.</summary>
    public IEnumerable<string> All => _parts.Keys;

    public int Count => _parts.Count;

    public bool Contains(string kanji) => _parts.ContainsKey(kanji);

    public IReadOnlyList<string> PartsOf(string kanji) =>
        _parts.TryGetValue(kanji, out var list) ? list : [];

    public IReadOnlyList<string> UsedBy(string kanji) =>
        _usedBy.TryGetValue(kanji, out var list) ? list : [];
}
