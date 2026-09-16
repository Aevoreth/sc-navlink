using System.Net.Http;
using NexusApp.Services;
using Xunit;

namespace NexusApp.Tests;

public class UexNormalizerTests
{
    [Fact]
    public void ParseAndNormalize_TradePricesFixture_ProducesCatalogRows()
    {
        var body = TradePricesFixture.LoadSampleJson();
        var parsed = MarketParse.ParseTradePriceRows(body, out _);
        Assert.True(parsed.Count > 0);

        var catalog = parsed.Select(UexNormalizer.TradePrice).ToList();
        Assert.Equal(parsed.Count, catalog.Count);
        Assert.All(catalog, row =>
        {
            Assert.True(row.TerminalId > 0);
            Assert.True(row.CommodityId > 0);
            Assert.False(string.IsNullOrWhiteSpace(row.CommodityName));
            Assert.NotEqual(default, row.ObservedUtc);
        });

        var roundTrip = catalog.Select(UexNormalizer.ToMarket).ToList();
        Assert.Equal(parsed[0].TerminalId, roundTrip[0].TerminalId);
        Assert.Equal(parsed[0].Sell, roundTrip[0].Sell);
        Assert.Equal(parsed[0].ModifiedUtc, roundTrip[0].ModifiedUtc);
    }

    [Fact]
    public void ParseAndNormalize_TerminalsFixture_ProducesCatalogRows()
    {
        var body = TerminalsFixture.LoadSampleJson();
        var parsed = MarketParse.ParseTerminals(body, out var skipped);
        Assert.True(parsed.Count > 0);

        var catalog = parsed.Select(UexNormalizer.Terminal).ToList();
        Assert.Equal(parsed.Count, catalog.Count);
        Assert.All(catalog, row =>
        {
            Assert.True(row.Id > 0);
            Assert.False(string.IsNullOrWhiteSpace(row.Name));
        });
    }

    [Fact]
    public void ParseAndNormalize_CommoditiesEnvelope_MapsParentAndFlags()
    {
        const string body = """
            {"status":"ok","data":[
              {"id":10,"name":"Bexalite (Raw)","slug":"bexalite-raw","is_raw":1,"is_refined":0,"id_parent":11},
              {"id":11,"name":"Bexalite","code":"BEXA","is_raw":0,"is_refined":1,"id_parent":0}
            ]}
            """;
        var parsed = MarketParse.ParseCommodities(body, out var skipped);
        Assert.Equal(0, skipped);
        var catalog = parsed.Select(UexNormalizer.Commodity).ToList();
        Assert.Equal(2, catalog.Count);
        Assert.True(catalog[0].IsRaw);
        Assert.Equal(11, catalog[0].ParentId);
        Assert.True(catalog[1].IsRefined);
        Assert.Equal("bexa", catalog[1].Slug);
    }
}

public class UexMarketProviderTests
{
    private sealed class FakeTransport : IMarketDataTransport
    {
        public Dictionary<string, string> Responses { get; } = new();
        public List<string> Requested { get; } = new();

        public Task<string> GetStringAsync(string url, int maxBytes, CancellationToken ct)
        {
            Requested.Add(url);
            if (!Responses.TryGetValue(url, out var body))
                throw new HttpRequestException("404");
            return Task.FromResult(body);
        }
    }

    [Fact]
    public async Task FetchCommodities_NormalizesOkEnvelope()
    {
        var t = new FakeTransport
        {
            Responses =
            {
                [MarketDataService.BaseUrl + "commodities"] =
                    """{"status":"ok","data":[{"id":11,"name":"Bexalite","slug":"bexalite","is_raw":0,"is_refined":1,"id_parent":0}]}"""
            }
        };
        var provider = new UexMarketProvider(t);

        var result = await provider.FetchCommoditiesAsync(CancellationToken.None);

        Assert.True(result.Ok);
        var row = Assert.Single(result.Rows);
        Assert.Equal(11, row.Id);
        Assert.Equal("Bexalite", row.Name);
    }

    [Fact]
    public async Task FetchCommodities_BadShape_DoesNotThrow()
    {
        var t = new FakeTransport
        {
            Responses = { [MarketDataService.BaseUrl + "commodities"] = "<html>502</html>" }
        };
        var provider = new UexMarketProvider(t);

        var result = await provider.FetchCommoditiesAsync(CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Empty(result.Rows);
        Assert.Equal("the response was not in the expected format", result.Error);
    }

    [Fact]
    public async Task FetchCommodities_TransportFailure_ReturnsFailed()
    {
        var provider = new UexMarketProvider(new FakeTransport());

        var result = await provider.FetchCommoditiesAsync(CancellationToken.None);

        Assert.False(result.TransportOk);
        Assert.Empty(result.Rows);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Fact]
    public async Task FetchVehicles_NormalizesOkEnvelope()
    {
        var t = new FakeTransport
        {
            Responses =
            {
                [MarketDataService.BaseUrl + "vehicles"] =
                    """{"status":"ok","data":[{"id":10,"name":"100i","slug":"100i","company_name":"Origin Jumpworks","scu":2,"is_spaceship":1,"is_cargo":1}]}"""
            }
        };
        var provider = new UexMarketProvider(t);
        var result = await provider.FetchVehiclesAsync(CancellationToken.None);
        Assert.True(result.Ok);
        var row = Assert.Single(result.Rows);
        Assert.Equal("100i", row.Id);
        Assert.Equal(2, row.CargoScu);
    }
}
