namespace NexusApp.Services;

/// <summary>Projects persisted lots plus the active ship into the GameState cargo slice.</summary>
internal static class CargoProjection
{
    public static GameCargoState From(GameActiveShipState ship, IReadOnlyList<GameCargoLot> lots)
    {
        if (!ship.HasShip || string.IsNullOrWhiteSpace(ship.ShipId))
            return GameCargoState.Empty;

        var aboard = new List<GameCargoLot>();
        var used = 0;
        foreach (var lot in lots)
        {
            if (!string.Equals(lot.ShipId, ship.ShipId, StringComparison.OrdinalIgnoreCase))
                continue;
            aboard.Add(lot);
            used += lot.Scu;
        }

        var usable = ship.UsableCargoScu;
        int? free = usable is int cap ? cap - used : null;
        var over = usable is int limit && used > limit;
        return new GameCargoState(
            ship.ShipId,
            ship.DisplayName,
            aboard,
            used,
            usable,
            free,
            over);
    }
}

internal static class CargoSync
{
    public static void Publish(GameState? state, IReadOnlyList<GameCargoLot> lots)
    {
        if (state is null) return;
        state.PublishCargo(CargoProjection.From(state.ActiveShip, lots));
    }
}
