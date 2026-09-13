namespace NexusApp.Services;

/// <summary>
/// Freshness of a cached provider slice. This is metadata for callers.
/// The Market tab shows it. Cached rows do not expire out of SQLite.
/// </summary>
public enum ProviderFreshness
{
    /// <summary>Last successful fetch is inside the cadence and rows exist.</summary>
    Fresh,

    /// <summary>Rows exist and the last successful fetch is older than the cadence.</summary>
    Stale,

    /// <summary>Rows exist and the latest attempt failed; serving last-known-good cache.</summary>
    OfflineCached,

    /// <summary>No cached rows are available.</summary>
    Unavailable,
}

/// <summary>One documented UEX data class persisted in the provider cache.</summary>
internal enum ProviderDataClass
{
    Commodities,
    Terminals,
    TradePrices,
    RefinedPrices,
    Yields,
    RawPrices,
    GameVersion,
}

/// <summary>A cached catalog query: domain rows plus fetch/observation clocks and freshness.</summary>
public sealed record CachedSlice<T>(
    IReadOnlyList<T> Items,
    DateTime? FetchedUtc,
    DateTime? ObservedUtc,
    ProviderFreshness Freshness,
    string Source)
{
    public static CachedSlice<T> Empty { get; } = new(
        Array.Empty<T>(), null, null, ProviderFreshness.Unavailable, "uex");
}

/// <summary>Normalized commodity catalogue row. Not a UEX DTO.</summary>
public sealed record CatalogCommodity(
    int Id,
    string Name,
    string Slug,
    bool IsRaw,
    bool IsRefined,
    int ParentId);

/// <summary>Normalized terminal/location row. Not a UEX DTO.</summary>
public sealed record CatalogTerminal(
    int Id,
    string Name,
    string Type,
    bool IsRefinery,
    string System,
    string Location,
    string Orbit,
    string PlanetOrMoon);

/// <summary>Normalized bulk buy/sell price row. <see cref="ObservedUtc"/> is UEX date_modified.</summary>
public sealed record CatalogTradePrice(
    int TerminalId,
    int CommodityId,
    double Buy,
    double Sell,
    int BuyStockScu,
    int SellDemandScu,
    int StatusBuy,
    int StatusSell,
    string ContainerSizes,
    DateTime ObservedUtc,
    string TerminalName,
    string CommodityName);

/// <summary>Normalized refined sell row from the per-commodity price endpoint.</summary>
public sealed record CatalogRefinedPrice(
    int TerminalId,
    int CommodityId,
    double Sell,
    double SellAvgWeek,
    string GameVersion,
    DateTime ObservedUtc,
    string TerminalName);

/// <summary>Normalized refinery yield row.</summary>
public sealed record CatalogYield(
    int TerminalId,
    int CommodityId,
    int BonusPct,
    int BonusPctWeek,
    DateTime ObservedUtc,
    string TerminalName);

/// <summary>
/// Retired raw-ore price row. UEX no longer supplies a live raw-price feed; the cache keeps
/// imported snapshot rows so inherited Trade schema carry-forward still works.
/// </summary>
public sealed record CatalogRawPrice(
    int TerminalId,
    int CommodityId,
    double Sell,
    double SellAvgWeek,
    string GameVersion,
    DateTime ObservedUtc,
    string TerminalName);

/// <summary>Result of one provider HTTP+normalize attempt. Empty rows are not a transport failure.</summary>
internal readonly struct ProviderFetch<T>
{
    public bool TransportOk { get; init; }
    public bool ShapeOk { get; init; }
    public IReadOnlyList<T> Rows { get; init; }
    public string? Error { get; init; }
    public long ElapsedMs { get; init; }
    public int Skipped { get; init; }
    public int ByteCount { get; init; }

    public bool Ok => TransportOk && ShapeOk;
    public bool HasRows => Rows.Count > 0;

    public static ProviderFetch<T> Failed(string error, long elapsedMs) => new()
    {
        TransportOk = false,
        ShapeOk = false,
        Rows = Array.Empty<T>(),
        Error = error,
        ElapsedMs = elapsedMs,
    };

    public static ProviderFetch<T> BadShape(long elapsedMs, int byteCount) => new()
    {
        TransportOk = true,
        ShapeOk = false,
        Rows = Array.Empty<T>(),
        Error = "the response was not in the expected format",
        ElapsedMs = elapsedMs,
        ByteCount = byteCount,
    };

    public static ProviderFetch<T> Parsed(IReadOnlyList<T> rows, int skipped, long elapsedMs, int byteCount) => new()
    {
        TransportOk = true,
        ShapeOk = true,
        Rows = rows,
        ElapsedMs = elapsedMs,
        Skipped = skipped,
        ByteCount = byteCount,
    };
}
