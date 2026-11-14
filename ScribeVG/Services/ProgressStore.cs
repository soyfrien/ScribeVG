using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScribeVG.Services;

/// <summary>Running average score for one base stroke type (㇐, ㇑, ㇒ …).</summary>
public sealed class StrokeTypeStat
{
    public double Average { get; set; }
    public int Attempts { get; set; }

    public void Add(int score)
    {
        Attempts++;
        Average += (score - Average) / Attempts;
    }
}

/// <summary>Everything ScribeVG remembers between sessions.</summary>
public sealed class ProgressData
{
    public int Version { get; set; } = 1;
    public Dictionary<string, CardState> Cards { get; set; } = [];
    public Dictionary<string, StrokeTypeStat> StrokeTypes { get; set; } = [];

    /// <summary>Local date (yyyy-MM-dd) that <see cref="NewCardsToday"/> counts for.</summary>
    public string? NewCardsDate { get; set; }
    public int NewCardsToday { get; set; }
}

/// <summary>Loads and saves <c>%AppData%\ScribeVG\progress.json</c>.</summary>
public sealed class ProgressStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // keep kanji readable
        Converters = { new JsonStringEnumConverter() }
    };

    public ProgressStore(string path, ProgressData data)
    {
        FilePath = path;
        Data = data;
    }

    public string FilePath { get; }
    public ProgressData Data { get; }

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ScribeVG", "progress.json");

    public static ProgressStore Load(string? path = null)
    {
        path ??= DefaultPath;
        ProgressData? data = null;
        try
        {
            if (File.Exists(path))
            {
                data = JsonSerializer.Deserialize<ProgressData>(File.ReadAllText(path), Options);
            }
        }
        catch (Exception ex)
        {
            // Keep the unreadable file for inspection rather than overwriting it.
            System.Diagnostics.Debug.WriteLine($"Failed to read progress {path}: {ex.Message}");
            try { File.Copy(path, path + ".bad", overwrite: true); } catch { /* best effort */ }
        }

        return new ProgressStore(path, data ?? new ProgressData());
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(Data, Options));
        File.Move(temp, FilePath, overwrite: true);
    }
}
