using System.IO;
using System.Text.Json;

namespace ScribeVG.Services;

/// <summary>Small user settings kept in <c>%AppData%\ScribeVG\settings.json</c>.</summary>
public sealed class AppSettings
{
    /// <summary>Folder holding Emi Asano's <c>{id}.m4a</c> clips, if the user picked one.</summary>
    public string? AudioFolder { get; set; }

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
