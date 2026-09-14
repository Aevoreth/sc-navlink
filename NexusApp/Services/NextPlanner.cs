namespace NexusApp.Services;

/// <summary>
/// Deterministic first <c>NEXT</c> rule set (issue #7). Reads current location
/// and known hauling stops. Returns an ordered, explainable action list.
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
        Plan(new NextPlannerInput(location, hauling));

    public static NextRecommendation Plan(NextPlannerInput input)
    {
        var location = input.Location;
        var assumptions = BuildAssumptions(location);
        var candidates = new List<Candidate>(input.Hauling.Pickups.Count + input.Hauling.Dropoffs.Count);

        foreach (var stop in input.Hauling.Pickups)
            candidates.Add(MakeCandidate(stop, NextActionKind.HaulPickup, location));
        foreach (var stop in input.Hauling.Dropoffs)
            candidates.Add(MakeCandidate(stop, NextActionKind.HaulDropoff, location));

        var ordered = candidates
            .OrderByDescending(c => c.Score)
            .ThenBy(c => c.Stop.Location, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.Stop.MissionId, StringComparer.Ordinal)
            .ThenBy(c => c.Stop.Commodity, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.Kind)
            .ToList();

        var actions = new NextAction[ordered.Count];
        for (var i = 0; i < ordered.Count; i++)
            actions[i] = ToAction(ordered[i], rank: i + 1, location, assumptions);

        return new NextRecommendation(
            actions,
            Summary(actions),
            assumptions,
            location.SeenUtc);
    }

    internal static bool MatchesLocation(GameLocationState location, string stopLocation)
    {
        if (!location.HasLocation || string.IsNullOrWhiteSpace(stopLocation)) return false;
        return Eq(location.Label, stopLocation)
            || Eq(location.UexLocation, stopLocation)
            || Eq(location.RawToken, stopLocation);
    }

    private static Candidate MakeCandidate(GameHaulStop stop, NextActionKind kind, GameLocationState location)
    {
        var at = MatchesLocation(location, stop.Location);
        var score = kind == NextActionKind.HaulPickup
            ? (at ? ScoreAtPickup : ScorePickup)
            : (at ? ScoreAtDropoff : ScoreDropoff);
        return new Candidate(stop, kind, at, score);
    }

    private static NextAction ToAction(
        Candidate candidate,
        int rank,
        GameLocationState location,
        IReadOnlyList<string> assumptions)
    {
        var stop = candidate.Stop;
        var pickup = candidate.Kind == NextActionKind.HaulPickup;
        var verb = pickup ? "Collect" : "Deliver";
        var confidence = !location.HasLocation
            ? NextConfidence.Unknown
            : candidate.AtLocation ? NextConfidence.Observed : NextConfidence.Inferred;
        var explanation = candidate.AtLocation
            ? $"You are at {stop.Location}. {verb} {stop.Scu} SCU of {stop.Commodity}."
            : $"{verb} {stop.Scu} SCU of {stop.Commodity} at {stop.Location}.";

        return new NextAction(
            NextModule.Hauling,
            candidate.Kind,
            $"{verb} {stop.Scu} SCU {stop.Commodity}",
            stop.Location,
            stop.Commodity,
            stop.Scu,
            stop.MissionId,
            rank,
            candidate.Score,
            explanation,
            confidence,
            assumptions);
    }

    private static IReadOnlyList<string> BuildAssumptions(GameLocationState location)
    {
        var list = new List<string>
        {
            "Recommendations use hauling contract stops only.",
            "Cargo inventory is not used.",
        };
        if (!location.HasLocation)
            list.Add("Current location is unknown.");
        else
            list.Add("A stop at the current location ranks first.");
        return list.ToArray();
    }

    private static string Summary(IReadOnlyList<NextAction> actions)
    {
        if (actions.Count == 0) return "No hauling stop is known.";
        var primary = actions[0];
        var verb = primary.Kind == NextActionKind.HaulPickup ? "collect" : "deliver";
        return $"Next: {verb} {primary.Scu} SCU of {primary.Objective} at {primary.Location}.";
    }

    private static bool Eq(string? left, string right) =>
        !string.IsNullOrWhiteSpace(left)
        && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private readonly record struct Candidate(
        GameHaulStop Stop,
        NextActionKind Kind,
        bool AtLocation,
        int Score);
}
