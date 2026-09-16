using NexusApp.Models;

namespace NexusApp.Services;

/// <summary>
/// Catalog-facing helpers for the bundled blueprint seed. The seed is the
/// supported source. <c>components.ini</c> is a name map, not the catalog.
/// </summary>
public static class BlueprintCatalog
{
    public static readonly string[] PreferredCategoryOrder =
        ["Armor", "Weapons", "Ship Components", "Ammo"];

    public enum NameStatus { InCatalog, Unknown, CatalogUnavailable }

    public static IReadOnlyList<string> CategoriesFrom(IEnumerable<Blueprint> blueprints)
    {
        var present = new HashSet<string>(StringComparer.Ordinal);
        foreach (var b in blueprints)
        {
            if (!string.IsNullOrWhiteSpace(b.Category))
                present.Add(b.Category);
        }

        var ordered = new List<string>();
        foreach (var cat in PreferredCategoryOrder)
        {
            if (present.Remove(cat))
                ordered.Add(cat);
        }

        ordered.AddRange(present.OrderBy(c => c, StringComparer.OrdinalIgnoreCase));
        return ordered;
    }

    public static NameStatus Classify(string name, IReadOnlyCollection<string> catalogNames)
    {
        if (catalogNames.Count == 0) return NameStatus.CatalogUnavailable;
        foreach (var n in catalogNames)
        {
            if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase))
                return NameStatus.InCatalog;
        }
        return NameStatus.Unknown;
    }
}
