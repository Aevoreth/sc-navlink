namespace NexusApp.Services;

/// <summary>
/// Display fold of the shared route plus the current NEXT recommendation.
/// Operations, Starmap, and overlay paint this; they do not re-rank or invent geography.
/// Remaining-work copy only: completed hauling visits drop off the seed, so this never
/// claims "2 of 5 complete."
/// </summary>
public sealed record RouteNextView(
    GameRouteStatus Status,
    int RemainingStopCount,
    string? CurrentLabel,
    string? NextLabel,
    string StatusLabel,
    string? LegLabel,
    NextAction? Primary,
    string NextSummary,
    NextConfidence Confidence,
    string ConfidenceLabel,
    IReadOnlyList<string> Assumptions,
    bool LocationUnknown,
    bool HasShip,
    string? ShipName,
    int UsedScu,
    int? UsableScu,
    int? FreeScu,
    bool IsOverCapacity,
    string ShipLabel,
    string HoldLabel);

/// <summary>
/// Pure projection of <see cref="GameState"/> for the 0.2 presentation surfaces.
/// Calls <see cref="NextPlanner.Plan(GameState)"/> once. No WPF. No provider HTTP.
/// </summary>
public static class RouteNextProjection
{
    public static RouteNextView From(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var route = state.Route;
        var next = NextPlanner.Plan(state);
        var cargo = state.Cargo;
        var ship = state.ActiveShip;
        var remaining = 0;
        foreach (var stop in route.Stops)
        {
            if (RoutePlanMath.HasActiveWork(stop)) remaining++;
        }

        var hasShip = ship.HasShip || cargo.HasShip;
        var shipName = FirstName(cargo.DisplayName, ship.DisplayName, cargo.ShipId, ship.ShipId);
        var used = cargo.UsedScu;
        var usable = cargo.UsableScu ?? ship.UsableCargoScu;
        var free = cargo.FreeScu;
        var over = cargo.IsOverCapacity;
        var primary = next.Primary;
        var confidence = primary?.Confidence
            ?? (state.Location.HasLocation ? NextConfidence.Inferred : NextConfidence.Unknown);

        return new RouteNextView(
            Status: route.Status,
            RemainingStopCount: remaining,
            CurrentLabel: route.CurrentStop?.Location.Label,
            NextLabel: route.NextStop?.Location.Label,
            StatusLabel: StatusCopy(route.Status, remaining),
            LegLabel: LegCopy(route.CurrentStop?.Location.Label, route.NextStop?.Location.Label),
            Primary: primary,
            NextSummary: next.Summary,
            Confidence: confidence,
            ConfidenceLabel: ConfidenceCopy(confidence),
            Assumptions: next.Assumptions,
            LocationUnknown: !state.Location.HasLocation,
            HasShip: hasShip,
            ShipName: shipName,
            UsedScu: used,
            UsableScu: usable,
            FreeScu: free,
            IsOverCapacity: over,
            ShipLabel: hasShip ? (shipName ?? "Active ship") : "No active ship",
            HoldLabel: HoldCopy(hasShip, used, usable, free, over));
    }

    internal static string StatusCopy(GameRouteStatus status, int remaining) => status switch
    {
        GameRouteStatus.Empty => "No route",
        GameRouteStatus.Complete => "Route complete",
        _ => remaining == 1 ? "1 stop remaining" : remaining + " stops remaining",
    };

    internal static string? LegCopy(string? from, string? to)
    {
        if (string.IsNullOrWhiteSpace(from) && string.IsNullOrWhiteSpace(to)) return null;
        if (string.IsNullOrWhiteSpace(to)) return from;
        if (string.IsNullOrWhiteSpace(from)) return to;
        return from + " → " + to;
    }

    internal static string ConfidenceCopy(NextConfidence confidence) => confidence switch
    {
        NextConfidence.Observed => "At this stop",
        NextConfidence.Inferred => "Ranked by rule",
        _ => "Location unknown",
    };

    internal static string HoldCopy(bool hasShip, int used, int? usable, int? free, bool over)
    {
        if (!hasShip) return "No hold";
        if (usable is int cap)
        {
            var line = used + " / " + cap + " SCU";
            if (over) return line + " · over capacity";
            if (free is int left) return line + " · " + left + " free";
            return line;
        }

        if (used <= 0) return "Capacity unknown";
        return used + " SCU aboard";
    }

    private static string? FirstName(params string?[] names)
    {
        foreach (var name in names)
        {
            if (!string.IsNullOrWhiteSpace(name)) return name;
        }

        return null;
    }
}
