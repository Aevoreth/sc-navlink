namespace NexusApp.Services;

/// <summary>What happens at a route stop. Pickup/Buy before Delivery/Sell on a visit.</summary>
public enum GameRouteActionKind { Pickup, Delivery, Buy, Sell }

/// <summary>
/// Whether skipping this item is a bad idea. Every stop can still be skipped.
/// <see cref="Detrimental"/> needs a <c>SkipRiskReason</c> so later UI can warn.
/// </summary>
public enum GameRouteSkipRisk { None, Detrimental }

/// <summary>Derived progress of the shared route snapshot.</summary>
public enum GameRouteStatus { Empty, Planned, InProgress, Complete }

/// <summary>
/// Place identity for a route stop. <see cref="Label"/> is the compare/display key.
/// Optional UEX fields are hints, not a catalog requirement.
/// </summary>
public sealed record GameRouteLocation(
    string Label,
    string? UexLocation = null,
    int? TerminalId = null);

/// <summary>
/// One pickup, delivery, buy, or sell on the shared route.
///
/// Inventory and contract obligations stay out. <see cref="SkipRisk"/> does not
/// forbid skip; it only says skipping would hurt, with a reason.
/// </summary>
public sealed record GameRouteAction(
    string Id,
    GameRouteActionKind Kind,
    string Commodity,
    int PlannedScu,
    int RemainingScu,
    string ObjectiveRef,
    string Reason,
    GameRouteSkipRisk SkipRisk,
    string SkipRiskReason)
{
    public bool IsComplete => RemainingScu <= 0;
    public bool IsPartial => RemainingScu > 0 && RemainingScu < PlannedScu;
    public bool IsDetrimental => SkipRisk == GameRouteSkipRisk.Detrimental;
}

/// <summary>
/// One ordered visit. <see cref="Sequence"/> is dense 0..n-1 after every mutation.
/// <see cref="Skipped"/> is an explicit visit skip; the stop stays on the plan.
/// </summary>
public sealed record GameRouteStop(
    string Id,
    int Sequence,
    GameRouteLocation Location,
    IReadOnlyList<GameRouteAction> Actions,
    bool Skipped = false)
{
    public bool Equals(GameRouteStop? other) =>
        other is not null
        && Id == other.Id
        && Sequence == other.Sequence
        && Location == other.Location
        && Skipped == other.Skipped
        && Actions.SequenceEqual(other.Actions);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Id);
        hash.Add(Sequence);
        hash.Add(Location);
        hash.Add(Skipped);
        foreach (var action in Actions) hash.Add(action);
        return hash.ToHashCode();
    }
}

/// <summary>Current visit to the following active visit. Both null when the plan has no active work.</summary>
public sealed record GameRouteLeg(GameRouteStop? From, GameRouteStop? To)
{
    public static GameRouteLeg Empty { get; } = new(null, null);
}

/// <summary>
/// Immutable shared route snapshot published into <see cref="GameState"/>.
///
/// This is the current operation's ordered stops, not a hauling consolidation table,
/// not pinned trade routes, and not a Starmap draft. Hauling seeds it. Later planners
/// insert, reorder, and add trade actions on this same list.
/// </summary>
public sealed record GameRoutePlan(
    IReadOnlyList<GameRouteStop> Stops,
    GameRouteStop? CurrentStop,
    GameRouteStop? NextStop,
    GameRouteLeg CurrentLeg,
    GameRouteStatus Status,
    int SkippedStopCount,
    int DetrimentalSkipCount,
    IReadOnlyList<string> SkipWarnings)
{
    public static GameRoutePlan Empty { get; } = new(
        Array.Empty<GameRouteStop>(),
        null,
        null,
        GameRouteLeg.Empty,
        GameRouteStatus.Empty,
        0,
        0,
        Array.Empty<string>());

    public bool HasStops => Stops.Count > 0;

    public bool Equals(GameRoutePlan? other) =>
        other is not null
        && CurrentStop == other.CurrentStop
        && NextStop == other.NextStop
        && CurrentLeg == other.CurrentLeg
        && Status == other.Status
        && SkippedStopCount == other.SkippedStopCount
        && DetrimentalSkipCount == other.DetrimentalSkipCount
        && Stops.SequenceEqual(other.Stops)
        && SkipWarnings.SequenceEqual(other.SkipWarnings);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(CurrentStop);
        hash.Add(NextStop);
        hash.Add(CurrentLeg);
        hash.Add(Status);
        hash.Add(SkippedStopCount);
        hash.Add(DetrimentalSkipCount);
        foreach (var stop in Stops) hash.Add(stop);
        foreach (var warning in SkipWarnings) hash.Add(warning);
        return hash.ToHashCode();
    }
}
