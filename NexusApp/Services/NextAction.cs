namespace NexusApp.Services;

/// <summary>
/// Which operational slice produced a <see cref="NextAction"/>. Desktop and overlay
/// surfaces can switch on this without knowing a provider. Later modules add values
/// here; they do not change the <see cref="NextAction"/> shape.
/// </summary>
public enum NextModule
{
    Hauling,
    Trade,
    Mining,
    Refinery,
    Goals,
}

/// <summary>
/// The kind of recommended next action. Hauling is the first producer.
/// Trade, mining, refinery, and blueprint kinds are reserved so a later
/// rule set can emit them on the same list.
/// </summary>
public enum NextActionKind
{
    HaulPickup,
    HaulDropoff,
    TradeBuy,
    TradeSell,
    Mine,
    Refine,
    Blueprint,
}

/// <summary>
/// How strongly the recommendation is tied to observed live state.
/// Unknown: no current location. Inferred: ranked by rule only.
/// Observed: the current location matches the stop.
/// </summary>
public enum NextConfidence
{
    Unknown,
    Inferred,
    Observed,
}

/// <summary>
/// One explainable next action. Independent of WPF. Later modules fill the
/// same fields so desktop and overlay contracts stay stable.
/// </summary>
public sealed record NextAction(
    NextModule Module,
    NextActionKind Kind,
    string Title,
    string Location,
    string Objective,
    int Scu,
    string MissionId,
    int Rank,
    int Score,
    string Explanation,
    NextConfidence Confidence,
    IReadOnlyList<string> Assumptions);

/// <summary>
/// Ordered next-action list for one planning pass. <see cref="Actions"/> is
/// empty when no hauling stop is known. The list is the UI contract.
/// </summary>
public sealed record NextRecommendation(
    IReadOnlyList<NextAction> Actions,
    string Summary,
    IReadOnlyList<string> Assumptions,
    DateTime? LocationSeenUtc)
{
    public NextAction? Primary => Actions.Count == 0 ? null : Actions[0];
}

/// <summary>
/// Inputs the first <c>NEXT</c> rule set consumes. Location plus hauling stops
/// are the original path. When <see cref="Route"/> has stops, remaining unskipped
/// route actions are used instead. Later mining, refinery, or goal slices can be
/// added without changing <see cref="NextAction"/>.
/// </summary>
public sealed record NextPlannerInput(
    GameLocationState Location,
    GameHaulingState Hauling,
    GameRoutePlan Route)
{
    public static NextPlannerInput From(GameState state) =>
        new(state.Location, state.Hauling, state.Route);
}
