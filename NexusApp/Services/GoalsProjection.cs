namespace NexusApp.Services;

using NexusApp.Models;

/// <summary>
/// Pure snapshot builder for the durable-goals GameState slice. DataService owns
/// the shopping list; SettingsService owns blueprint ownership. Search, selection,
/// and session auto-mark tallies stay out.
/// </summary>
public static class GoalsProjection
{
    public static IReadOnlyList<GameShoppingItem> ShoppingFrom(IEnumerable<ShoppingItem> items)
        => items.Select(i => new GameShoppingItem(i.ResourceName, i.Quantity, i.Unit)).ToArray();

    public static IReadOnlyList<string> OwnedFrom(IEnumerable<string> names)
        => names.Where(n => !string.IsNullOrWhiteSpace(n)).ToArray();

    public static GameGoalsState From(IEnumerable<ShoppingItem> shopping, IEnumerable<string> owned)
        => new(ShoppingFrom(shopping), OwnedFrom(owned));
}
