using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace NexusApp.Services;

internal interface IShipImageTransport
{
    Task<string> GetStringAsync(string url, int maxBytes, CancellationToken ct);
    Task<(byte[] Bytes, string ContentType)> GetBytesAsync(string url, int maxBytes, CancellationToken ct);
}

internal sealed class HttpShipImageTransport : IShipImageTransport
{
    private static readonly HttpClient Http = Create();

    private static HttpClient Create()
    {
        var handler = new SocketsHttpHandler { AllowAutoRedirect = true, MaxAutomaticRedirections = 5 };
        var c = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd($"NexusApp-Ships/{NexusApp.AppInfo.Version}");
        return c;
    }

    public async Task<string> GetStringAsync(string url, int maxBytes, CancellationToken ct)
    {
        var (bytes, _) = await GetBytesAsync(url, maxBytes, ct).ConfigureAwait(false);
        return Encoding.UTF8.GetString(bytes);
    }

    public async Task<(byte[] Bytes, string ContentType)> GetBytesAsync(string url, int maxBytes, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(30));
        using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
        if (resp.RequestMessage?.RequestUri?.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("response did not arrive over https");
        resp.EnsureSuccessStatusCode();
        if (resp.Content.Headers.ContentLength is { } len && len > maxBytes)
            throw new InvalidOperationException("response larger than expected");
        await using var stream = await resp.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false);
        using var ms = new MemoryStream();
        var buf = new byte[81920];
        long total = 0;
        int n;
        while ((n = await stream.ReadAsync(buf, cts.Token).ConfigureAwait(false)) > 0)
        {
            total += n;
            if (total > maxBytes) throw new InvalidOperationException("response larger than expected");
            ms.Write(buf, 0, n);
        }
        var type = resp.Content.Headers.ContentType?.MediaType ?? "";
        return (ms.ToArray(), type);
    }
}

/// <summary>
/// On-demand ship preview and wiki-intro cache. Fetches only while Ships is open.
/// Failures become placeholders (images) or an omitted blurb. Previously cached files still show offline.
/// </summary>
public sealed class ShipImageCache
{
    public const int MaxBytes = 2 * 1024 * 1024;
    public const string WikiApi = "https://starcitizen.tools/api.php";

    private static readonly HashSet<string> ImageTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/jpg", "image/png", "image/webp", "image/gif",
    };

    private readonly IShipImageTransport _transport;
    private readonly string _dir;
    private readonly string _blurbDir;
    private readonly object _gate = new();
    private readonly Dictionary<string, Task<string?>> _inflight = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Task<string?>> _blurbInflight = new(StringComparer.OrdinalIgnoreCase);

    public ShipImageCache() : this(new HttpShipImageTransport(), null) { }

    internal ShipImageCache(IShipImageTransport? transport, string? cacheDir)
    {
        _transport = transport ?? new HttpShipImageTransport();
        _dir = cacheDir ?? Path.Combine(AppPaths.Root, "cache", "ship_images");
        _blurbDir = Path.Combine(_dir, "blurbs");
        Directory.CreateDirectory(_dir);
        Directory.CreateDirectory(_blurbDir);
    }

    public static bool IsAllowedHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host)) return false;
        host = host.Trim().TrimEnd('.').ToLowerInvariant();
        return host == "robertsspaceindustries.com" || host.EndsWith(".robertsspaceindustries.com", StringComparison.Ordinal)
            || host == "uexcorp.space" || host.EndsWith(".uexcorp.space", StringComparison.Ordinal)
            || host == "uexcorp.uk" || host.EndsWith(".uexcorp.uk", StringComparison.Ordinal)
            || host == "starcitizen.tools" || host.EndsWith(".starcitizen.tools", StringComparison.Ordinal);
    }

    public static bool IsAllowedUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        return uri.Scheme == Uri.UriSchemeHttps && IsAllowedHost(uri.Host);
    }

    public string? LocalPathIfPresent(string catalogId)
    {
        var path = PathFor(catalogId);
        return File.Exists(path) ? path : null;
    }

    public Task<string?> EnsureLocalAsync(string catalogId, string? photoUrl, string? displayName, CancellationToken ct)
    {
        var existing = LocalPathIfPresent(catalogId);
        if (existing is not null) return Task.FromResult<string?>(existing);

        lock (_gate)
        {
            if (_inflight.TryGetValue(catalogId, out var pending)) return pending;
            var task = FetchAsync(catalogId, photoUrl, displayName, ct);
            _inflight[catalogId] = task;
            _ = task.ContinueWith(_ =>
            {
                lock (_gate) _inflight.Remove(catalogId);
            }, TaskScheduler.Default);
            return task;
        }
    }

    private async Task<string?> FetchAsync(string catalogId, string? photoUrl, string? displayName, CancellationToken ct)
    {
        try
        {
            if (IsAllowedUrl(photoUrl))
            {
                var saved = await DownloadImageAsync(catalogId, photoUrl!, ct).ConfigureAwait(false);
                if (saved is not null) return saved;
            }

            var wikiUrl = await ResolveWikiThumbAsync(displayName ?? catalogId, ct).ConfigureAwait(false);
            if (wikiUrl is not null)
                return await DownloadImageAsync(catalogId, wikiUrl, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.Error($"[NET] ship image fetch failed ({ex.GetType().Name})");
        }
        return LocalPathIfPresent(catalogId);
    }

    private async Task<string?> DownloadImageAsync(string catalogId, string url, CancellationToken ct)
    {
        if (!IsAllowedUrl(url)) return null;
        var (bytes, type) = await _transport.GetBytesAsync(url, MaxBytes, ct).ConfigureAwait(false);
        if (bytes.Length == 0 || !ImageTypes.Contains(type)) return null;
        var path = PathFor(catalogId);
        await File.WriteAllBytesAsync(path, bytes, ct).ConfigureAwait(false);
        return path;
    }

    internal async Task<string?> ResolveWikiThumbAsync(string title, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;
        var url = WikiApi
            + "?action=query&format=json&prop=pageimages&piprop=thumbnail&pithumbsize=400&redirects=1&origin=*&titles="
            + Uri.EscapeDataString(title);
        if (!IsAllowedUrl(url)) return null;
        var body = await _transport.GetStringAsync(url, MaxBytes, ct).ConfigureAwait(false);
        return ParseWikiThumb(body);
    }

    internal static string? ParseWikiThumb(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("query", out var query)) return null;
            if (!query.TryGetProperty("pages", out var pages) || pages.ValueKind != JsonValueKind.Object)
                return null;
            foreach (var page in pages.EnumerateObject())
            {
                if (!page.Value.TryGetProperty("thumbnail", out var thumb)) continue;
                if (!thumb.TryGetProperty("source", out var source) || source.ValueKind != JsonValueKind.String)
                    continue;
                var src = source.GetString();
                if (IsAllowedUrl(src)) return src;
            }
        }
        catch (JsonException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
        return null;
    }

    public string? LocalBlurbIfPresent(string catalogId)
    {
        var path = BlurbPath(catalogId);
        if (!File.Exists(path)) return null;
        try
        {
            var text = File.ReadAllText(path);
            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }
        catch (IOException)
        {
            return null;
        }
    }

    public Task<string?> EnsureBlurbAsync(string catalogId, string? displayName, CancellationToken ct)
    {
        var existing = LocalBlurbIfPresent(catalogId);
        if (existing is not null) return Task.FromResult<string?>(existing);

        lock (_gate)
        {
            if (_blurbInflight.TryGetValue(catalogId, out var pending)) return pending;
            var task = FetchBlurbAsync(catalogId, displayName, ct);
            _blurbInflight[catalogId] = task;
            _ = task.ContinueWith(_ =>
            {
                lock (_gate) _blurbInflight.Remove(catalogId);
            }, TaskScheduler.Default);
            return task;
        }
    }

    /// <summary>Wiki page titles to try, display name first, then id, then the slug tail.</summary>
    public static IReadOnlyList<string> WikiTitles(string? catalogId, string? displayName)
    {
        var list = new List<string>();
        AddTitle(list, displayName);
        AddTitle(list, catalogId);
        if (!string.IsNullOrWhiteSpace(catalogId))
        {
            var i = catalogId.LastIndexOf('-');
            if (i > 0 && i < catalogId.Length - 1)
                AddTitle(list, catalogId[(i + 1)..]);
        }
        return list;
    }

    internal static string WikiExtractUrl(string title) =>
        WikiApi
        + "?action=query&format=json&prop=extracts&exintro=1&explaintext=1&exchars=800&redirects=1&origin=*&titles="
        + Uri.EscapeDataString(title);

    internal static string? ParseWikiExtract(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("query", out var query)) return null;
            if (!query.TryGetProperty("pages", out var pages) || pages.ValueKind != JsonValueKind.Object)
                return null;
            foreach (var page in pages.EnumerateObject())
            {
                if (page.Value.TryGetProperty("missing", out _)) continue;
                if (!page.Value.TryGetProperty("extract", out var extract) || extract.ValueKind != JsonValueKind.String)
                    continue;
                var text = NormalizeBlurb(extract.GetString());
                if (text is not null) return text;
            }
        }
        catch (JsonException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
        return null;
    }

    internal static string? NormalizeBlurb(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var text = raw.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        if (text.StartsWith("REDIRECT ", StringComparison.OrdinalIgnoreCase)) return null;
        if (text.Contains('<', StringComparison.Ordinal)) return null;
        if (text.Length > 1200) text = text[..1200].TrimEnd();
        return text.Length == 0 ? null : text;
    }

    private async Task<string?> FetchBlurbAsync(string catalogId, string? displayName, CancellationToken ct)
    {
        try
        {
            foreach (var title in WikiTitles(catalogId, displayName))
            {
                var url = WikiExtractUrl(title);
                if (!IsAllowedUrl(url)) continue;
                var body = await _transport.GetStringAsync(url, MaxBytes, ct).ConfigureAwait(false);
                var text = ParseWikiExtract(body);
                if (text is null) continue;
                await File.WriteAllTextAsync(BlurbPath(catalogId), text, ct).ConfigureAwait(false);
                return text;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.Error($"[NET] ship blurb fetch failed ({ex.GetType().Name})");
        }
        return LocalBlurbIfPresent(catalogId);
    }

    private static void AddTitle(List<string> list, string? value)
    {
        var s = (value ?? "").Trim();
        if (s.Length == 0) return;
        foreach (var existing in list)
        {
            if (string.Equals(existing, s, StringComparison.OrdinalIgnoreCase)) return;
        }
        list.Add(s);
    }

    private string PathFor(string catalogId)
    {
        var safe = SafeId(catalogId);
        return Path.Combine(_dir, safe + ".img");
    }

    private string BlurbPath(string catalogId)
    {
        var safe = SafeId(catalogId);
        return Path.Combine(_blurbDir, safe + ".txt");
    }

    private static string SafeId(string catalogId)
    {
        var safe = new StringBuilder(catalogId.Length);
        foreach (var c in catalogId)
            safe.Append(char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_');
        if (safe.Length == 0) safe.Append("ship");
        return safe.ToString();
    }
}
