namespace NexusApp.Services;

/// <summary>
/// Maps UEX vehicle slugs onto the embedded trade-ship ids (orig-100i, drak-cutlass-black).
/// Usable cargo SCU prefers the trade catalog when both describe the same hull.
/// </summary>
public static class ShipCatalogIds
{
    public static string? ToTradeShipId(string? catalogId, TradeShipCatalog trade)
    {
        if (string.IsNullOrWhiteSpace(catalogId) || trade.Ships.Count == 0) return null;
        if (trade.ById(catalogId) is not null) return catalogId;

        foreach (var ship in trade.Ships)
        {
            if (ship.Id.EndsWith("-" + catalogId, StringComparison.OrdinalIgnoreCase))
                return ship.Id;
        }

        foreach (var ship in trade.Ships)
        {
            if (string.Equals(ship.DisplayName, catalogId, StringComparison.OrdinalIgnoreCase))
                return ship.Id;
        }

        return null;
    }

    public static TradeShip? ToTradeShip(string? catalogId, TradeShipCatalog trade)
    {
        var id = ToTradeShipId(catalogId, trade);
        return id is null ? null : trade.ById(id);
    }

    public static int? UsableScu(ShipCatalogEntry? catalog, TradeShipCatalog? trade = null)
    {
        if (catalog is null) return null;
        trade ??= Trade.Value;
        var mapped = ToTradeShip(catalog.Id, trade);
        if (mapped is not null) return mapped.TotalScu;
        return catalog.CargoScu;
    }

    private static readonly Lazy<TradeShipCatalog> Trade = new(TradeShipCatalog.LoadEmbedded);
}
