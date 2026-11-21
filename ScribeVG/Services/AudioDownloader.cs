using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace ScribeVG.Services;

public sealed class AudioManifest
{
    [JsonPropertyName("version")] public int Version { get; init; }
    [JsonPropertyName("generated")] public string? Generated { get; init; }
    [JsonPropertyName("files")] public Dictionary<string, AudioManifestEntry> Files { get; init; } = [];

    public bool TryGet(int sentenceId, out AudioManifestEntry entry)
    {
        if (Files.TryGetValue(sentenceId.ToString(), out var found))
        {
            entry = found;
            return true;
        }
        entry = null!;
        return false;
    }
}

public sealed class AudioManifestEntry
{
    [JsonPropertyName("bytes")] public int Bytes { get; init; }
    [JsonPropertyName("sha256")] public string Sha256 { get; init; } = "";
}

/// <summary>
/// Fetches Emi Asano's <c>{sentenceId}.m4a</c> clips from the Internet and stores them in
/// <see cref="AudioCache"/>. Callers should try a local folder first.
/// </summary>
public sealed class AudioDownloader : IDisposable
{
    public const string DefaultBaseUrl = "https://ppdac.ltd/scribevg-audio";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan DownloadCancelAfter = TimeSpan.FromSeconds(5);

    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly AudioCache _cache;
    private readonly SemaphoreSlim _gate = new(3);
    private readonly SemaphoreSlim _manifestGate = new(1, 1);
    private AudioManifest? _manifest;
    private bool _manifestAttempted;

    public AudioDownloader(string? baseUrl, AudioCache? cache = null, HttpClient? http = null)
    {
        BaseUrl = Normalize(baseUrl) ?? DefaultBaseUrl;
        _cache = cache ?? new AudioCache();
        if (http is null)
        {
            _http = new HttpClient { Timeout = RequestTimeout };
            var version = typeof(AudioDownloader).Assembly.GetName().Version?.ToString(3) ?? "0.2.0";
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("ScribeVG/" + version);
            _ownsHttp = true;
        }
        else
        {
            _http = http;
            _ownsHttp = false;
        }
    }

    public string BaseUrl { get; }
    public AudioCache Cache => _cache;

    public Uri ClipUri(int sentenceId) => new($"{BaseUrl}/{sentenceId}.m4a", UriKind.Absolute);

    /// <summary>A cached or freshly downloaded path, or null if the clip couldn't be retrieved.</summary>
    public async Task<string?> GetAsync(int sentenceId, CancellationToken cancellationToken = default)
    {
        await EnsureManifestAsync(cancellationToken).ConfigureAwait(false);

        int? bytes = null;
        string? hash = null;
        if (_manifest is not null)
        {
            if (!_manifest.TryGet(sentenceId, out var entry)) return _cache.TryGet(sentenceId);
            bytes = entry.Bytes;
            hash = entry.Sha256;
        }

        var cached = _cache.TryGet(sentenceId, bytes, hash);
        if (cached is not null) return cached;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cached = _cache.TryGet(sentenceId, bytes, hash);
            if (cached is not null) return cached;

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(DownloadCancelAfter);
            using var response = await _http.GetAsync(ClipUri(sentenceId), HttpCompletionOption.ResponseHeadersRead, cts.Token)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            await using var stream = await response.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false);
            return await _cache.SaveAtomicAsync(sentenceId, stream, bytes, hash, cts.Token).ConfigureAwait(false);
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task PrefetchAsync(IEnumerable<int> sentenceIds, CancellationToken cancellationToken = default)
    {
        foreach (var id in sentenceIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await GetAsync(id, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task EnsureManifestAsync(CancellationToken cancellationToken)
    {
        if (_manifestAttempted) return;
        await _manifestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_manifestAttempted) return;
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(DownloadCancelAfter);
                _manifest = await _http.GetFromJsonAsync<AudioManifest>($"{BaseUrl}/manifest.json", cts.Token)
                    .ConfigureAwait(false);
                _manifestAttempted = true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                // Timeout, 404, or offline: try clip URLs directly this session.
                _manifest = null;
                _manifestAttempted = true;
            }
        }
        finally
        {
            _manifestGate.Release();
        }
    }

    internal static string? Normalize(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        return url.Trim().TrimEnd('/');
    }

    public void Dispose()
    {
        _gate.Dispose();
        _manifestGate.Dispose();
        if (_ownsHttp) _http.Dispose();
    }
}
