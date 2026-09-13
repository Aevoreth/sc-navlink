namespace NexusApp.Services;

/// <summary>
/// Side filter for the native Market catalog. Buy keeps rows with a buy price.
/// Sell keeps rows with a sell price. Both keeps a row that has either price.
/// </summary>
internal enum MarketCatalogSide
{
    Both,
    Buy,
    Sell,
}

/// <summary>
/// Commodity name is an exact case-insensitive match (the Trade picker
/// commits full names). Location text is a case-insensitive contains.
/// Empty or ALL system means no system constraint.
/// </summary>
internal sealed record MarketCatalogFilter(
    string? CommodityText = null,
    string? System = null,
    string? LocationText = null,
    MarketCatalogSide Side = MarketCatalogSide.Both,
    int? TerminalId = null)
{
    /// <summary>
    /// True when the view has a commodity, location, or terminal constraint.
    /// System or side alone is not enough to list the full price table.
    /// </summary>
    public bool HasBrowseConstraint =>
        !string.IsNullOrWhiteSpace(CommodityText)
        || !string.IsNullOrWhiteSpace(LocationText)
        || TerminalId is not null;
}

/// <summary>
/// One catalog price row ready for Trade's Market tab. System and location come from
/// the terminal catalog when the id is present.
/// </summary>
internal sealed record MarketCatalogRow(
    int CommodityId,
    string CommodityName,
    int TerminalId,
    string TerminalName,
    string System,
    string Location,
    double Buy,
    double Sell,
    int BuyStockScu,
    int SellDemandScu,
    int StatusBuy,
    int StatusSell,
    DateTime ObservedUtc);

/// <summary>
/// Pure reads over cached catalog slices. Trade's Market tab filters through this.
/// It never calls UEX HTTP. Omitted snapshot ids stay if they are in the slice.
/// </summary>
internal static class MarketCatalogQueries
{
    public static IReadOnlyList<MarketCatalogRow> Query(
        IReadOnlyList<CatalogTradePrice>? prices,
        IReadOnlyList<CatalogTerminal>? terminals,
        MarketCatalogFilter? filter)
    {
        if (prices is null || prices.Count == 0)
            return Array.Empty<MarketCatalogRow>();

        filter ??= new MarketCatalogFilter();
        Dictionary<int, CatalogTerminal>? byId = null;
        if (terminals is { Count: > 0 })
        {
            byId = new Dictionary<int, CatalogTerminal>(terminals.Count);
            foreach (var t in terminals)
                byId[t.Id] = t;
        }

        var commodity = filter.CommodityText?.Trim();
        var location = filter.LocationText?.Trim();
        var system = NormalizeSystem(filter.System);

        var mapped = new List<MarketCatalogRow>();
        foreach (var p in prices)
        {
            if (!string.IsNullOrEmpty(commodity)
                && !p.CommodityName.Equals(commodity, StringComparison.OrdinalIgnoreCase))
                continue;

            if (filter.TerminalId is { } tid && p.TerminalId != tid)
                continue;

            CatalogTerminal? t = null;
            byId?.TryGetValue(p.TerminalId, out t);
            var sys = t?.System ?? "";
            if (system is not null && !sys.Equals(system, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!string.IsNullOrEmpty(location) && !LocationMatches(p, t, location))
                continue;

            if (!SideMatches(p, filter.Side))
                continue;

            mapped.Add(new MarketCatalogRow(
                p.CommodityId,
                p.CommodityName,
                p.TerminalId,
                p.TerminalName,
                sys,
                t?.Location ?? "",
                p.Buy,
                p.Sell,
                p.BuyStockScu,
                p.SellDemandScu,
                p.StatusBuy,
                p.StatusSell,
                p.ObservedUtc));
        }

        mapped.Sort(CompareRows);
        return mapped;
    }

    public static IReadOnlyList<string> CommodityNames(IReadOnlyList<CatalogTradePrice>? prices)
    {
        if (prices is null || prices.Count == 0)
            return Array.Empty<string>();

        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in prices)
        {
            if (!string.IsNullOrWhiteSpace(p.CommodityName))
                names.Add(p.CommodityName);
        }

        return names.ToList();
    }

    private static string? NormalizeSystem(string? system)
    {
        if (string.IsNullOrWhiteSpace(system)) return null;
        if (system.Equals("ALL", StringComparison.OrdinalIgnoreCase)) return null;
        return system.Trim();
    }

    private static bool SideMatches(CatalogTradePrice p, MarketCatalogSide side) => side switch
    {
        MarketCatalogSide.Buy => p.Buy > 0,
        MarketCatalogSide.Sell => p.Sell > 0,
        _ => p.Buy > 0 || p.Sell > 0,
    };

    private static bool LocationMatches(CatalogTradePrice p, CatalogTerminal? t, string needle)
    {
        if (p.TerminalName.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
            return true;
        if (t is null) return false;
        return t.Name.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0
            || t.Location.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0
            || t.Orbit.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0
            || t.PlanetOrMoon.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static int CompareRows(MarketCatalogRow a, MarketCatalogRow b)
    {
        int sell = b.Sell.CompareTo(a.Sell);
        if (sell != 0) return sell;
        int commodity = string.Compare(a.CommodityName, b.CommodityName, StringComparison.OrdinalIgnoreCase);
        if (commodity != 0) return commodity;
        return string.Compare(a.TerminalName, b.TerminalName, StringComparison.OrdinalIgnoreCase);
    }
}
