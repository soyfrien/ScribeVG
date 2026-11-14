using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using ScribeVG.Models;

namespace ScribeVG.Services;

/// <summary>
/// Loads JSON written by the main-branch <c>Parser</c> CLI, and also the
/// alternate shape used by open PR #4 (<c>Codepoint</c>/<c>Character</c>/<c>Index</c>).
/// </summary>
public sealed class KanjiStrokeLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public IReadOnlyList<KanjiCard> LoadDirectory(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var files = Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                     || f.EndsWith(".svg", StringComparison.OrdinalIgnoreCase));
        return LoadFiles(files);
    }

    public IReadOnlyList<KanjiCard> LoadFiles(IEnumerable<string> paths)
    {
        var cards = new List<KanjiCard>();
        foreach (var path in paths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var card = LoadFile(path);
                if (card is not null && card.Strokes.Count > 0)
                {
                    cards.Add(card);
                }
            }
            catch (Exception ex)
            {
                // Skip unreadable files so a mixed folder still loads.
                System.Diagnostics.Debug.WriteLine($"Failed to load {path}: {ex.Message}");
            }
        }

        return cards;
    }

    public KanjiCard? LoadFile(string path)
    {
        if (path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
        {
            return KanjiVgSvgReader.Read(path);
        }

        var json = File.ReadAllText(path);
        var dto = JsonSerializer.Deserialize<KanjiFileDto>(json, Options);
        if (dto is null)
        {
            return null;
        }

        var strokes = (dto.Strokes ?? [])
            .Select((stroke, index) => new CanonicalStroke
            {
                StrokeNumber = stroke.StrokeNumber ?? stroke.Index ?? (index + 1),
                Points = (stroke.Points ?? [])
                    .Select(p => new StrokePoint(p.X, p.Y))
                    .ToList(),
                StrokeType = stroke.StrokeType,
                Element = stroke.Element,
                OriginalPath = stroke.OriginalPath
            })
            .Where(s => s.Points.Count > 0)
            .OrderBy(s => s.StrokeNumber)
            .ToList();

        var character = dto.Kanji ?? dto.Character;
        if (string.IsNullOrEmpty(character) && !string.IsNullOrEmpty(dto.Codepoint)
            && int.TryParse(dto.Codepoint, System.Globalization.NumberStyles.HexNumber, null, out var codePoint))
        {
            try { character = char.ConvertFromUtf32(codePoint); }
            catch (ArgumentOutOfRangeException) { /* leave null */ }
        }

        return new KanjiCard
        {
            Character = character,
            Codepoint = dto.Codepoint,
            SourceFile = dto.SourceFile ?? Path.GetFileName(path),
            Strokes = strokes
        };
    }

    private sealed class KanjiFileDto
    {
        public string? Kanji { get; set; }
        public string? Character { get; set; }
        public string? Codepoint { get; set; }
        public string? SourceFile { get; set; }
        public List<StrokeDto>? Strokes { get; set; }
    }

    private sealed class StrokeDto
    {
        public int? StrokeNumber { get; set; }
        public int? Index { get; set; }
        public List<PointDto>? Points { get; set; }
        public string? StrokeType { get; set; }
        public string? Element { get; set; }
        public string? OriginalPath { get; set; }
    }

    private sealed class PointDto
    {
        [JsonPropertyName("X")]
        public double X { get; set; }

        [JsonPropertyName("Y")]
        public double Y { get; set; }
    }
}
