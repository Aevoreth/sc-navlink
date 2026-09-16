namespace NexusApp.Services;

/// <summary>UI-free filter for <see cref="ShipCatalogEntry"/> rows.</summary>
public static class ShipCatalogQueries
{
    public static IReadOnlyList<ShipCatalogEntry> Filter(
        IEnumerable<ShipCatalogEntry> rows, ShipCatalogQuery query)
    {
        var search = (query.Search ?? "").Trim();
        var list = new List<ShipCatalogEntry>();
        foreach (var row in rows)
        {
            if (!query.IncludeConcept && row.IsConcept) continue;
            if (!query.IncludeGround && row.IsGroundVehicle) continue;
            if (!string.IsNullOrEmpty(query.Manufacturer)
                && !string.Equals(row.Manufacturer, query.Manufacturer, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!string.IsNullOrEmpty(query.Role)
                && !string.Equals(row.Role, query.Role, StringComparison.OrdinalIgnoreCase))
                continue;
            if (search.Length > 0 && !MatchesSearch(row, search)) continue;
            list.Add(row);
        }

        list.Sort((a, b) =>
        {
            var byMfr = string.Compare(a.Manufacturer, b.Manufacturer, StringComparison.OrdinalIgnoreCase);
            return byMfr != 0
                ? byMfr
                : string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase);
        });
        return list;
    }

    /// <summary>
    /// Chip labels from rows that pass the concept/ground gates. Manufacturer, role, and search
    /// do not shrink the chip set, so a selected chip stays visible.
    /// </summary>
    public static (IReadOnlyList<string> Manufacturers, IReadOnlyList<string> Roles) Facets(
        IEnumerable<ShipCatalogEntry> rows, bool includeConcept, bool includeGround)
    {
        var manufacturers = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var roles = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            if (!includeConcept && row.IsConcept) continue;
            if (!includeGround && row.IsGroundVehicle) continue;
            if (!string.IsNullOrWhiteSpace(row.Manufacturer)) manufacturers.Add(row.Manufacturer.Trim());
            if (!string.IsNullOrWhiteSpace(row.Role)) roles.Add(row.Role.Trim());
        }
        return (manufacturers.ToList(), roles.ToList());
    }

    private static bool MatchesSearch(ShipCatalogEntry row, string search) =>
        row.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase)
        || row.Manufacturer.Contains(search, StringComparison.OrdinalIgnoreCase)
        || row.Id.Contains(search, StringComparison.OrdinalIgnoreCase)
        || row.Role.Contains(search, StringComparison.OrdinalIgnoreCase);
}
