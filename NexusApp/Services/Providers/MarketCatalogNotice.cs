namespace NexusApp.Services;

/// <summary>
/// User-facing copy for Trade's Market tab catalog browser. Pure so tests can pin
/// freshness labels without a WPF page.
/// </summary>
internal static class MarketCatalogNotice
{
    public const string Fresh = "Fresh";
    public const string Stale = "Stale";
    public const string OfflineCached = "Offline cached";
    public const string Unavailable = "Unavailable";

    public const string SourceUex = "uex";
    public const string Refresh = "Refresh";
    public const string NeverChecked = "Never checked";

    public const string ConsentEmpty =
        "Turn on live market data (above, or in Settings) to browse cached prices.";

    public const string NeedFilter =
        "Pick a commodity or a location to browse prices.";

    public const string NoCachedRows =
        "No cached prices. Refresh when you have a connection.";

    public const string NoMatchingRows = "No matching rows.";

    public const string OfflineBanner =
        "Showing last-known cached prices. The last refresh failed.";

    public const string StaleBanner =
        "Cached prices are older than the usual check interval.";

    public const string PageEyebrow = "CATALOG";
    public const string PageTitle = "Market";
    public const string PageSubtitle =
        "Browse cached UEX buy and sell prices by commodity and location. Age is the community report time.";

    public static string FreshnessLabel(ProviderFreshness freshness) => freshness switch
    {
        ProviderFreshness.Fresh => Fresh,
        ProviderFreshness.Stale => Stale,
        ProviderFreshness.OfflineCached => OfflineCached,
        _ => Unavailable,
    };

    public static string? Banner(ProviderFreshness freshness) => freshness switch
    {
        ProviderFreshness.OfflineCached => OfflineBanner,
        ProviderFreshness.Stale => StaleBanner,
        _ => null,
    };

    public static string LastChecked(DateTime? fetchedUtc, DateTime nowUtc)
    {
        if (fetchedUtc is null) return NeverChecked;
        return "Last checked " + MarketNotice.FormatAge(nowUtc - fetchedUtc.Value);
    }

    public static string TruncationNote(int shown, int total) =>
        $"Showing {shown} of {total} rows. Narrow the filters.";
}
