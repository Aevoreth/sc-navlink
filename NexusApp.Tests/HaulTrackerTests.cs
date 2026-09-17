using NexusApp.Models;
using NexusApp.Services;
using Xunit;

namespace NexusApp.Tests;

// Headless: feed real (redacted) lines straight into Ingest, no watcher tick, no real file.
public class HaulTrackerTests
{
    private const string Mid = "a6c598e0-e8d2-4613-a7a3-e80f4c30d9c6";

    private static GameLogEntry E(string raw) => new() { Raw = raw, Category = LogCategory.Other };

    private static readonly string MarkerPickup = HaulLogParserFixtures.MarkerPickup;
    private static readonly string MarkerDropoff = HaulLogParserFixtures.MarkerDropoff;
    private static readonly string DeliverLine = HaulLogParserFixtures.DeliverLine;
    private static readonly string PickupCompleted = HaulLogParserFixtures.ObjectiveCompletedLine;
    private const string EndComplete =
        "<2026-06-27T14:40:00.000Z> [Notice] <EndMission> Ending mission for player. " +
        "MissionId[a6c598e0-e8d2-4613-a7a3-e80f4c30d9c6] Player[REDACTED] PlayerId[REDACTED] " +
        "CompletionType[Complete] Reason[Mission Ended] [Team_MissionFeatures][Missions]";

    [Fact]
    public void Marker_CreatesHaul_WithCompanyAndTopologyAndLeg()
    {
        var t = new HaulTracker();
        t.Ingest(E(MarkerPickup));
        var h = Assert.Single(t.ActiveHauls);
        Assert.Equal(Mid, h.MissionId);
        Assert.Equal("Red Wind", h.Company);
        Assert.Equal("1 to 1", h.Topology);
        Assert.Single(h.Legs);
        Assert.Equal(HaulRole.Pickup, h.Legs[0].Role);
    }

    [Fact]
    public void DuplicateMarker_DoesNotDuplicateLeg()
    {
        var t = new HaulTracker();
        t.Ingest(E(MarkerPickup));
        t.Ingest(E(MarkerPickup));   // re-emitted on zone reload
        Assert.Single(t.ActiveHauls[0].Legs);
    }

    [Fact]
    public void Deliver_EnrichesDropoffLeg_WithCommodityScuDestination()
    {
        var t = new HaulTracker();
        t.Ingest(E(MarkerDropoff));
        t.Ingest(E(DeliverLine));
        var leg = t.ActiveHauls[0].Legs[0];
        Assert.Equal("Carbon", leg.Commodity);
        Assert.Equal(158, leg.TargetScu);
        Assert.Equal("Jackson's Swap", leg.Destination);
    }

    [Fact]
    public void ObjectiveCompleted_Pickup_DoesNotAdvanceRemainingWork()
    {
        var t = new HaulTracker();
        t.Ingest(E(MarkerPickup));
        t.Ingest(E(PickupCompleted));
        Assert.False(t.ActiveHauls[0].Legs[0].Completed);
        Assert.Single(t.BuildConsolidation().Pickups);
    }

    [Fact]
    public void ObjectiveCompleted_Dropoff_MarksLegDone()
    {
        var t = new HaulTracker();
        t.Ingest(E(MarkerDropoff));
        t.Ingest(E(DeliverLine));
        t.Ingest(E(PickupCompleted.Replace("pickup_6e9335e8-f62e-4eca-8fc5-a42ab1c3ab7d_0",
                                           "dropoff_6e9335e8-f62e-4eca-8fc5-a42ab1c3ab7d_0")));
        Assert.True(t.ActiveHauls[0].Legs[0].Completed);
        Assert.Empty(t.BuildConsolidation().Dropoffs);
    }

    [Fact]
    public void EndMission_MovesHaulToFinished_WithOutcome()
    {
        var t = new HaulTracker();
        t.Ingest(E(MarkerPickup));
        t.Ingest(E(EndComplete));
        Assert.Empty(t.ActiveHauls);
        var h = Assert.Single(t.FinishedHauls);
        Assert.Equal(HaulOutcome.Complete, h.Outcome);
    }

    [Fact]
    public void EndMission_ForUnknownNonHaulMission_Ignored()
    {
        var t = new HaulTracker();
        t.Ingest(E(EndComplete.Replace(Mid, "ffffffff-0000-0000-0000-000000000000")));
        Assert.Empty(t.AllHauls);
    }

    [Fact]
    public void Reset_ClearsEverything()
    {
        var t = new HaulTracker();
        t.Ingest(E(MarkerPickup));
        t.Reset();
        Assert.Empty(t.AllHauls);
    }

    [Fact]
    public void ChangedEvent_FiresOnNewHaul()
    {
        var t = new HaulTracker();
        int fired = 0;
        t.Changed += () => fired++;
        t.Ingest(E(MarkerPickup));
        Assert.True(fired >= 1);
    }

    [Fact]
    public void Consolidation_GroupsActiveDropoffsByDestination()
    {
        var t = new HaulTracker();
        t.Ingest(E(MarkerDropoff));
        t.Ingest(E(DeliverLine));   // Carbon 158 -> Jackson's Swap
        var con = t.BuildConsolidation();
        var stop = Assert.Single(con.Dropoffs);
        Assert.Equal("Jackson's Swap", stop.Location);
        Assert.Equal(158, stop.TotalScu);
        Assert.Equal("Carbon", stop.Items[0].Commodity);
    }

    [Fact]
    public void Consolidation_PickupBorrowsCommodityFromSiblingDropoff()
    {
        var t = new HaulTracker();
        t.Ingest(E(MarkerPickup));    // pickup, CargoKey 6e9335e8-..._0
        t.Ingest(E(MarkerDropoff));   // dropoff, same CargoKey
        t.Ingest(E(DeliverLine));     // commodity/SCU land on the dropoff leg
        t.Ingest(E(HaulLogParserFixtures.AcceptRouteLine)); // route -> pickup name "Ruin Station"
        var con = t.BuildConsolidation();
        var pickup = Assert.Single(con.Pickups);
        Assert.Equal("Ruin Station", pickup.Location);
        Assert.Equal(158, pickup.TotalScu);   // borrowed from the sibling dropoff
    }

    [Fact]
    public void Consolidation_ExcludesCompletedLegs()
    {
        var t = new HaulTracker();
        t.Ingest(E(MarkerDropoff));
        t.Ingest(E(DeliverLine));
        // complete the dropoff
        t.Ingest(E(PickupCompleted.Replace("pickup_6e9335e8-f62e-4eca-8fc5-a42ab1c3ab7d_0",
                                           "dropoff_6e9335e8-f62e-4eca-8fc5-a42ab1c3ab7d_0")));
        Assert.Empty(t.BuildConsolidation().Dropoffs);
    }

    private static string Join(string shard) =>
        $"<2026-06-27T13:14:51.882Z> [Notice] <Join PU> address[10.0.0.1] port[64318] shard[{shard}] locationId[1] [x]";

    // EAC ends the session when the player leaves the PU (menu / quit / disconnect).
    private const string EndSession =
        "<2026-06-27T14:30:51.596Z> [Notice] <CDisciplineServiceExternal::EndSession> Ending session [AntiCheat][EAC]";

    [Fact]
    public void ShardExit_ClearsAllHauls()
    {
        var t = new HaulTracker();
        t.Ingest(E(Join("pub_use1b_12030094_140")));   // on a shard
        t.Ingest(E(MarkerPickup));                      // with a haul
        t.Ingest(E(EndSession));                        // leave the shard/server -> contracts abandoned
        Assert.Empty(t.AllHauls);
    }

    [Fact]
    public void ShardChange_ClearsAllHauls()
    {
        var t = new HaulTracker();
        t.Ingest(E(Join("pub_use1b_12030094_140")));   // initial shard
        t.Ingest(E(MarkerPickup));                      // a haul on shard 140
        t.Ingest(E(Join("pub_use1b_12030094_150")));    // hop to a new shard -> stale hauls clear
        Assert.Empty(t.AllHauls);
    }

    [Fact]
    public void SameShardRejoin_DoesNotClear()
    {
        var t = new HaulTracker();
        t.Ingest(E(Join("pub_use1b_12030094_140")));
        t.Ingest(E(MarkerPickup));
        t.Ingest(E(Join("pub_use1b_12030094_140")));    // reconnect to the same shard -> keep hauls
        Assert.Single(t.AllHauls);
    }

    [Fact]
    public void ClearAll_RemovesEverything()
    {
        var t = new HaulTracker();
        t.Ingest(E(MarkerPickup));
        t.ClearAll();
        Assert.Empty(t.AllHauls);
    }

    [Fact]
    public void Remove_DeletesOnlyThatMission()
    {
        var t = new HaulTracker();
        t.Ingest(E(MarkerPickup));                            // mission a6c598e0...
        t.Ingest(E(HaulLogParserFixtures.CfpMarkerPickup));   // mission 6b4e3396...
        t.Remove(Mid);                                        // remove a6c598e0...
        var h = Assert.Single(t.AllHauls);
        Assert.Equal("6b4e3396-1506-4051-b348-79c4eabae9d9", h.MissionId);
    }

    private static ContractDetails RedWindCarbonOcr() => ContractParser.Parse(
        "PRIMARY OBJECTIVES x Ä 139,250 N/A Red Wind Linehaul [50/200 Rep] Need a Hauler DETAILS " +
        "O Deliver 0/158 SCU of Carbon to Jackson's Swap. o Collect Carbon from Ruin Station. TRACK")!;

    private void SeedCarbonHaul(HaulTracker t)
    {
        t.Ingest(E(MarkerPickup));
        t.Ingest(E(MarkerDropoff));
        t.Ingest(E(DeliverLine));
        t.Ingest(E(HaulLogParserFixtures.AcceptRouteLine));
    }

    [Fact]
    public void SetStopCompleted_UnknownMission_ReturnsFalse()
    {
        var t = new HaulTracker();
        SeedCarbonHaul(t);
        Assert.False(t.SetStopCompleted("ffffffff-0000-0000-0000-000000000000",
            HaulRole.Pickup, "Ruin Station", "Carbon", true));
        Assert.Single(t.BuildConsolidation().Pickups);
    }

    [Fact]
    public void SetStopCompleted_Pickup_LeavesDropoffRemaining()
    {
        var t = new HaulTracker();
        SeedCarbonHaul(t);
        Assert.True(t.SetStopCompleted(Mid, HaulRole.Pickup, "Ruin Station", "Carbon", true));

        Assert.Empty(t.BuildConsolidation().Pickups);
        var drop = Assert.Single(t.BuildConsolidation().Dropoffs);
        Assert.Equal("Jackson's Swap", drop.Location);
        Assert.True(t.ActiveHauls[0].Legs.Find(l => l.Role == HaulRole.Pickup)!.Completed);
        Assert.False(t.ActiveHauls[0].Legs.Find(l => l.Role == HaulRole.Dropoff)!.Completed);
    }

    [Fact]
    public void SetStopCompleted_CanReopenPickup()
    {
        var t = new HaulTracker();
        SeedCarbonHaul(t);
        Assert.True(t.SetStopCompleted(Mid, HaulRole.Pickup, "Ruin Station", "Carbon", true));
        Assert.True(t.SetStopCompleted(Mid, HaulRole.Pickup, "Ruin Station", "Carbon", false));
        Assert.Single(t.BuildConsolidation().Pickups);
        Assert.False(t.ActiveHauls[0].Legs.Find(l => l.Role == HaulRole.Pickup)!.Completed);
    }

    [Fact]
    public void Consolidation_OcrPath_IgnoresLogPickupComplete()
    {
        var t = new HaulTracker();
        SeedCarbonHaul(t);
        t.ApplyContractDetails(RedWindCarbonOcr());
        Assert.Single(t.BuildConsolidation().Pickups);

        t.Ingest(E(PickupCompleted));

        var pickup = Assert.Single(t.BuildConsolidation().Pickups);
        Assert.Equal("Ruin Station", pickup.Location);
        Assert.Single(t.BuildConsolidation().Dropoffs);
    }

    [Fact]
    public void Consolidation_OcrMissingCollect_FallsBackToLogPickup()
    {
        var t = new HaulTracker();
        SeedCarbonHaul(t);
        t.ApplyContractDetails(ContractParser.Parse(
            "PRIMARY OBJECTIVES x Ä 139,250 N/A Red Wind Linehaul Deliver 0/158 SCU of Carbon to Jackson's Swap.")!);

        var pickup = Assert.Single(t.BuildConsolidation().Pickups);
        Assert.Equal("Ruin Station", pickup.Location);
        Assert.Equal("Carbon", pickup.Items[0].Commodity);
        Assert.Single(t.BuildConsolidation().Dropoffs);
    }

    [Fact]
    public void TryCompleteNext_Pickup_AdvancesToDropoff()
    {
        var t = new HaulTracker();
        SeedCarbonHaul(t);
        var next = new NextAction(
            NextModule.Hauling, NextActionKind.HaulPickup, "Collect 158 SCU Carbon",
            "Ruin Station", "Carbon", 158, Mid, 1, 400, "Collect.", NextConfidence.Observed,
            Array.Empty<string>());

        Assert.True(t.TryCompleteNext(next));
        Assert.Empty(t.BuildConsolidation().Pickups);
        Assert.Single(t.BuildConsolidation().Dropoffs);
    }

    [Fact]
    public void SetStopCompleted_OcrPickup_AdvancesWithoutLogComplete()
    {
        var t = new HaulTracker();
        SeedCarbonHaul(t);
        t.ApplyContractDetails(RedWindCarbonOcr());
        Assert.True(t.SetStopCompleted(Mid, HaulRole.Pickup, "Ruin Station", "Carbon", true));

        Assert.True(Assert.Single(t.ActiveHauls[0].ContractObjectives).PickupCompleted);
        Assert.Empty(t.BuildConsolidation().Pickups);
        Assert.Single(t.BuildConsolidation().Dropoffs);
    }

    [Fact]
    public void Enrich_Rescan_PreservesManualPickupComplete()
    {
        var t = new HaulTracker();
        SeedCarbonHaul(t);
        t.ApplyContractDetails(RedWindCarbonOcr());
        t.SetStopCompleted(Mid, HaulRole.Pickup, "Ruin Station", "Carbon", true);
        t.ApplyContractDetails(RedWindCarbonOcr());

        Assert.True(Assert.Single(t.ActiveHauls[0].ContractObjectives).PickupCompleted);
        Assert.Empty(t.BuildConsolidation().Pickups);
    }

    [Fact]
    public void CompletePlannedStop_SubtractsSlice_KeepsRemainingObligation_AndNoCargoLot()
    {
        var state = new GameState();
        using var t = new HaulTracker(gameState: state);
        SeedCarbonHaul(t);
        t.ApplyContractDetails(RedWindCarbonOcr());

        var slice = HaulPlanner.Plan(state.Hauling, HaulComplexity.Advanced, new HaulCapacity(16, 32));
        state.PublishRoute(slice.Plan);
        Assert.Equal(16, HaulPlanner.PlannedPickupScu(state.Route));

        Assert.True(t.CompletePlannedStop(Mid, HaulRole.Pickup, "Ruin Station", "Carbon"));

        var pickup = Assert.Single(state.Hauling.Pickups);
        Assert.Equal(142, pickup.Scu);
        Assert.Equal(142, Assert.Single(t.ActiveHauls[0].ContractObjectives).PickupRemaining);
        Assert.False(t.ActiveHauls[0].ContractObjectives[0].PickupCompleted);
        Assert.Equal(GameCargoState.Empty, state.Cargo);
    }

    [Fact]
    public void SetStopCompleted_Reopen_RestoresFullRemaining()
    {
        var state = new GameState();
        using var t = new HaulTracker(gameState: state);
        SeedCarbonHaul(t);
        t.ApplyContractDetails(RedWindCarbonOcr());
        var slice = HaulPlanner.Plan(state.Hauling, HaulComplexity.Advanced, new HaulCapacity(16, 32));
        state.PublishRoute(slice.Plan);
        t.CompletePlannedStop(Mid, HaulRole.Pickup, "Ruin Station", "Carbon");

        Assert.True(t.SetStopCompleted(Mid, HaulRole.Pickup, "Ruin Station", "Carbon", false));
        Assert.Equal(158, Assert.Single(t.BuildConsolidation().Pickups).TotalScu);
    }
}
