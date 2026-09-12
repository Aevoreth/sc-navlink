namespace NexusApp.Services;

/// <summary>
/// Provider-neutral market catalog. Views read this (or the inherited MarketSnapshot
/// projection). They must not call UEX HTTP. Source: UEX API 2.0, https://uexcorp.space/api
/// </summary>
public interface IMarketCatalog
{
    string Source { get; }
    string LiveGameVersion { get; }
    CachedSlice<CatalogCommodity> Commodities { get; }
    CachedSlice<CatalogTerminal> Terminals { get; }
    CachedSlice<CatalogTradePrice> TradePrices { get; }
    CachedSlice<CatalogRefinedPrice> RefinedPrices { get; }
    CachedSlice<CatalogYield> Yields { get; }
    bool FetchInProgress { get; }
    string? LastError { get; }
    event Action? Changed;
    Task RefreshAsync(bool manual = false);
}

/// <summary>
/// Fetches one documented UEX data class and returns normalized domain rows.
/// UEX JSON field names stay inside the implementation.
/// Public docs: https://uexcorp.space/api (API 2.0 GET resources).
/// </summary>
internal interface IMarketDataProvider
{
    Task<(string? Live, string? Error, long ElapsedMs, int ByteCount)> FetchLiveGameVersionAsync(CancellationToken ct);
    Task<ProviderFetch<CatalogCommodity>> FetchCommoditiesAsync(CancellationToken ct);
    Task<ProviderFetch<CatalogTerminal>> FetchTerminalsAsync(CancellationToken ct);
    Task<ProviderFetch<CatalogTradePrice>> FetchTradePricesAsync(CancellationToken ct);
    Task<ProviderFetch<CatalogYield>> FetchYieldsAsync(CancellationToken ct);
    Task<ProviderFetch<CatalogRefinedPrice>> FetchRefinedPricesAsync(int commodityId, CancellationToken ct);
}
