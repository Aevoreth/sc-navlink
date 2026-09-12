using System.Diagnostics;
using System.Text;

namespace NexusApp.Services;

/// <summary>
/// UEX API 2.0 client. GET https://api.uexcorp.uk/2.0/{resource}/
/// Envelope: { "status": "ok", "data": ... }. Docs: https://uexcorp.space/api
/// Returns normalized catalog rows. Does not persist and does not own cadence.
/// </summary>
internal sealed class UexMarketProvider : IMarketDataProvider
{
    public const string BaseUrl = MarketDataService.BaseUrl;

    private const string GameVersionsEndpoint = "game_versions";
    private const string CommoditiesEndpoint = "commodities";
    private const string RefinedPricesEndpoint = "commodities_prices";
    private const string YieldsEndpoint = "refineries_yields";
    private const string TerminalsEndpoint = "terminals";
    private const string CommoditiesPricesAllEndpoint = "commodities_prices_all";

    private readonly IMarketDataTransport _transport;

    public UexMarketProvider(IMarketDataTransport transport)
    {
        _transport = transport;
    }

    public async Task<(string? Live, string? Error, long ElapsedMs, int ByteCount)> FetchLiveGameVersionAsync(CancellationToken ct)
    {
        var (body, error, ms, bytes) = await GetAsync(BaseUrl + GameVersionsEndpoint, ct).ConfigureAwait(false);
        if (body is null) return (null, error, ms, bytes);
        var live = MarketParse.ParseLiveGameVersion(body);
        return live is null
            ? (null, "the response was not in the expected format", ms, bytes)
            : (live, null, ms, bytes);
    }

    public Task<ProviderFetch<CatalogCommodity>> FetchCommoditiesAsync(CancellationToken ct) =>
        FetchArrayAsync(
            BaseUrl + CommoditiesEndpoint,
            (body, out skipped) => MarketParse.ParseCommodities(body, out skipped).Select(UexNormalizer.Commodity).ToList(),
            ct);

    public Task<ProviderFetch<CatalogTerminal>> FetchTerminalsAsync(CancellationToken ct) =>
        FetchArrayAsync(
            BaseUrl + TerminalsEndpoint,
            (body, out skipped) => MarketParse.ParseTerminals(body, out skipped).Select(UexNormalizer.Terminal).ToList(),
            ct);

    public Task<ProviderFetch<CatalogTradePrice>> FetchTradePricesAsync(CancellationToken ct) =>
        FetchArrayAsync(
            BaseUrl + CommoditiesPricesAllEndpoint,
            (body, out skipped) => MarketParse.ParseTradePriceRows(body, out skipped).Select(UexNormalizer.TradePrice).ToList(),
            ct);

    public Task<ProviderFetch<CatalogYield>> FetchYieldsAsync(CancellationToken ct) =>
        FetchArrayAsync(
            BaseUrl + YieldsEndpoint,
            (body, out skipped) => MarketParse.ParseYieldRows(body, out skipped).Select(UexNormalizer.Yield).ToList(),
            ct);

    public Task<ProviderFetch<CatalogRefinedPrice>> FetchRefinedPricesAsync(int commodityId, CancellationToken ct) =>
        FetchArrayAsync(
            $"{BaseUrl}{RefinedPricesEndpoint}?id_commodity={commodityId}",
            (body, out skipped) => MarketParse.ParsePriceRows(body, out skipped).Select(UexNormalizer.RefinedPrice).ToList(),
            ct);

    private delegate List<T> ParseRows<T>(string body, out int skipped);

    private async Task<ProviderFetch<T>> FetchArrayAsync<T>(string url, ParseRows<T> parse, CancellationToken ct)
    {
        var (body, error, ms, bytes) = await GetAsync(url, ct).ConfigureAwait(false);
        if (body is null) return ProviderFetch<T>.Failed(error ?? "unknown error", ms);

        if (!MarketParse.TryGetData(body, out var data) || data.ValueKind != System.Text.Json.JsonValueKind.Array)
            return ProviderFetch<T>.BadShape(ms, bytes);

        var rows = parse(body, out var skipped);
        return ProviderFetch<T>.Parsed(rows, skipped, ms, bytes);
    }

    private async Task<(string? Body, string? Error, long ElapsedMs, int ByteCount)> GetAsync(
        string url, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var body = await _transport.GetStringAsync(url, MarketDataService.MaxResponseBytes, ct).ConfigureAwait(false);
            return (body, null, sw.ElapsedMilliseconds, Encoding.UTF8.GetByteCount(body));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var reason = ex is OperationCanceledException ? "the request timed out" : Shorten(ex.Message);
            return (null, reason, sw.ElapsedMilliseconds, 0);
        }
    }

    private static string Shorten(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return "unknown error";
        var line = message.Split('\n')[0].Trim();
        return line.Length <= 120 ? line : line[..120];
    }
}
