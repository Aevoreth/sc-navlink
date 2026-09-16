namespace NexusApp.Services;

/// <summary>
/// UI-free copy for the Ships hull detail pane. Views must not format these facts inline.
/// </summary>
public static class ShipDetailCopy
{
    public static string StatusBadge(ShipCatalogEntry ship) =>
        ship.IsConcept ? "CONCEPT" : ship.IsGroundVehicle ? "GROUND" : "FLYABLE";

    public static string Crew(ShipCatalogEntry ship)
    {
        var crew = (ship.Crew ?? "").Trim();
        return crew.Length == 0 ? "—" : crew;
    }

    public static string Mass(ShipCatalogEntry ship) =>
        ship.Mass > 0 ? $"{ship.Mass:n0} kg" : "—";

    public static string Dimensions(ShipCatalogEntry ship)
    {
        if (ship.Length <= 0 && ship.Beam <= 0 && ship.Height <= 0) return "—";
        return $"{Meters(ship.Length)} × {Meters(ship.Beam)} × {Meters(ship.Height)} m";
    }

    public static string Cargo(ShipCatalogEntry ship)
    {
        var scu = ShipCatalogIds.UsableScu(ship);
        return scu is > 0 ? $"{scu} SCU" : "—";
    }

    public static string RoleLine(ShipCatalogEntry ship)
    {
        var role = (ship.Role ?? "").Trim();
        return role.Length == 0 ? "—" : role;
    }

    public const string WikiCredit = "Star Citizen Wiki";

    /// <summary>
    /// RSI/UEX store URL only when it is HTTPS and on the same host allowlist as ship images.
    /// Null means the pane must not offer a link.
    /// </summary>
    public static string? SafeStoreUrl(string? url) =>
        ShipImageCache.IsAllowedUrl(url) ? url : null;

    public static string PriceAuec(double price) => $"{price:n0} aUEC";

    public static string ListingLine(string terminalName, double price)
    {
        var name = string.IsNullOrWhiteSpace(terminalName) ? "Unknown terminal" : terminalName.Trim();
        return $"{name}  ·  {PriceAuec(price)}";
    }

    public static IReadOnlyList<CatalogVehiclePurchase> RankPurchases(
        IEnumerable<CatalogVehiclePurchase> rows)
    {
        return rows
            .Where(p => p.PriceBuy > 0)
            .OrderBy(p => p.PriceBuy)
            .ThenBy(p => p.TerminalName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static IReadOnlyList<CatalogVehicleRental> RankRentals(
        IEnumerable<CatalogVehicleRental> rows)
    {
        return rows
            .Where(r => r.PriceRent > 0)
            .OrderBy(r => r.PriceRent)
            .ThenBy(r => r.TerminalName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string Meters(double value) => value > 0 ? value.ToString("n1") : "—";
}
