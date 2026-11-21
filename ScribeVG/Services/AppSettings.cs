using System.IO;
using System.Text.Json;

namespace ScribeVG.Services;

/// <summary>Small user settings kept in <c>%AppData%\ScribeVG\settings.json</c>.</summary>
public sealed class AppSettings
{
    /// <summary>Folder holding Emi Asano's <c>{id}.m4a</c> clips, if the user picked one.</summary>
    public string? AudioFolder { get; set; }

    /// <summary>
    /// Base URL for clips hosted on the Internet. Null or blank means
    /// <see cref="DefaultAudioBaseUrl"/>.
    /// </summary>
    public string? AudioBaseUrl { get; set; }

    public const string DefaultAudioBaseUrl = AudioDownloader.DefaultBaseUrl;

    /// <summary>The base URL actually used (no trailing slash).</summary>
    public string EffectiveAudioBaseUrl =>
        AudioDownloader.Normalize(AudioBaseUrl) ?? DefaultAudioBaseUrl;

    /// <summary>Move to the next card automatically after finishing a kanji with a high enough average.</summary>
    public bool AutoAdvance { get; set; } = true;

    /// <summary>Minimum kanji average (0-100) that triggers auto-advance.</summary>
    public int AutoAdvanceScore { get; set; } = 75;

    /// <summary>Skip the shake and particle bursts; keep only gentle color flashes.</summary>
    public bool ReduceMotion { get; set; }

    /// <summary>Write each kanji 6 times like a practice-notebook row: model, fading model, then from memory.</summary>
    public bool SheetMode { get; set; } = true;

    /// <summary>Hide meanings, readings and sentence translations while writing so they have to be recalled.</summary>
    public bool HideMeanings { get; set; } = true;

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ScribeVG", "settings.json");

    public static AppSettings Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            if (File.Exists(path))
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Options) ?? new AppSettings();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to read settings {path}: {ex.Message}");
        }
        return new AppSettings();
    }

    public void Save(string? path = null)
    {
        path ??= DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, Options));
        File.Move(temp, path, overwrite: true);
    }
}
