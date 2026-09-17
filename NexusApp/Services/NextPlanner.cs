namespace NexusApp.Services;

/// <summary>
/// Deterministic first <c>NEXT</c> rule set (issue #7). Reads current location
/// and known hauling stops, or remaining shared-route actions when a RoutePlan
/// is published. Returns an ordered, explainable action list.
/// No provider call. No AI model. Cargo inventory is not an input.
/// </summary>
public static class NextPlanner
{
    public const int ScoreAtPickup = 400;
    public const int ScoreAtDropoff = 300;
    public const int ScorePickup = 200;
    public const int ScoreDropoff = 100;

    public static NextRecommendation Plan(GameState state) => Plan(NextPlannerInput.From(state));

    public static NextRecommendation Plan(GameLocationState location, GameHaulingState hauling) =>
        Plan(new NextPlannerInput(location, hauling, GameRoutePlan.Empty));

    public static NextRecommendation Plan(NextPlannerInput input)
    {
        if (input.Route.HasStops)
            return PlanFromRoute(input);
        return PlanFromHauling(input);
    }

    internal static bool MatchesLocation(GameLocationState location, string stopLocation)
    {
        if (!location.HasLocation || string.IsNullOrWhiteSpace(stopLocation)) return false;
        return Eq(location.Label, stopLocation)
            || Eq(location.UexLocation, stopLocation)
            || Eq(location.RawToken, stopLocation);
    }

    internal static bool MatchesRouteLocation(GameLocationState location, GameRouteLocation stop)
    {
        if (!location.HasLocation) return false;
        return MatchesLocation(location, stop.Label)
            || (!string.IsNullOrWhiteSpace(stop.UexLocation) && MatchesLocation(location, stop.UexLocation));
    }

    private static NextRecommendation PlanFromHauling(NextPlannerInput input)
    {
        var location = input.Location;
        var assumptions = BuildHaulingAssumptions(location);
        var candidates = new List<Candidate>(input.Hauling.Pickups.Count + input.Hauling.Dropoffs.Count);

        foreach (var stop in input.Hauling.Pickups)
            candidates.Add(MakeHaulCandidate(stop, NextActionKind.HaulPickup, location));
        foreach (var stop in input.Hauling.Dropoffs)
            candidates.Add(MakeHaulCandidate(stop, NextActionKind.HaulDropoff, location));

        var ordered = candidates
            .OrderByDescending(c => c.Score)
            .ThenBy(c => c.Location, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.MissionId, StringComparer.Ordinal)
            .ThenBy(c => c.Commodity, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.Kind)
            .ToList();

        var actions = new NextAction[ordered.Count];
        for (var i = 0; i < ordered.Count; i++)
            actions[i] = ToHaulAction(ordered[i], rank: i + 1, location, assumptions);

        return new NextRecommendation(
            actions,
            HaulSummary(actions),
            assumptions,
            location.SeenUtc);
    }

    private static NextRecommendation PlanFromRoute(NextPlannerInput input)
    {
        var location = input.Location;
        var assumptions = BuildRouteAssumptions(location);
        var candidates = new List<Candidate>();

        foreach (var stop in input.Route.Stops)
        {
            if (stop.Skipped) continue;
            var at = MatchesRouteLocation(location, stop.Location);
            foreach (var item in stop.Actions)
            {
                if (item.RemainingScu <= 0) continue;
                var kind = ToNextKind(item.Kind);
                var pickupLike = item.Kind is GameRouteActionKind.Pickup or GameRouteActionKind.Buy;
                var score = pickupLike
                    ? (at ? ScoreAtPickup : ScorePickup)
                    : (at ? ScoreAtDropoff : ScoreDropoff);
                var module = item.Kind is GameRouteActionKind.Pickup or GameRouteActionKind.Delivery
                    ? NextModule.Hauling
                    : NextModule.Trade;
                candidates.Add(new Candidate(
                    kind,
                    stop.Location.Label,
                    item.Commodity,
                    item.RemainingScu,
                    item.ObjectiveRef,
                    at,
                    score,
                    module));
            }
        }

        var ordered = candidates
            .OrderByDescending(c => c.Score)
            .ThenBy(c => c.Location, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.MissionId, StringComparer.Ordinal)
            .ThenBy(c => c.Commodity, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.Kind)
            .ToList();

        var actions = new NextAction[ordered.Count];
        for (var i = 0; i < ordered.Count; i++)
            actions[i] = ToRouteAction(ordered[i], rank: i + 1, location, assumptions);

        return new NextRecommendation(
            actions,
            RouteSummary(actions),
            assumptions,
            location.SeenUtc);
    }

    private static Candidate MakeHaulCandidate(GameHaulStop stop, NextActionKind kind, GameLocationState location)
    {
        var at = MatchesLocation(location, stop.Location);
        var score = kind == NextActionKind.HaulPickup
            ? (at ? ScoreAtPickup : ScorePickup)
            : (at ? ScoreAtDropoff : ScoreDropoff);
        return new Candidate(
            kind,
            stop.Location,
            stop.Commodity,
            stop.Scu,
            stop.MissionId,
            at,
            score,
            NextModule.Hauling);
    }

    private static NextAction ToHaulAction(
        Candidate candidate,
        int rank,
        GameLocationState location,
        IReadOnlyList<string> assumptions)
    {
        var pickup = candidate.Kind == NextActionKind.HaulPickup;
        var verb = pickup ? "Collect" : "Deliver";
        var confidence = !location.HasLocation
            ? NextConfidence.Unknown
            : candidate.AtLocation ? NextConfidence.Observed : NextConfidence.Inferred;
        var explanation = candidate.AtLocation
            ? $"You are at {candidate.Location}. {verb} {candidate.Scu} SCU of {candidate.Commodity}."
            : $"{verb} {candidate.Scu} SCU of {candidate.Commodity} at {candidate.Location}.";

        return new NextAction(
            NextModule.Hauling,
            candidate.Kind,
            $"{verb} {candidate.Scu} SCU {candidate.Commodity}",
            candidate.Location,
            candidate.Commodity,
            candidate.Scu,
            candidate.MissionId,
            rank,
            candidate.Score,
            explanation,
            confidence,
            assumptions);
    }

    private static NextAction ToRouteAction(
        Candidate candidate,
        int rank,
        GameLocationState location,
        IReadOnlyList<string> assumptions)
    {
        var verb = Verb(candidate.Kind);
        var confidence = !location.HasLocation
            ? NextConfidence.Unknown
            : candidate.AtLocation ? NextConfidence.Observed : NextConfidence.Inferred;
        var explanation = candidate.AtLocation
            ? $"You are at {candidate.Location}. {verb} {candidate.Scu} SCU of {candidate.Commodity}."
            : $"{verb} {candidate.Scu} SCU of {candidate.Commodity} at {candidate.Location}.";

        return new NextAction(
            candidate.Module,
            candidate.Kind,
            $"{verb} {candidate.Scu} SCU {candidate.Commodity}",
            candidate.Location,
            candidate.Commodity,
            candidate.Scu,
            candidate.MissionId,
            rank,
            candidate.Score,
            explanation,
            confidence,
            assumptions);
    }

    private static NextActionKind ToNextKind(GameRouteActionKind kind) => kind switch
    {
        GameRouteActionKind.Pickup => NextActionKind.HaulPickup,
        GameRouteActionKind.Delivery => NextActionKind.HaulDropoff,
        GameRouteActionKind.Buy => NextActionKind.TradeBuy,
        GameRouteActionKind.Sell => NextActionKind.TradeSell,
        _ => NextActionKind.HaulPickup,
    };

    private static string Verb(NextActionKind kind) => kind switch
    {
        NextActionKind.HaulPickup => "Collect",
        NextActionKind.HaulDropoff => "Deliver",
        NextActionKind.TradeBuy => "Buy",
        NextActionKind.TradeSell => "Sell",
        _ => "Handle",
    };

    private static IReadOnlyList<string> BuildHaulingAssumptions(GameLocationState location)
    {
        var list = new List<string>
        {
            "Recommendations use hauling contract stops only.",
            "Cargo inventory is not used.",
        };
        AddLocationAssumptions(list, location);
        return list.ToArray();
    }

    private static IReadOnlyList<string> BuildRouteAssumptions(GameLocationState location)
    {
        var list = new List<string>
        {
            "Recommendations use the shared route plan.",
            "Cargo inventory is not used.",
        };
        AddLocationAssumptions(list, location);
        return list.ToArray();
    }

    private static void AddLocationAssumptions(List<string> list, GameLocationState location)
    {
        if (!location.HasLocation)
            list.Add("Current location is unknown.");
        else
            list.Add("A stop at the current location ranks first.");
    }

    private static string HaulSummary(IReadOnlyList<NextAction> actions)
    {
        if (actions.Count == 0) return "No hauling stop is known.";
        var primary = actions[0];
        var verb = primary.Kind == NextActionKind.HaulPickup ? "collect" : "deliver";
        return $"Next: {verb} {primary.Scu} SCU of {primary.Objective} at {primary.Location}.";
    }

    private static string RouteSummary(IReadOnlyList<NextAction> actions)
    {
        if (actions.Count == 0) return "No remaining route stop is known.";
        var primary = actions[0];
        var verb = Verb(primary.Kind).ToLowerInvariant();
        return $"Next: {verb} {primary.Scu} SCU of {primary.Objective} at {primary.Location}.";
    }

    private static bool Eq(string? left, string right) =>
        !string.IsNullOrWhiteSpace(left)
        && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private readonly record struct Candidate(
        NextActionKind Kind,
        string Location,
        string Commodity,
        int Scu,
        string MissionId,
        bool AtLocation,
        int Score,
        NextModule Module);
}
