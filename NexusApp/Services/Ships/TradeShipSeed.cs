using NexusApp.Models;

namespace NexusApp.Services;

/// <summary>
/// Seeds the trade planner ship from the hangar active ship when the planner has no selection yet.
/// Does not overwrite an existing <see cref="AppSettings.TradeShipId"/>.
/// </summary>
internal static class TradeShipSeed
{
    private static readonly Lazy<TradeShipCatalog> Trade = new(TradeShipCatalog.LoadEmbedded);

    public static bool MaybeSeed(AppSettings settings, string? catalogId)
    {
        if (!string.IsNullOrEmpty(settings.TradeShipId)) return false;
        var tradeId = ShipCatalogIds.ToTradeShipId(catalogId, Trade.Value);
        if (tradeId is null) return false;
        settings.TradeShipId = tradeId;
        return true;
    }
}
