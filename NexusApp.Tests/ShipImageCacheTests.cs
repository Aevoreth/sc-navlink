using System.Net.Http;
using NexusApp.Services;
using Xunit;

namespace NexusApp.Tests;

public class ShipImageCacheTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "navlink-shipimg-" + Path.GetRandomFileName());

    public ShipImageCacheTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Theory]
    [InlineData("https://media.robertsspaceindustries.com/a.jpg", true)]
    [InlineData("https://starcitizen.tools/thumb.jpg", true)]
    [InlineData("https://api.uexcorp.uk/x", true)]
    [InlineData("http://media.robertsspaceindustries.com/a.jpg", false)]
    [InlineData("https://evil.example/a.jpg", false)]
    public void IsAllowedUrl_AllowlistsHttpsHosts(string url, bool allowed) =>
        Assert.Equal(allowed, ShipImageCache.IsAllowedUrl(url));

    [Fact]
    public void ParseWikiThumb_ReadsSource()
    {
        const string body = """
            {"query":{"pages":{"12":{"title":"100i","thumbnail":{"source":"https://starcitizen.tools/a.jpg","width":400}}}}}
            """;
        Assert.Equal("https://starcitizen.tools/a.jpg", ShipImageCache.ParseWikiThumb(body));
    }

    [Fact]
    public void ParseWikiThumb_RejectsOffHost()
    {
        const string body = """
            {"query":{"pages":{"12":{"thumbnail":{"source":"https://evil.example/a.jpg"}}}}}
            """;
        Assert.Null(ShipImageCache.ParseWikiThumb(body));
    }

    [Fact]
    public async Task EnsureLocal_WritesAllowedImage_AndSkipsHttp()
    {
        var jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 };
        var transport = new FakeTransport
        {
            Bytes =
            {
                ["https://media.robertsspaceindustries.com/100i.jpg"] = (jpeg, "image/jpeg"),
            }
        };
        var cache = new ShipImageCache(transport, _dir);

        var path = await cache.EnsureLocalAsync("100i", "https://media.robertsspaceindustries.com/100i.jpg", "100i", CancellationToken.None);
        Assert.NotNull(path);
        Assert.True(File.Exists(path));
        Assert.Equal(jpeg, File.ReadAllBytes(path!));

        var blocked = await cache.EnsureLocalAsync("x", "http://media.robertsspaceindustries.com/x.jpg", "x", CancellationToken.None);
        Assert.Null(blocked);
    }

    [Fact]
    public async Task EnsureLocal_RejectsOversizedPayload()
    {
        var transport = new FakeTransport { ThrowOversize = true };
        var cache = new ShipImageCache(transport, _dir);
        var path = await cache.EnsureLocalAsync("big", "https://media.robertsspaceindustries.com/big.jpg", "big", CancellationToken.None);
        Assert.Null(path);
    }

    [Fact]
    public void WikiTitles_TriesNameThenSlugTail()
    {
        Assert.Equal(
            ["Origin 100i", "orig-100i", "100i"],
            ShipImageCache.WikiTitles("orig-100i", "Origin 100i"));
    }

    [Fact]
    public void ParseWikiExtract_ReadsPlainIntro()
    {
        const string body = """
            {"query":{"pages":{"12":{"title":"100i","extract":"The Origin 100i is a luxury starter ship."}}}}
            """;
        Assert.Equal("The Origin 100i is a luxury starter ship.", ShipImageCache.ParseWikiExtract(body));
    }

    [Fact]
    public void ParseWikiExtract_SkipsMissingAndHtml()
    {
        const string missing = """{"query":{"pages":{"-1":{"title":"Nope","missing":""}}}}""";
        Assert.Null(ShipImageCache.ParseWikiExtract(missing));
        const string html = """{"query":{"pages":{"1":{"extract":"<p>Nope</p>"}}}}""";
        Assert.Null(ShipImageCache.ParseWikiExtract(html));
    }

    [Fact]
    public async Task EnsureBlurb_WritesAllowedExtract()
    {
        var transport = new FakeTransport();
        transport.Strings[ShipImageCache.WikiExtractUrl("Origin 100i")] =
            """{"query":{"pages":{"12":{"title":"100i","extract":"The Origin 100i is a luxury starter ship."}}}}""";
        var cache = new ShipImageCache(transport, _dir);

        var text = await cache.EnsureBlurbAsync("100i", "Origin 100i", CancellationToken.None);
        Assert.Equal("The Origin 100i is a luxury starter ship.", text);
        Assert.Equal(text, cache.LocalBlurbIfPresent("100i"));
    }

    private sealed class FakeTransport : IShipImageTransport
    {
        public Dictionary<string, (byte[] Bytes, string Type)> Bytes { get; } = new();
        public bool ThrowOversize { get; set; }

        public Task<string> GetStringAsync(string url, int maxBytes, CancellationToken ct)
        {
            if (Strings.TryGetValue(url, out var body)) return Task.FromResult(body);
            return Task.FromResult("{}");
        }

        public Dictionary<string, string> Strings { get; } = new();

        public Task<(byte[] Bytes, string ContentType)> GetBytesAsync(string url, int maxBytes, CancellationToken ct)
        {
            if (ThrowOversize) throw new InvalidOperationException("response larger than expected");
            if (!Bytes.TryGetValue(url, out var row)) throw new HttpRequestException("404");
            if (row.Bytes.Length > maxBytes) throw new InvalidOperationException("response larger than expected");
            return Task.FromResult(row);
        }
    }
}
