using NexusApp.Models;

namespace NexusApp.Services;

// BETA. App-lifetime cargo-hauling consumer of the shared Game.log tail (GameLogFeed). Mirrors
// GameLogSession's shape (public Ingest for headless tests; a private feed when none is injected).
// Reads a game-authored file (read-only) and never extracts player identity. A mission id becomes a
// "haul" only when a HaulCargo/CargoHauling marker or a "Deliver N SCU" objective is seen; the
// generic ObjectiveUpserted/EndMission lines are applied only to known hauls so bounty/combat
// missions are ignored.
public sealed class HaulTracker : IDisposable
{
    private readonly GameLogFeed _feed;
    private readonly bool _ownsFeed;            // true only when nobody handed us a shared feed
    private GameLogSubscription? _sub;
    private readonly Dictionary<string, Haul> _byId = new();
    private readonly List<Haul> _order = new();   // insertion order for display
    private string _currentShardId = "";          // last shard seen, to clear hauls on a shard change
    private readonly Dictionary<string, ContractDetails> _pendingByOrg = new();   // OCR contractor org -> detail, applied when its haul appears

    // Attaches with the replay appetite: every start of the tail replays the current Game.log from
    // the top, so an app restart mid-session rebuilds active hauls (parsing is idempotent: markers
    // dedupe by objectiveId).
    public HaulTracker(GameLogFeed? feed = null, GameState? gameState = null)
    {
        _feed = feed ?? new GameLogFeed();
        _ownsFeed = feed is null;
        GameState = gameState;
        _sub = _feed.Subscribe(Ingest, includeReplay: true, onLogReset: Reset);
    }

    /// <summary>
    /// Shared operational state this tracker publishes the hauling slice into. Null when a test
    /// or inherited constructor did not supply a store.
    /// </summary>
    public GameState? GameState { get; }

    public IReadOnlyList<Haul> AllHauls => _order;
    public IReadOnlyList<Haul> ActiveHauls => _order.FindAll(h => h.IsActive);
    public IReadOnlyList<Haul> FinishedHauls => _order.FindAll(h => !h.IsActive);

    public event Action? Changed;
    public event Action<Haul>? HaulEnded;
    /// <summary>Raised the first time a haul gets OCR contract detail attached (for a toast / indicator).</summary>
    public event Action<Haul>? ContractPaired;

    public void Reset() => ClearInternal();   // new SC session (Game.log reset)

    /// <summary>User-requested clear of all hauls, active and finished (the Clear-all button).</summary>
    public void ClearAll()
    {
        if (_order.Count == 0) return;
        Logger.Info("[HAUL] cleared all hauls");
        ClearInternal();
    }

    /// <summary>Delete one haul by mission id (the per-mission x button).</summary>
    public void Remove(string missionId)
    {
        if (_byId.Remove(missionId, out var h))
        {
            _order.Remove(h);
            Logger.Info($"[HAUL] removed haul {h.Company}");
            RaiseChanged();
        }
    }

    /// <summary>
    /// Session-only: mark a collect or deliver stop done (or reopen it). Remaining-work
    /// consolidation and the shared route reseed so NEXT can advance. Does not write Game.log
    /// or cargo lots. Unknown, finished, or unmatched stops return false.
    /// </summary>
    public bool SetStopCompleted(string missionId, HaulRole role, string? location, string? commodity, bool completed)
    {
        if (!ApplyStopCompleted(missionId, role, location, commodity, completed)) return false;
        RaiseChanged();
        return true;
    }

    /// <summary>
    /// Subtract this-run planned SCU from a remaining obligation. When the route has no matching
    /// slice, the full remaining amount is marked done. Does not write cargo lots.
    /// </summary>
    public bool CompletePlannedStop(string missionId, HaulRole role, string? location, string? commodity)
    {
        var slice = PlannedSlice(GameState?.Route, missionId, role, location, commodity);
        if (slice is int scu && scu > 0)
        {
            if (!ApplyStopProgress(missionId, role, location, commodity, scu)) return false;
            RaiseChanged();
            return true;
        }

        return SetStopCompleted(missionId, role, location, commodity, completed: true);
    }

    /// <summary>Mark several remaining stops in one publish so NEXT reseeds once.</summary>
    public bool SetStopsCompleted(
        IEnumerable<(string MissionId, HaulRole Role, string? Location, string? Commodity)> stops,
        bool completed = true)
    {
        var any = false;
        foreach (var s in stops)
            any |= ApplyStopCompleted(s.MissionId, s.Role, s.Location, s.Commodity, completed);
        if (any) RaiseChanged();
        return any;
    }

    /// <summary>
    /// Mark the current NEXT haul step done. Trade/buy/sell recommendations are ignored.
    /// </summary>
    public bool TryCompleteNext(NextAction? action)
    {
        if (action is null || string.IsNullOrWhiteSpace(action.MissionId)) return false;
        var role = action.Kind switch
        {
            NextActionKind.HaulPickup => HaulRole.Pickup,
            NextActionKind.HaulDropoff => HaulRole.Dropoff,
            _ => (HaulRole?)null,
        };
        if (role is null) return false;
        return CompletePlannedStop(action.MissionId, role.Value, action.Location, action.Objective);
    }

    /// <summary>OCR collect is done only when the player marked it. Game.log pickup-complete is not cargo collected.</summary>
    public static bool ObjectivePickupComplete(Haul h, ContractObjective o)
    {
        _ = h;
        return o.PickupCompleted;
    }

    /// <summary>OCR deliver is done when the player marked it or matching dropoff legs are complete.</summary>
    public static bool ObjectiveDropoffComplete(Haul h, ContractObjective o)
    {
        if (o.DropoffCompleted) return true;
        var drops = h.Legs.FindAll(l => l.Role == HaulRole.Dropoff);
        if (drops.Count == 0) return false;
        var matched = 0;
        foreach (var leg in drops)
        {
            if (!DropoffLegMatchesObjective(leg, o)) continue;
            matched++;
            if (!leg.Completed) return false;
        }
        return matched > 0;
    }

    private void ClearInternal()
    {
        _byId.Clear();
        _order.Clear();
        _pendingByOrg.Clear();
        RaiseChanged();
    }

    // Groups incomplete legs of all active hauls by location: where to load (Pickups) and
    // where to drop (Dropoffs). Pickup commodity/SCU is borrowed from the sibling dropoff
    // that shares the same CargoKey (same physical cargo, different direction).
    public Consolidation BuildConsolidation()
    {
        var con = new Consolidation();
        var pickups = new Dictionary<string, ConsolidationStop>();
        var dropoffs = new Dictionary<string, ConsolidationStop>();

        foreach (var h in _order)
        {
            if (!h.IsActive) continue;

            // Prefer OCR-sourced objectives (what the cards render): they carry per-leg commodity/SCU and
            // the fine-grained pickup/dropoff. The log legs often lack a Deliver line (empty commodity /
            // 0 SCU) or give only a coarse system destination, which produced "Cargo / 0" pickups and
            // system-level ("Stanton System") dropoffs in the table.
            if (h.ContractObjectives.Count > 0)
            {
                var addedOcrPickup = false;
                foreach (var o in h.ContractObjectives)
                {
                    if (!string.IsNullOrWhiteSpace(o.Pickup) && o.PickupRemaining > 0)
                    {
                        AddItem(pickups, o.Pickup, o.Commodity, o.PickupRemaining, h.MissionId);
                        addedOcrPickup = true;
                    }
                    if (!string.IsNullOrWhiteSpace(o.Dropoff) && o.DropoffRemaining > 0)
                        AddItem(dropoffs, o.Dropoff, o.Commodity, o.DropoffRemaining, h.MissionId);
                }

                // OCR often reads Deliver and misses Collect. If no collect remains named, keep
                // incomplete log pickups so NEXT does not jump straight to Deliver.
                if (!addedOcrPickup && !h.ContractObjectives.Exists(o => o.PickupCompleted))
                {
                    foreach (var leg in h.Legs)
                    {
                        if (leg.Role != HaulRole.Pickup || leg.Completed) continue;
                        var sib = h.Legs.Find(l => l.Role == HaulRole.Dropoff && l.CargoKey == leg.CargoKey);
                        var name = string.IsNullOrWhiteSpace(h.PickupName) ? "Pickup (TBD)" : h.PickupName;
                        var remaining = PickupLegRemaining(leg, sib);
                        if (remaining > 0)
                            AddItem(pickups, name, sib?.Commodity ?? "", remaining, h.MissionId);
                    }
                }
                continue;
            }

            foreach (var leg in h.Legs)
            {
                if (leg.Completed) continue;

                if (leg.Role == HaulRole.Dropoff)
                {
                    var remaining = LegRemaining(leg);
                    if (remaining > 0)
                        AddItem(dropoffs, leg.Destination, leg.Commodity, remaining, h.MissionId);
                }

                if (leg.Role == HaulRole.Pickup)
                {
                    var sib = h.Legs.Find(l => l.Role == HaulRole.Dropoff && l.CargoKey == leg.CargoKey);
                    var name = string.IsNullOrWhiteSpace(h.PickupName) ? "Pickup (TBD)" : h.PickupName;
                    AddItem(pickups, name, sib?.Commodity ?? "", PickupLegRemaining(leg, sib), h.MissionId);
                }
            }
        }

        con.Pickups.AddRange(pickups.Values);
        con.Dropoffs.AddRange(dropoffs.Values);
        return con;

        static void AddItem(Dictionary<string, ConsolidationStop> map, string loc, string commodity, int scu, string mid)
        {
            if (string.IsNullOrWhiteSpace(loc)) return;
            if (!map.TryGetValue(loc, out var stop)) { stop = new ConsolidationStop { Location = loc }; map[loc] = stop; }
            stop.Items.Add((commodity, scu, mid));
        }
    }

    public void Ingest(GameLogEntry e)
    {
        var raw = e.Raw;

        // Leaving the PU entirely (menu / quit / disconnect) abandons your in-game contracts, so clear
        // all hauls on the EAC EndSession - the same signal the shard tracker uses to drop the "current"
        // shard. This line does NOT pass LooksHaulRelevant, so it must be handled before that filter.
        if (raw.Contains("CDisciplineServiceExternal::EndSession"))
        {
            _currentShardId = "";
            if (_order.Count > 0) { Logger.Info("[HAUL] shard exit - cleared hauls"); ClearInternal(); }
            return;
        }

        // Missions are shard-specific: changing shard/server abandons your contracts in-game, so a new
        // shard clears all hauls (active and finished). Reuses the shard parser to read the join line.
        if (raw.Contains("<Join PU>"))
        {
            var shardId = ShardLogParser.ParseJoin(raw)?.ShardId;
            if (shardId is not null && shardId != _currentShardId)
            {
                _currentShardId = shardId;
                if (_order.Count > 0) { Logger.Info("[HAUL] shard changed - cleared hauls"); ClearInternal(); }
            }
            return;
        }

        if (!HaulLogParser.LooksHaulRelevant(raw)) return;

        var marker = HaulLogParser.ParseMarker(raw);
        if (marker is not null) { ApplyMarker(marker); return; }

        var deliver = HaulLogParser.ParseDeliver(raw);
        if (deliver is not null) { ApplyDeliver(deliver); return; }

        var accept = HaulLogParser.ParseContractAccepted(raw);
        if (accept is not null && _byId.TryGetValue(accept.MissionId, out var ah))
        {
            ah.RouteTitle = accept.Title;
            TryApplyPending(ah);
            ah.PickupName = DerivePickup(accept.Title);
            RaiseChanged();
            return;
        }

        var completed = HaulLogParser.ParseObjectiveCompleted(raw);
        if (completed is not null && _byId.TryGetValue(completed.MissionId, out var ch))
        {
            var leg = ch.LegByObjective(completed.ObjectiveId);
            if (leg is not null)
            {
                // Pickup ObjectiveCompleted often fires on accept when you are already at the
                // source. That is not cargo collected, so remaining work stays on Collect until
                // Task Complete (or a real dropoff complete).
                if (leg.Role == HaulRole.Pickup)
                {
                    Logger.Info($"[HAUL] pickup objective completed in log (ignored for remaining work): {ch.Company}");
                    return;
                }
                leg.Completed = true;
                Logger.Info($"[HAUL] leg complete: {ch.Company} {leg.Role} {leg.Commodity}");
                RaiseChanged();
            }
            return;
        }

        var end = HaulLogParser.ParseEndMission(raw);
        if (end is not null && _byId.TryGetValue(end.MissionId, out var eh))
        {
            eh.Outcome = end.Outcome;
            Logger.Info($"[HAUL] mission ended: {eh.Company} {eh.Topology} -> {end.Outcome}");
            HaulEnded?.Invoke(eh);
            RaiseChanged();
        }
    }

    private Haul GetOrCreate(string missionId)
    {
        if (_byId.TryGetValue(missionId, out var h)) return h;
        h = new Haul { MissionId = missionId };
        _byId[missionId] = h;
        _order.Add(h);
        return h;
    }

    private void ApplyMarker(MarkerInfo m)
    {
        var existed = _byId.ContainsKey(m.MissionId);
        var h = GetOrCreate(m.MissionId);
        h.Company = HaulLogParser.CompanyDisplay(m.Generator);
        h.ContractName = m.Contract;
        h.Topology = HaulLogParser.ParseTopology(m.Contract);
        // Container cap from the datamined contract data (primary source, keyed by the exact contract
        // token the log records). OCR is only a fallback when a contract is not in the datamined map.
        var minedCap = ContractCapCatalog.Instance.Lookup(m.Contract);
        if (minedCap.HasValue) h.ContainerCap = minedCap;
        if (h.LegByObjective(m.ObjectiveId) is null)
            h.Legs.Add(new HaulLeg
            {
                ObjectiveId = m.ObjectiveId, CargoKey = m.CargoKey, Role = m.Role,
                LegIndex = m.LegIndex,
            });
        if (!existed) Logger.Info($"[HAUL] mission accepted: {h.Company} {h.Topology}");
        TryApplyPending(h);   // the haul's company is now known; apply any contract scanned before it appeared
        RaiseChanged();
    }

    private void ApplyDeliver(DeliverInfo d)
    {
        var h = GetOrCreate(d.MissionId);
        var leg = h.LegByObjective(d.ObjectiveId);
        if (leg is null)
        {
            leg = new HaulLeg { ObjectiveId = d.ObjectiveId, Role = HaulRole.Dropoff };
            h.Legs.Add(leg);
        }
        leg.Commodity = d.Commodity;
        leg.TargetScu = d.TargetScu;
        leg.Destination = d.Destination;
        RaiseChanged();
    }

    // Best-effort: the left side of an "A > B" route title is the pickup location. Flavor titles
    // ("Red Wind Seeking New Haulers") have no route, so pickup name stays empty (handled in consolidation).
    private static string DerivePickup(string title)
    {
        var idx = title.IndexOf('>');
        if (idx <= 0) return "";
        var left = title[..idx];
        var bar = left.LastIndexOf('|');
        if (bar >= 0) left = left[(bar + 1)..];
        return left.Trim();
    }

    /// <summary>Attach OCR'd contract detail to the matching active haul. Matching the OCR title to the
    /// log title is hopeless (the panel's OCR reading order scrambles it), so we join on the contractor:
    /// the OCR org ("Ling Family Hauling") contains the log company ("Ling Family"). When several active
    /// hauls share a contractor (e.g. two Red Wind hauls), disambiguate by cargo - the log gives each
    /// haul its commodity/SCU. If cargo still can't tell them apart, skip rather than mis-tag.</summary>
    public void ApplyContractDetails(ContractDetails d)
    {
        var org = ContractParser.NormalizeTitle(d.ContractedBy);
        if (org.Length == 0) return;   // no contractor -> no reliable join key

        var candidates = _order.FindAll(h => h.IsActive && h.Company.Length > 0
                                             && org.Contains(ContractParser.NormalizeTitle(h.Company)));
        if (candidates.Count == 0) { _pendingByOrg[org] = d; return; }   // haul not accepted yet; applied later

        var target = candidates.Count == 1 ? candidates[0] : DisambiguateByCargo(candidates, d);
        if (target is null) return;   // ambiguous and cargo can't separate them -> skip

        ApplyAndNotify(target, d);
        RaiseChanged();
    }

    // Several active hauls share a contractor (e.g. three Red Wind hauls). Place the scanned contract:
    //   1) on the haul whose LOG legs carry this commodity (strongest, when the log knows it);
    //   2) else on a haul ALREADY paired with this same cargo, so re-scans update it instead of duplicating;
    //   3) else - the log can't tell these hauls apart (no commodity on their legs) - on the first not-yet-
    //      paired one. They are interchangeable in the log, so each scanned contract still lands accurately.
    private static Haul? DisambiguateByCargo(List<Haul> candidates, ContractDetails d)
    {
        if (d.Objectives.Count == 0) return null;   // nothing to place these indistinguishable hauls by

        var byCommodity = candidates.FindAll(h => d.Objectives.TrueForAll(o => HasDropoff(h, o, matchScu: false)));
        if (byCommodity.Count == 1) return byCommodity[0];
        if (byCommodity.Count > 1)
        {
            var byScu = byCommodity.FindAll(h => d.Objectives.TrueForAll(o => HasDropoff(h, o, matchScu: true)));
            if (byScu.Count == 1) return byScu[0];
        }

        var already = candidates.Find(h => SameCargo(h.ContractObjectives, d.Objectives));
        if (already is not null) return already;

        return candidates.Find(h => h.ContractObjectives.Count == 0);
    }

    private static bool HasDropoff(Haul h, ContractObjective o, bool matchScu)
    {
        var commodity = ContractParser.NormalizeTitle(o.Commodity);
        if (commodity.Length == 0) return false;
        return h.Legs.Exists(l => l.Role == HaulRole.Dropoff
            && ContractParser.NormalizeTitle(l.Commodity) == commodity
            && (!matchScu || o.Scu == 0 || l.TargetScu == 0 || l.TargetScu == o.Scu));
    }

    // Two OCR objective sets describe the same cargo when they list the same commodities.
    private static bool SameCargo(List<ContractObjective> have, List<ContractObjective> scanned)
    {
        if (have.Count == 0 || have.Count != scanned.Count) return false;
        return scanned.TrueForAll(o => have.Exists(x =>
            ContractParser.NormalizeTitle(x.Commodity) == ContractParser.NormalizeTitle(o.Commodity)));
    }

    // Enrich, and raise ContractPaired exactly once per haul (on the transition to having detail), so the
    // continuous scanner's repeated re-enriches don't spam the toast/indicator.
    private void ApplyAndNotify(Haul h, ContractDetails d)
    {
        bool wasEnriched = h.Reward > 0 || h.ContractObjectives.Count > 0;
        Enrich(h, d);
        if (!wasEnriched && (h.Reward > 0 || h.ContractObjectives.Count > 0))
        {
            Logger.Info($"[CONTRACT] paired {h.Company} reward {h.Reward}");
            ContractPaired?.Invoke(h);
        }
    }

    // Defensive: never clobber a known value with an empty/zero one from a noisier later scan.
    private static void Enrich(Haul h, ContractDetails d)
    {
        if (d.Reward > 0) h.Reward = d.Reward;
        if (!string.IsNullOrWhiteSpace(d.ContractedBy)) h.ContractedBy = d.ContractedBy;
        if (d.Objectives.Count > 0)
        {
            var old = h.ContractObjectives;
            foreach (var n in d.Objectives)
            {
                var prev = old.Find(o => SamePlace(o.Pickup, n.Pickup)
                    && SamePlace(o.Dropoff, n.Dropoff)
                    && CommodityMatches(o.Commodity, n.Commodity));
                if (prev is null) continue;
                n.PickupCompleted = prev.PickupCompleted;
                n.DropoffCompleted = prev.DropoffCompleted;
                n.PickupRemainingScu = prev.PickupRemainingScu;
                n.DropoffRemainingScu = prev.DropoffRemainingScu;
            }
            h.ContractObjectives = d.Objectives;
        }
        if (d.ContainerCap.HasValue && !h.ContainerCap.HasValue) h.ContainerCap = d.ContainerCap;  // OCR fills only when the datamined map has no entry
    }

    private bool ApplyStopCompleted(string missionId, HaulRole role, string? location, string? commodity, bool completed)
    {
        if (!_byId.TryGetValue(missionId, out var h) || !h.IsActive) return false;
        var changed = false;

        foreach (var o in h.ContractObjectives)
        {
            if (role == HaulRole.Pickup)
            {
                if (string.IsNullOrWhiteSpace(o.Pickup)) continue;
                if (!string.IsNullOrWhiteSpace(location) && !LocationEq(o.Pickup, location)) continue;
                if (!CommodityMatches(commodity, o.Commodity)) continue;
                var remaining = completed ? 0 : (int?)null;
                if (o.PickupCompleted == completed && o.PickupRemainingScu == remaining) continue;
                o.PickupCompleted = completed;
                o.PickupRemainingScu = remaining;
                changed = true;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(o.Dropoff)) continue;
                if (!string.IsNullOrWhiteSpace(location) && !LocationEq(o.Dropoff, location)) continue;
                if (!CommodityMatches(commodity, o.Commodity)) continue;
                var remaining = completed ? 0 : (int?)null;
                if (o.DropoffCompleted == completed && o.DropoffRemainingScu == remaining) continue;
                o.DropoffCompleted = completed;
                o.DropoffRemainingScu = remaining;
                changed = true;
            }
        }

        foreach (var leg in h.Legs)
        {
            if (leg.Role != role) continue;
            if (!LegMatchesStop(h, leg, location, commodity)) continue;
            var remaining = completed ? 0 : (int?)null;
            if (leg.Completed == completed && leg.RemainingScu == remaining) continue;
            leg.Completed = completed;
            leg.RemainingScu = remaining;
            changed = true;
        }

        if (!changed) return false;
        var verb = completed ? "marked" : "reopened";
        var what = string.IsNullOrWhiteSpace(commodity) ? "" : $" {commodity.Trim()}";
        var where = string.IsNullOrWhiteSpace(location) ? "" : $" @ {location.Trim()}";
        Logger.Info($"[HAUL] user {verb} {role}{what}{where}: {h.Company}");
        return true;
    }

    private bool ApplyStopProgress(string missionId, HaulRole role, string? location, string? commodity, int amount)
    {
        if (amount <= 0) return false;
        if (!_byId.TryGetValue(missionId, out var h) || !h.IsActive) return false;
        var changed = false;

        foreach (var o in h.ContractObjectives)
        {
            if (role == HaulRole.Pickup)
            {
                if (string.IsNullOrWhiteSpace(o.Pickup)) continue;
                if (!string.IsNullOrWhiteSpace(location) && !LocationEq(o.Pickup, location)) continue;
                if (!CommodityMatches(commodity, o.Commodity)) continue;
                var now = o.PickupRemaining;
                if (now <= 0) continue;
                var next = Math.Max(0, now - amount);
                o.PickupRemainingScu = next;
                o.PickupCompleted = next == 0;
                changed = true;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(o.Dropoff)) continue;
                if (!string.IsNullOrWhiteSpace(location) && !LocationEq(o.Dropoff, location)) continue;
                if (!CommodityMatches(commodity, o.Commodity)) continue;
                var now = o.DropoffRemaining;
                if (now <= 0) continue;
                var next = Math.Max(0, now - amount);
                o.DropoffRemainingScu = next;
                o.DropoffCompleted = next == 0;
                changed = true;
            }
        }

        foreach (var leg in h.Legs)
        {
            if (leg.Role != role) continue;
            if (!LegMatchesStop(h, leg, location, commodity)) continue;
            var sib = h.Legs.Find(l => l.Role == HaulRole.Dropoff && l.CargoKey == leg.CargoKey);
            var now = role == HaulRole.Pickup ? PickupLegRemaining(leg, sib) : LegRemaining(leg);
            if (now <= 0) continue;
            var next = Math.Max(0, now - amount);
            leg.RemainingScu = next;
            leg.Completed = next == 0;
            changed = true;
        }

        if (!changed) return false;
        var what = string.IsNullOrWhiteSpace(commodity) ? "" : $" {commodity.Trim()}";
        var where = string.IsNullOrWhiteSpace(location) ? "" : $" @ {location.Trim()}";
        Logger.Info($"[HAUL] user progressed {role}{what}{where} by {amount} SCU: {h.Company}");
        return true;
    }

    internal static int? PlannedSlice(
        GameRoutePlan? route, string missionId, HaulRole role, string? location, string? commodity)
    {
        if (route is null || !route.HasStops) return null;
        var kind = role == HaulRole.Pickup ? GameRouteActionKind.Pickup : GameRouteActionKind.Delivery;
        foreach (var stop in route.Stops)
        {
            if (!string.IsNullOrWhiteSpace(location) && !LocationEq(stop.Location.Label, location)) continue;
            foreach (var action in stop.Actions)
            {
                if (action.Kind != kind) continue;
                if (!string.Equals(action.ObjectiveRef, missionId, StringComparison.OrdinalIgnoreCase)) continue;
                if (!CommodityMatches(commodity, action.Commodity)) continue;
                if (action.RemainingScu > 0) return action.RemainingScu;
            }
        }

        return null;
    }

    private static int LegRemaining(HaulLeg leg)
        => leg.Completed ? 0 : leg.RemainingScu ?? leg.TargetScu;

    private static int PickupLegRemaining(HaulLeg pickup, HaulLeg? sibling)
    {
        if (pickup.Completed) return 0;
        if (pickup.RemainingScu is int remaining) return remaining;
        if (sibling is null) return 0;
        return LegRemaining(sibling);
    }

    private static bool LegMatchesStop(Haul h, HaulLeg leg, string? location, string? commodity)
    {
        if (leg.Role == HaulRole.Dropoff)
        {
            if (!CommodityMatches(commodity, leg.Commodity)) return false;
            if (string.IsNullOrWhiteSpace(location)) return true;
            return LocationEq(leg.Destination, location);
        }

        var sib = h.Legs.Find(l => l.Role == HaulRole.Dropoff && l.CargoKey == leg.CargoKey);
        if (!CommodityMatches(commodity, sib?.Commodity ?? leg.Commodity)) return false;
        if (string.IsNullOrWhiteSpace(location) || IsTbdPickup(location)) return true;
        if (!string.IsNullOrWhiteSpace(h.PickupName)) return LocationEq(h.PickupName, location);
        // 2-to-1 OCR pickups share no log destination; leave those legs to the OCR flags.
        return h.Legs.FindAll(l => l.Role == HaulRole.Pickup).Count == 1;
    }

    private static bool DropoffLegMatchesObjective(HaulLeg leg, ContractObjective o)
    {
        if (!CommodityMatches(o.Commodity, leg.Commodity)) return false;
        if (string.IsNullOrWhiteSpace(o.Dropoff) || string.IsNullOrWhiteSpace(leg.Destination)) return true;
        return LocationEq(leg.Destination, o.Dropoff);
    }

    private static bool LocationEq(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
        return string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool SamePlace(string? a, string? b)
        => string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool CommodityMatches(string? filter, string? value)
    {
        if (string.IsNullOrWhiteSpace(filter)) return true;
        if (string.IsNullOrWhiteSpace(value)) return false;
        return string.Equals(filter.Trim(), value.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTbdPickup(string? location)
        => string.Equals((location ?? "").Trim(), "Pickup (TBD)", StringComparison.OrdinalIgnoreCase);

    // When a haul first appears (its company becomes known), apply any contract scanned before it existed.
    private void TryApplyPending(Haul h)
    {
        if (_pendingByOrg.Count == 0 || h.Company.Length == 0) return;
        var comp = ContractParser.NormalizeTitle(h.Company);
        string? hit = null;
        foreach (var k in _pendingByOrg.Keys) if (k.Contains(comp)) { hit = k; break; }
        if (hit != null) { ApplyAndNotify(h, _pendingByOrg[hit]); _pendingByOrg.Remove(hit); }
    }

    private void RaiseChanged()
    {
        PublishGameStateHauling();
        Changed?.Invoke();
    }

    private void PublishGameStateHauling()
    {
        if (GameState is null) return;
        if (_order.Count == 0)
        {
            GameState.PublishHauling(GameHaulingState.Empty);
            RouteSync.PublishFromHauling(GameState);
            return;
        }

        var hauls = new GameHaulSummary[_order.Count];
        for (int i = 0; i < _order.Count; i++)
        {
            var h = _order[i];
            hauls[i] = new GameHaulSummary(h.MissionId, h.Company, h.IsActive, h.Outcome, h.ContainerCap);
        }

        var con = BuildConsolidation();
        GameState.PublishHauling(new GameHaulingState(
            hauls,
            FlattenStops(con.Pickups),
            FlattenStops(con.Dropoffs)));
        RouteSync.PublishFromHauling(GameState);
    }

    private static GameHaulStop[] FlattenStops(List<ConsolidationStop> stops)
    {
        var count = 0;
        foreach (var stop in stops) count += stop.Items.Count;
        if (count == 0) return Array.Empty<GameHaulStop>();

        var items = new GameHaulStop[count];
        var i = 0;
        foreach (var stop in stops)
        {
            foreach (var item in stop.Items)
                items[i++] = new GameHaulStop(stop.Location, item.Commodity, item.Scu, item.MissionId);
        }
        return items;
    }

    public void Dispose()
    {
        _sub?.Dispose();
        _sub = null;
        if (_ownsFeed) _feed.Dispose();   // a shared feed is the app's to dispose, not ours
    }
}
