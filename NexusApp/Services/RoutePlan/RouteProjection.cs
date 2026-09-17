namespace NexusApp.Services;

/// <summary>
/// Projects contract stops into the shared route snapshot.
/// Pickup locations come first, then dropoffs; the same place merges into one stop.
/// This is a seed order, not a planner.
/// </summary>
internal static class RouteProjection
{
    public static GameRoutePlan FromHauling(GameHaulingState hauling)
    {
        ArgumentNullException.ThrowIfNull(hauling);
        if (hauling.Pickups.Count == 0 && hauling.Dropoffs.Count == 0)
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

        foreach (var stop in hauling.Pickups)
            Add(stop, GameRouteActionKind.Pickup);
        foreach (var stop in hauling.Dropoffs)
            Add(stop, GameRouteActionKind.Delivery);

        if (order.Count == 0) return GameRoutePlan.Empty;

        var stops = new GameRouteStop[order.Count];
        for (var i = 0; i < order.Count; i++)
        {
            var key = order[i];
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
    public static void PublishFromHauling(GameState? state)
    {
        if (state is null) return;
        state.PublishRoute(RouteProjection.FromHauling(state.Hauling));
    }
}
