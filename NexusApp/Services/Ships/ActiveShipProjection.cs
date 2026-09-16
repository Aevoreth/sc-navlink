namespace NexusApp.Services;

/// <summary>Projects hangar + catalog into the GameState active-ship slice.</summary>
internal static class ActiveShipProjection
{
    public static GameActiveShipState From(
        string? activeCatalogId,
        HangarEntry? hangar,
        ShipCatalogEntry? catalog)
    {
        if (string.IsNullOrWhiteSpace(activeCatalogId) || hangar is null)
            return GameActiveShipState.Empty;

        var name = catalog?.DisplayName ?? hangar.CatalogId;
        var scu = ShipCatalogIds.UsableScu(catalog);
        return new GameActiveShipState(activeCatalogId, name, scu, GameActiveShipProvenance.Hangar);
    }
}

internal static class ActiveShipSync
{
    public static void Publish(
        GameState? state,
        string? activeCatalogId,
        IReadOnlyList<HangarEntry> hangar,
        IShipCatalog? catalog)
    {
        if (state is null) return;
        HangarEntry? entry = null;
        if (!string.IsNullOrWhiteSpace(activeCatalogId))
        {
            foreach (var row in hangar)
            {
                if (row.CatalogId == activeCatalogId)
                {
                    entry = row;
                    break;
                }
            }
        }

        var ship = catalog is null || string.IsNullOrWhiteSpace(activeCatalogId)
            ? null
            : catalog.ById(activeCatalogId);
        state.PublishActiveShip(ActiveShipProjection.From(activeCatalogId, entry, ship));
    }
}
