namespace NexusApp.Services;

/// <summary>
/// UI-free route snapshot rules: normalize, cursor, skip, insert, reorder.
/// Distance sorting and complexity presets live in HaulPlanner.
/// </summary>
internal static class RoutePlanMath
{
    public static bool TryNormalizeAction(GameRouteAction draft, out GameRouteAction action)
    {
        action = draft;
        var id = (draft.Id ?? "").Trim();
        if (id.Length == 0) return false;

        var commodity = (draft.Commodity ?? "").Trim();
        if (commodity.Length == 0) return false;
        if (draft.PlannedScu < 0) return false;

        var remaining = Math.Clamp(draft.RemainingScu, 0, draft.PlannedScu);
        var reason = (draft.Reason ?? "").Trim();
        var skipReason = (draft.SkipRiskReason ?? "").Trim();

        if (draft.SkipRisk == GameRouteSkipRisk.Detrimental)
        {
            if (skipReason.Length == 0) return false;
        }
        else
        {
            skipReason = "";
        }

        action = draft with
        {
            Id = id,
            Commodity = commodity,
            RemainingScu = remaining,
            ObjectiveRef = (draft.ObjectiveRef ?? "").Trim(),
            Reason = reason,
            SkipRiskReason = skipReason,
        };
        return true;
    }

    public static bool TryNormalizeStop(GameRouteStop draft, out GameRouteStop stop)
    {
        stop = draft;
        var id = (draft.Id ?? "").Trim();
        if (id.Length == 0) return false;

        var label = (draft.Location.Label ?? "").Trim();
        if (label.Length == 0) return false;

        var actions = new List<GameRouteAction>(draft.Actions.Count);
        foreach (var item in draft.Actions)
        {
            if (!TryNormalizeAction(item, out var action)) return false;
            actions.Add(action);
        }

        actions.Sort(CompareActions);
        var uex = string.IsNullOrWhiteSpace(draft.Location.UexLocation)
            ? null
            : draft.Location.UexLocation.Trim();

        stop = draft with
        {
            Id = id,
            Location = new GameRouteLocation(label, uex, draft.Location.TerminalId),
            Actions = actions,
        };
        return true;
    }

    public static GameRoutePlan Build(IReadOnlyList<GameRouteStop> stops)
    {
        ArgumentNullException.ThrowIfNull(stops);
        if (stops.Count == 0) return GameRoutePlan.Empty;

        var normalized = new GameRouteStop[stops.Count];
        var seenStops = new HashSet<string>(StringComparer.Ordinal);
        var seenActions = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < stops.Count; i++)
        {
            if (!TryNormalizeStop(stops[i], out var stop))
                throw new ArgumentException("Route stop is invalid.", nameof(stops));
            if (!seenStops.Add(stop.Id))
                throw new ArgumentException("Route stop ids must be unique.", nameof(stops));
            foreach (var action in stop.Actions)
            {
                if (!seenActions.Add(action.Id))
                    throw new ArgumentException("Route action ids must be unique.", nameof(stops));
            }
            normalized[i] = stop with { Sequence = i };
        }

        return Snapshot(normalized);
    }

    public static GameRoutePlan WithActionRemaining(GameRoutePlan plan, string actionId, int remaining)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var id = (actionId ?? "").Trim();
        if (id.Length == 0) throw new ArgumentException("Action id is required.", nameof(actionId));

        var found = false;
        var next = new List<GameRouteStop>(plan.Stops.Count);
        foreach (var stop in plan.Stops)
        {
            var actions = new List<GameRouteAction>(stop.Actions.Count);
            var changed = false;
            foreach (var action in stop.Actions)
            {
                if (action.Id == id)
                {
                    found = true;
                    changed = true;
                    actions.Add(action with { RemainingScu = Math.Clamp(remaining, 0, action.PlannedScu) });
                }
                else
                {
                    actions.Add(action);
                }
            }

            next.Add(changed ? stop with { Actions = actions } : stop);
        }

        if (!found) throw new ArgumentException("Unknown route action.", nameof(actionId));
        return Build(next);
    }

    public static GameRoutePlan WithStopSkipped(GameRoutePlan plan, string stopId, bool skipped = true)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var id = (stopId ?? "").Trim();
        if (id.Length == 0) throw new ArgumentException("Stop id is required.", nameof(stopId));

        var found = false;
        var next = new List<GameRouteStop>(plan.Stops.Count);
        foreach (var stop in plan.Stops)
        {
            if (stop.Id == id)
            {
                found = true;
                next.Add(stop with { Skipped = skipped });
            }
            else
            {
                next.Add(stop);
            }
        }

        if (!found) throw new ArgumentException("Unknown route stop.", nameof(stopId));
        return Build(next);
    }

    public static GameRoutePlan WithStopInserted(GameRoutePlan plan, GameRouteStop stop, int atIndex)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!TryNormalizeStop(stop, out var normalized))
            throw new ArgumentException("Route stop is invalid.", nameof(stop));

        var count = plan.Stops.Count;
        if (atIndex < 0 || atIndex > count)
            throw new ArgumentOutOfRangeException(nameof(atIndex));

        foreach (var existing in plan.Stops)
        {
            if (existing.Id == normalized.Id)
                throw new ArgumentException("Route stop ids must be unique.", nameof(stop));
        }

        var next = new List<GameRouteStop>(count + 1);
        next.AddRange(plan.Stops);
        next.Insert(atIndex, normalized);
        return Build(next);
    }

    public static GameRoutePlan WithStopsReordered(GameRoutePlan plan, IReadOnlyList<string> stopIdsInOrder)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(stopIdsInOrder);
        if (stopIdsInOrder.Count != plan.Stops.Count)
            throw new ArgumentException("Reorder must name every stop once.", nameof(stopIdsInOrder));

        var byId = new Dictionary<string, GameRouteStop>(plan.Stops.Count, StringComparer.Ordinal);
        foreach (var stop in plan.Stops)
            byId[stop.Id] = stop;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var next = new List<GameRouteStop>(plan.Stops.Count);
        foreach (var raw in stopIdsInOrder)
        {
            var id = (raw ?? "").Trim();
            if (id.Length == 0 || !byId.TryGetValue(id, out var stop) || !seen.Add(id))
                throw new ArgumentException("Reorder ids must match the plan.", nameof(stopIdsInOrder));
            next.Add(stop);
        }

        return Build(next);
    }

    internal static bool HasActiveWork(GameRouteStop stop)
    {
        if (stop.Skipped) return false;
        foreach (var action in stop.Actions)
        {
            if (action.RemainingScu > 0) return true;
        }
        return false;
    }

    internal static int CompareActions(GameRouteAction a, GameRouteAction b)
    {
        var order = VisitOrder(a.Kind).CompareTo(VisitOrder(b.Kind));
        if (order != 0) return order;
        order = string.Compare(a.Commodity, b.Commodity, StringComparison.OrdinalIgnoreCase);
        if (order != 0) return order;
        return string.Compare(a.Id, b.Id, StringComparison.Ordinal);
    }

    internal static int VisitOrder(GameRouteActionKind kind) => kind switch
    {
        GameRouteActionKind.Pickup => 0,
        GameRouteActionKind.Buy => 1,
        GameRouteActionKind.Delivery => 2,
        GameRouteActionKind.Sell => 3,
        _ => 4,
    };

    private static GameRoutePlan Snapshot(IReadOnlyList<GameRouteStop> stops)
    {
        GameRouteStop? current = null;
        GameRouteStop? next = null;
        var skipped = 0;
        var detrimentalSkips = 0;
        var warnings = new List<string>();
        var anyPartial = false;
        var anyCompleted = false;

        foreach (var stop in stops)
        {
            if (stop.Skipped)
            {
                skipped++;
                var warned = false;
                foreach (var action in stop.Actions)
                {
                    if (!action.IsDetrimental) continue;
                    if (!warned)
                    {
                        detrimentalSkips++;
                        warned = true;
                    }
                    warnings.Add(action.SkipRiskReason);
                }
                continue;
            }

            if (HasActiveWork(stop))
            {
                if (current is null) current = stop;
                else if (next is null) next = stop;

                foreach (var action in stop.Actions)
                {
                    if (action.IsPartial) anyPartial = true;
                }
            }
            else if (stop.Actions.Count > 0)
            {
                anyCompleted = true;
            }
        }

        GameRouteStatus status;
        if (current is null)
            status = GameRouteStatus.Complete;
        else if (anyPartial || anyCompleted)
            status = GameRouteStatus.InProgress;
        else
            status = GameRouteStatus.Planned;

        return new GameRoutePlan(
            stops,
            current,
            next,
            new GameRouteLeg(current, next),
            status,
            skipped,
            detrimentalSkips,
            warnings);
    }
}
