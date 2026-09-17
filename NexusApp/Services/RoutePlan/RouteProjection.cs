using NexusApp.Services.Map;

namespace NexusApp.Services;

/// <summary>
/// Projects contract stops into a route snapshot.
/// Pickup locations come first, then dropoffs, unless a location order is supplied.
/// The same place merges into one stop. Live seeding goes through HaulPlanner.
/// </summary>
internal static class RouteProjection
{
    public static GameRoutePlan FromHauling(GameHaulingState hauling)
    {
        ArgumentNullException.ThrowIfNull(hauling);
        return FromStops(hauling.Pickups, hauling.Dropoffs);
    }

    public static GameRoutePlan FromStops(
        IReadOnlyList<GameHaulStop> pickups,
        IReadOnlyList<GameHaulStop> dropoffs,
        IReadOnlyList<string>? locationOrder = null)
    {
        ArgumentNullException.ThrowIfNull(pickups);
        ArgumentNullException.ThrowIfNull(dropoffs);
        if (pickups.Count == 0 && dropoffs.Count == 0)
            return GameRoutePlan.Empty;

        var order = new List<string>();
        var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var actions = new Dictionary<string, List<GameRouteAction>>(StringComparer.OrdinalIgnoreCase);

        void Add(GameHaulStop stop, GameRouteActionKind kind)
        {
            var label = (stop.Location ?? "").Trim();
            if (label.Length == 0) return;

            var commodity = (stop.Commodity ?? "").Trim();
            if (commodity.Length == 0) return;

            var verb = kind == GameRouteActionKind.Pickup ? "pickup" : "delivery";
            var draft = new GameRouteAction(
                Id: $"{kind}:{stop.MissionId}:{commodity}:{label}:{actions.GetValueOrDefault(label)?.Count ?? 0}",
                Kind: kind,
                Commodity: commodity,
                PlannedScu: Math.Max(0, stop.Scu),
                RemainingScu: Math.Max(0, stop.Scu),
                ObjectiveRef: stop.MissionId ?? "",
                Reason: "Hauling contract",
                SkipRisk: GameRouteSkipRisk.Detrimental,
                SkipRiskReason: $"Skipping this {verb} fails the hauling contract for {commodity}.");
            if (!RoutePlanMath.TryNormalizeAction(draft, out var action))
                return;

            if (!actions.TryGetValue(label, out var list))
            {
                actions[label] = list = new List<GameRouteAction>();
                labels[label] = label;
                order.Add(label);
            }

            list.Add(action);
        }

        foreach (var stop in pickups)
            Add(stop, GameRouteActionKind.Pickup);
        foreach (var stop in dropoffs)
            Add(stop, GameRouteActionKind.Delivery);

        if (order.Count == 0) return GameRoutePlan.Empty;

        IReadOnlyList<string> sequence = order;
        if (locationOrder is { Count: > 0 })
        {
            var next = new List<string>(order.Count);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in locationOrder)
            {
                var key = (raw ?? "").Trim();
                if (key.Length == 0 || !actions.ContainsKey(key) || !seen.Add(key)) continue;
                next.Add(labels.TryGetValue(key, out var label) ? label : key);
            }

            foreach (var key in order)
            {
                if (seen.Add(key)) next.Add(labels[key]);
            }

            sequence = next;
        }

        var stops = new GameRouteStop[sequence.Count];
        for (var i = 0; i < sequence.Count; i++)
        {
            var key = sequence[i];
            stops[i] = new GameRouteStop(
                Id: "loc:" + labels[key],
                Sequence: i,
                Location: new GameRouteLocation(labels[key]),
                Actions: actions[key]);
        }

        return RoutePlanMath.Build(stops);
    }
}

internal static class RouteSync
{
    public static HaulComplexity Complexity { get; set; } = HaulComplexity.Moderate;
    public static MapCatalog? Map { get; set; }

    public static HaulPlanResult PublishFromHauling(GameState? state)
    {
        if (state is null) return HaulPlanResult.Empty;
        var result = HaulPlanner.Plan(state, Complexity, Map);
        state.PublishRoute(result.Plan);
        return result;
    }
}
