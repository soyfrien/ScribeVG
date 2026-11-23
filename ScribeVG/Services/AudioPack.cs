using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ScribeVG.Services;

/// <summary>
/// Emi Asano's sentence clips bundled with the app in <c>Data\audio.svpak</c>
/// (built by <c>tools/build_audio_pack.py</c>). Clips are unscrambled into
/// <see cref="AudioCache"/> on first play, because MediaPlayer needs a file.
/// </summary>
public sealed class AudioPack
{
    private const int HeaderBytes = 12;
    private const int EntryBytes = 16;
    private readonly Dictionary<int, (long Offset, int Length)> _index;

    private AudioPack(string path, Dictionary<int, (long, int)> index)
    {
        PackPath = path;
        _index = index;
    }

    public string PackPath { get; }
    public int Count => _index.Count;
    public bool Contains(int sentenceId) => _index.ContainsKey(sentenceId);

    public static string DefaultPath => Path.Combine(AppContext.BaseDirectory, "Data", "audio.svpak");

    /// <summary>The bundled pack, or null if it's missing or unreadable (e.g. a dev build without it).</summary>
    public static AudioPack? TryOpen(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            if (!File.Exists(path)) return null;
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            Span<byte> header = stackalloc byte[HeaderBytes];
            fs.ReadExactly(header);
            if (!header[..4].SequenceEqual("SVPK"u8)) return null;
            if (BinaryPrimitives.ReadUInt32LittleEndian(header[4..]) != 1) return null;
            var count = (int)BinaryPrimitives.ReadUInt32LittleEndian(header[8..]);
            if (count < 0 || count > 1_000_000) return null;

            var raw = new byte[count * EntryBytes];
            fs.ReadExactly(raw);
            var index = new Dictionary<int, (long, int)>(count);
            for (var i = 0; i < count; i++)
            {
                var e = raw.AsSpan(i * EntryBytes, EntryBytes);
                var id = BinaryPrimitives.ReadInt32LittleEndian(e);
                var offset = BinaryPrimitives.ReadInt64LittleEndian(e[4..]);
                var length = BinaryPrimitives.ReadInt32LittleEndian(e[12..]);
                if (offset < HeaderBytes || length <= 0 || offset + length > fs.Length) return null;
                index[id] = (offset, length);
            }
            return new AudioPack(path, index);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>The clip's original bytes, or null if it isn't in the pack.</summary>
    public byte[]? Read(int sentenceId)
    {
        if (!_index.TryGetValue(sentenceId, out var entry)) return null;
        var data = new byte[entry.Length];
        using (var fs = new FileStream(PackPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            fs.Seek(entry.Offset, SeekOrigin.Begin);
            fs.ReadExactly(data);
        }
        Unscramble(data, sentenceId);
        return data;
    }

    /// <summary>A playable file path: the cached copy, or the clip unpacked into the cache.</summary>
    public async Task<string?> GetAsync(int sentenceId, AudioCache cache, CancellationToken cancellationToken = default)
    {
        if (!_index.TryGetValue(sentenceId, out var entry)) return null;
        var cached = cache.TryGet(sentenceId, entry.Length);
        if (cached is not null) return cached;
        try
        {
            var data = await Task.Run(() => Read(sentenceId), cancellationToken).ConfigureAwait(false);
            if (data is null) return null;
            using var ms = new MemoryStream(data, writable: false);
            return await cache.SaveAtomicAsync(sentenceId, ms, entry.Length, null, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }
    }

    internal static void Unscramble(Span<byte> data, int sentenceId)
    {
        Span<byte> key = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes($"ScribeVG audio pack v1:{sentenceId}"), key);
        for (var i = 0; i < data.Length; i++) data[i] ^= key[i & 31];
    }
}
