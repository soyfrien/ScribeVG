using System.IO;
using System.Security.Cryptography;

namespace ScribeVG.Services;

/// <summary>
/// Sentence clips unpacked from <see cref="AudioPack"/>, kept in <c>%LocalAppData%\ScribeVG\audio-cache</c>
/// so MediaPlayer can open them as files. They survive updates and stay out of roaming AppData.
/// </summary>
public sealed class AudioCache
{
    public const int MinBytes = 1024;

    public static string DefaultFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ScribeVG", "audio-cache");

    public AudioCache(string? folder = null)
    {
        Folder = folder ?? DefaultFolder;
    }

    public string Folder { get; }

    public string PathFor(int sentenceId) => Path.Combine(Folder, $"{sentenceId}.m4a");

    /// <summary>The cached clip if it exists and matches the optional manifest checks.</summary>
    public string? TryGet(int sentenceId, int? expectedBytes = null, string? expectedSha256 = null)
    {
        var path = PathFor(sentenceId);
        if (!File.Exists(path)) return null;
        try
        {
            if (!IsValid(path, expectedBytes, expectedSha256))
            {
                TryDelete(path);
                return null;
            }
            return path;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Write to a temp file, validate, then rename into place. Deletes the temp file on failure.</summary>
    public async Task<string> SaveAtomicAsync(
        int sentenceId, Stream data, int? expectedBytes = null, string? expectedSha256 = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Folder);
        var final = PathFor(sentenceId);
        var temp = final + ".tmp";
        try
        {
            await using (var fs = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 81_920, useAsync: true))
            {
                await data.CopyToAsync(fs, cancellationToken).ConfigureAwait(false);
                await fs.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            if (!IsValid(temp, expectedBytes, expectedSha256))
            {
                throw new InvalidDataException("Clip did not match the expected size or checksum.");
            }
            File.Move(temp, final, overwrite: true);
            return final;
        }
        finally
        {
            TryDelete(temp);
        }
    }

    public void Clear()
    {
        if (!Directory.Exists(Folder)) return;
        Directory.Delete(Folder, recursive: true);
    }

    public int CachedCount()
    {
        try
        {
            return Directory.Exists(Folder) ? Directory.EnumerateFiles(Folder, "*.m4a").Count() : 0;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    internal static bool IsValid(string path, int? expectedBytes, string? expectedSha256)
    {
        var length = new FileInfo(path).Length;
        if (length < MinBytes) return false;
        if (expectedBytes is int n && length != n) return false;
        if (expectedSha256 is { Length: > 0 })
        {
            var actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
            if (!actual.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }

    internal static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception)
        {
            // Still locked by MediaPlayer, or already gone.
        }
    }
}
