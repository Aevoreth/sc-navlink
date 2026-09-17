using NexusApp.Services.Map;

namespace NexusApp.Services;

/// <summary>How mixed the this-run haul route may be (issue #29).</summary>
public enum HaulComplexity { Simple, Moderate, Complex, Advanced }

/// <summary>Why a remaining contract was held off the this-run plan.</summary>
public enum HaulHoldReason { Capacity, Complexity, StopCap, ContainerCap, NoShip }

/// <summary>One contract the planner did not put on the shared route.</summary>
public sealed record HaulHold(string MissionId, HaulHoldReason Reason);

/// <summary>
/// Hull the planner measures contractual pickup SCU against. Grid segments are a hint for later
/// packing; this layer only uses usable SCU and optional max container size.
/// </summary>
public sealed record HaulCapacity(int? UsableScu, int? MaxContainerScu)
{
    public static HaulCapacity Unknown { get; } = new(null, null);

    public bool HasKnownHull => UsableScu is >= 0;

    public static HaulCapacity From(GameActiveShipState? ship)
    {
        if (ship is null || !ship.HasShip) return Unknown;

        var trade = ShipCatalogIds.ToTradeShip(ship.ShipId);
        var cargo = ShipCatalogIds.ToCargoShip(ship.ShipId);
        int? maxBox = trade?.MaxContainerScu ?? (cargo is { MaxContainerScu: > 0 } ? cargo.MaxContainerScu : null);
        if (maxBox is 0) maxBox = null;
        return new(ship.UsableCargoScu, maxBox);
    }
}

/// <summary>Feasible this-run subset plus the contracts left waiting, with reasons.</summary>
public sealed record HaulPlanResult(
    GameRoutePlan Plan,
    IReadOnlyList<string> SelectedMissionIds,
    IReadOnlyList<HaulHold> Holds,
    bool CapacityUnknown)
{
    public static HaulPlanResult Empty { get; } = new(
        GameRoutePlan.Empty,
        Array.Empty<string>(),
        Array.Empty<HaulHold>(),
        true);

    public bool IsSelected(string? missionId)
        => !string.IsNullOrWhiteSpace(missionId)
           && SelectedMissionIds.Contains(missionId, StringComparer.OrdinalIgnoreCase);

    public HaulHold? HoldFor(string? missionId)
    {
        if (string.IsNullOrWhiteSpace(missionId)) return null;
        foreach (var hold in Holds)
        {
            if (string.Equals(hold.MissionId, missionId, StringComparison.OrdinalIgnoreCase))
                return hold;
        }
        return null;
    }
}

/// <summary>
/// UI-free hauling planner: select and order compatible contracts against hull and complexity.
/// GameHaulingState stays all remaining obligations; the result's route is the this-run subset.
/// </summary>
internal static class HaulPlanner
{
    public const int SimpleContractCap = 4;
    public const int ModerateStopCap = 6;
    public const int ComplexStopCap = 10;
    public const int AdvancedStopCap = 12;

    public static HaulComplexity Parse(string? stored) => (stored ?? "").Trim().ToUpperInvariant() switch
    {
        "SIMPLE" => HaulComplexity.Simple,
        "COMPLEX" => HaulComplexity.Complex,
        "ADVANCED" => HaulComplexity.Advanced,
        _ => HaulComplexity.Moderate,
    };

    public static string ToStored(HaulComplexity complexity) => complexity switch
    {
        HaulComplexity.Simple => "SIMPLE",
        HaulComplexity.Complex => "COMPLEX",
        HaulComplexity.Advanced => "ADVANCED",
        _ => "MODERATE",
    };

    public static int StopCap(HaulComplexity complexity) => complexity switch
    {
        HaulComplexity.Simple => SimpleContractCap,
        HaulComplexity.Moderate => ModerateStopCap,
        HaulComplexity.Complex => ComplexStopCap,
        _ => AdvancedStopCap,
    };

    public static HaulPlanResult Plan(GameState state, HaulComplexity complexity, MapCatalog? map = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        return Plan(state.Hauling, complexity, HaulCapacity.From(state.ActiveShip), map, state.Location.Label);
    }

    public static HaulPlanResult Plan(
        GameHaulingState hauling,
        HaulComplexity complexity,
        HaulCapacity capacity,
        MapCatalog? map = null,
        string? playerLabel = null)
    {
        ArgumentNullException.ThrowIfNull(hauling);
        ArgumentNullException.ThrowIfNull(capacity);
        var unknown = !capacity.HasKnownHull;

        var works = new List<ContractWork>();
        foreach (var haul in hauling.Hauls)
        {
            if (!haul.IsActive) continue;
            var work = ContractWork.From(haul, hauling);
            if (work.PickupScu == 0 && work.Dropoffs.Count == 0 && work.Pickups.Count == 0) continue;
            works.Add(work);
        }

        if (works.Count == 0)
            return new HaulPlanResult(GameRoutePlan.Empty, Array.Empty<string>(), Array.Empty<HaulHold>(), unknown);

        var selected = new List<ContractWork>();
        var holds = new List<HaulHold>();
        var hull = capacity.UsableScu;
        var used = 0;

        foreach (var work in works)
        {
            if (capacity.MaxContainerScu is int maxBox
                && work.ContainerCap is int box
                && box > maxBox)
            {
                holds.Add(new HaulHold(work.MissionId, HaulHoldReason.ContainerCap));
                continue;
            }

            if (!FitsShape(selected, work, complexity))
            {
                holds.Add(new HaulHold(work.MissionId, HaulHoldReason.Complexity));
                continue;
            }

            if (complexity == HaulComplexity.Simple && selected.Count >= SimpleContractCap)
            {
                holds.Add(new HaulHold(work.MissionId, HaulHoldReason.StopCap));
                continue;
            }

            var take = TryTake(work, hull, used, complexity, unknown);
            if (take is null)
            {
                holds.Add(new HaulHold(work.MissionId, HaulHoldReason.Capacity));
                continue;
            }

            var next = new List<ContractWork>(selected.Count + 1);
            next.AddRange(selected);
            next.Add(take);
            if (complexity != HaulComplexity.Simple && UniqueLocations(next).Count > StopCap(complexity))
            {
                holds.Add(new HaulHold(work.MissionId, HaulHoldReason.StopCap));
                continue;
            }

            selected.Add(take);
            used += take.PickupScu;
        }

        if (selected.Count == 0)
            return new HaulPlanResult(GameRoutePlan.Empty, Array.Empty<string>(), holds, unknown);

        var pickups = selected.SelectMany(w => w.Pickups).ToList();
        var dropoffs = selected.SelectMany(w => w.Dropoffs).ToList();
        IReadOnlyList<string>? order = complexity is HaulComplexity.Complex or HaulComplexity.Advanced
            ? OrderLocations(pickups, dropoffs, map, playerLabel)
            : null;
        var plan = RouteProjection.FromStops(pickups, dropoffs, order);
        if (!unknown && hull is int cap && PlannedPickupScu(plan) > cap)
            throw new InvalidOperationException("Haul planner published more pickup SCU than the hull.");

        return new HaulPlanResult(
            plan,
            selected.Select(w => w.MissionId).ToArray(),
            holds,
            unknown);
    }

    internal static int PlannedPickupScu(GameRoutePlan plan)
    {
        var total = 0;
        foreach (var stop in plan.Stops)
        {
            foreach (var action in stop.Actions)
            {
                if (action.Kind == GameRouteActionKind.Pickup)
                    total += action.RemainingScu;
            }
        }
        return total;
    }

    private static ContractWork? TryTake(
        ContractWork work, int? hull, int used, HaulComplexity complexity, bool unknown)
    {
        if (unknown || hull is null) return work;

        var room = hull.Value - used;
        if (room < 0) return null;
        if (work.PickupScu <= room) return work;
        if (complexity != HaulComplexity.Advanced || room == 0 || work.PickupScu <= 0) return null;
        return work.WithPickupBudget(room);
    }

    private static bool FitsShape(
        IReadOnlyList<ContractWork> selected, ContractWork candidate, HaulComplexity complexity)
    {
        if (complexity is HaulComplexity.Complex or HaulComplexity.Advanced) return true;

        var candPick = Locations(candidate.Pickups);
        var candDrop = Locations(candidate.Dropoffs);

        if (complexity == HaulComplexity.Simple)
        {
            if (candPick.Count > 1 || candDrop.Count > 1) return false;
            if (selected.Count == 0) return true;
            var havePick = Locations(selected.SelectMany(w => w.Pickups));
            var haveDrop = Locations(selected.SelectMany(w => w.Dropoffs));
            if (havePick.Count > 1 || haveDrop.Count > 1) return false;
            return SameSingleton(havePick, candPick) && SameSingleton(haveDrop, candDrop);
        }

        if (selected.Count == 0) return true;
        var cluster = UniqueLocations(selected);
        foreach (var loc in candPick)
            if (cluster.Contains(loc)) return true;
        foreach (var loc in candDrop)
            if (cluster.Contains(loc)) return true;
        return false;
    }

    private static bool SameSingleton(HashSet<string> have, HashSet<string> cand)
    {
        if (have.Count == 0) return cand.Count <= 1;
        if (cand.Count == 0) return true;
        if (have.Count != 1 || cand.Count != 1) return false;
        foreach (var loc in cand)
            return have.Contains(loc);
        return false;
    }

    private static HashSet<string> Locations(IEnumerable<GameHaulStop> stops)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var stop in stops)
        {
            var label = (stop.Location ?? "").Trim();
            if (label.Length > 0) set.Add(label);
        }
        return set;
    }

    private static HashSet<string> UniqueLocations(IEnumerable<ContractWork> works)
        => Locations(works.SelectMany(w => w.Pickups).Concat(works.SelectMany(w => w.Dropoffs)));

    private static IReadOnlyList<string> OrderLocations(
        IReadOnlyList<GameHaulStop> pickups,
        IReadOnlyList<GameHaulStop> dropoffs,
        MapCatalog? map,
        string? playerLabel)
    {
        var labels = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(GameHaulStop stop)
        {
            var label = (stop.Location ?? "").Trim();
            if (label.Length == 0 || !seen.Add(label)) return;
            labels.Add(label);
        }

        foreach (var stop in pickups) Add(stop);
        foreach (var stop in dropoffs) Add(stop);
        if (labels.Count < 2) return labels;

        MapObject? from = null;
        if (map is not null && !string.IsNullOrWhiteSpace(playerLabel))
            from = map.ResolvePlayerLocation(playerLabel, rawToken: null);

        return from is null
            ? ConsolidationOrder.ByPlace(labels, s => s)
            : ConsolidationOrder.ByDistanceFrom(labels, s => s, map!, from);
    }

    private sealed record ContractWork(
        string MissionId,
        int? ContainerCap,
        IReadOnlyList<GameHaulStop> Pickups,
        IReadOnlyList<GameHaulStop> Dropoffs)
    {
        public int PickupScu
        {
            get
            {
                var total = 0;
                foreach (var stop in Pickups) total += Math.Max(0, stop.Scu);
                return total;
            }
        }

        public static ContractWork From(GameHaulSummary haul, GameHaulingState hauling)
        {
            var pickups = new List<GameHaulStop>();
            foreach (var stop in hauling.Pickups)
            {
                if (string.Equals(stop.MissionId, haul.MissionId, StringComparison.OrdinalIgnoreCase))
                    pickups.Add(stop);
            }

            var dropoffs = new List<GameHaulStop>();
            foreach (var stop in hauling.Dropoffs)
            {
                if (string.Equals(stop.MissionId, haul.MissionId, StringComparison.OrdinalIgnoreCase))
                    dropoffs.Add(stop);
            }

            return new ContractWork(haul.MissionId, haul.ContainerCap, pickups, dropoffs);
        }

        public ContractWork WithPickupBudget(int budget)
        {
            if (budget <= 0) return this;
            var pickups = TakeScu(Pickups, budget, out var taken);
            var dropoffs = TakeScu(Dropoffs, taken, out _);
            return this with { Pickups = pickups, Dropoffs = dropoffs };
        }

        private static IReadOnlyList<GameHaulStop> TakeScu(
            IReadOnlyList<GameHaulStop> stops, int budget, out int taken)
        {
            taken = 0;
            if (budget <= 0 || stops.Count == 0) return Array.Empty<GameHaulStop>();
            var next = new List<GameHaulStop>(stops.Count);
            foreach (var stop in stops)
            {
                if (taken >= budget) break;
                var scu = Math.Max(0, stop.Scu);
                if (scu == 0)
                {
                    next.Add(stop);
                    continue;
                }

                var slice = Math.Min(scu, budget - taken);
                if (slice <= 0) continue;
                next.Add(stop with { Scu = slice });
                taken += slice;
            }

            return next;
        }
    }
}
