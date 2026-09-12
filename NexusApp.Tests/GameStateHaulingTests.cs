using NexusApp.Models;
using NexusApp.Services;
using Xunit;

namespace NexusApp.Tests;

public class GameStateHaulingTests
{
    private const string Mid = "a6c598e0-e8d2-4613-a7a3-e80f4c30d9c6";

    private static GameLogEntry E(string raw) => new() { Raw = raw, Category = LogCategory.Other };

    private static string Join(string shard) =>
        $"<2026-06-27T13:14:51.882Z> [Notice] <Join PU> address[10.0.0.1] port[64318] shard[{shard}] locationId[1] [x]";

    private static string EndSession() =>
        "<2026-06-27T14:30:51.596Z> [Notice] <CDisciplineServiceExternal::EndSession> Ending session [AntiCheat][EAC]";

    private static string LocationLine(string place, string timestamp) =>
        $"<{timestamp}> [Notice] <RequestLocationInventory> Player[TestPilot] requested inventory " +
        $"for Location[{place}] [Team_CoreGameplayFeatures][Inventory]";

    private const string EndComplete =
        "<2026-06-27T14:40:00.000Z> [Notice] <EndMission> Ending mission for player. " +
        "MissionId[a6c598e0-e8d2-4613-a7a3-e80f4c30d9c6] Player[REDACTED] PlayerId[REDACTED] " +
        "CompletionType[Complete] Reason[Mission Ended] [Team_MissionFeatures][Missions]";

    [Fact]
    public void NewState_StartsWithHonestEmptyHauling()
    {
        var state = new GameState();

        Assert.Equal(GameHaulingState.Empty, state.Hauling);
        Assert.False(state.Hauling.HasActiveHauls);
        Assert.Empty(state.Hauling.Hauls);
        Assert.Empty(state.Hauling.Pickups);
        Assert.Empty(state.Hauling.Dropoffs);
    }

    [Fact]
    public void PublishHauling_ReplacesImmutableSnapshot_AndNotifiesOnce()
    {
        var state = new GameState();
        var snapshot = new GameHaulingState(
            new[] { new GameHaulSummary(Mid, "Red Wind", true, HaulOutcome.Active) },
            Array.Empty<GameHaulStop>(),
            new[] { new GameHaulStop("Jackson's Swap", "Carbon", 158, Mid) });
        int changed = 0;
        int haulingChanged = 0;
        state.Changed += () => changed++;
        state.HaulingChanged += () => haulingChanged++;

        state.PublishHauling(snapshot);
        state.PublishHauling(new GameHaulingState(
            new[] { new GameHaulSummary(Mid, "Red Wind", true, HaulOutcome.Active) },
            Array.Empty<GameHaulStop>(),
            new[] { new GameHaulStop("Jackson's Swap", "Carbon", 158, Mid) }));

        Assert.Equal(1, changed);
        Assert.Equal(1, haulingChanged);
        Assert.True(state.Hauling.HasActiveHauls);
    }

    [Fact]
    public void HaulTracker_PublishesActiveHaulAndDropoffStop()
    {
        var state = new GameState();
        using var tracker = new HaulTracker(gameState: state);

        tracker.Ingest(E(HaulLogParserFixtures.MarkerDropoff));
        tracker.Ingest(E(HaulLogParserFixtures.DeliverLine));

        var haul = Assert.Single(state.Hauling.Hauls);
        Assert.Equal(Mid, haul.MissionId);
        Assert.Equal("Red Wind", haul.Company);
        Assert.True(haul.IsActive);
        var stop = Assert.Single(state.Hauling.Dropoffs);
        Assert.Equal("Jackson's Swap", stop.Location);
        Assert.Equal("Carbon", stop.Commodity);
        Assert.Equal(158, stop.Scu);
        Assert.Equal(Mid, stop.MissionId);
    }

    [Fact]
    public void DuplicateMarker_DoesNotNotifyGameStateTwice()
    {
        var state = new GameState();
        using var tracker = new HaulTracker(gameState: state);
        tracker.Ingest(E(HaulLogParserFixtures.MarkerPickup));
        int haulingChanged = 0;
        state.HaulingChanged += () => haulingChanged++;

        tracker.Ingest(E(HaulLogParserFixtures.MarkerPickup));

        Assert.Equal(0, haulingChanged);
        Assert.Single(state.Hauling.Hauls);
    }

    [Fact]
    public void EndMission_KeepsFinishedHaulWithoutActiveStops()
    {
        var state = new GameState();
        using var tracker = new HaulTracker(gameState: state);

        tracker.Ingest(E(HaulLogParserFixtures.MarkerDropoff));
        tracker.Ingest(E(HaulLogParserFixtures.DeliverLine));
        tracker.Ingest(E(EndComplete));

        var haul = Assert.Single(state.Hauling.Hauls);
        Assert.False(haul.IsActive);
        Assert.Equal(HaulOutcome.Complete, haul.Outcome);
        Assert.False(state.Hauling.HasActiveHauls);
        Assert.Empty(state.Hauling.Dropoffs);
    }

    [Fact]
    public void LogReset_ClearsHauling_WithoutClearingLocationOrSessionGenerationOnly()
    {
        var state = new GameState();
        using var feed = new GameLogFeed(state);
        using var locations = new LocationTracker(feed, state);
        using var hauls = new HaulTracker(feed, state);
        locations.Ingest(E(LocationLine("Stanton4_NewBabbage", "2026-09-12T12:01:00.000Z")));
        hauls.Ingest(E(HaulLogParserFixtures.MarkerPickup));
        int locationChanged = 0;
        state.LocationChanged += () => locationChanged++;

        feed.HandleLogReset();

        Assert.Equal(GameHaulingState.Empty, state.Hauling);
        Assert.Empty(hauls.AllHauls);
        Assert.Equal("New Babbage", state.Location.Label);
        Assert.Equal(1, state.Session.LogGeneration);
        Assert.Equal(0, locationChanged);
    }

    [Fact]
    public void ShardChange_ClearsHauling_WithoutClearingLocation()
    {
        var state = new GameState();
        using var feed = new GameLogFeed(state);
        using var locations = new LocationTracker(feed, state);
        using var hauls = new HaulTracker(feed, state);
        locations.Ingest(E(LocationLine("Stanton4_NewBabbage", "2026-09-12T12:01:00.000Z")));
        hauls.Ingest(E(Join("pub_use1b_12030094_140")));
        hauls.Ingest(E(HaulLogParserFixtures.MarkerPickup));

        hauls.Ingest(E(Join("pub_use1b_12030094_150")));

        Assert.Equal(GameHaulingState.Empty, state.Hauling);
        Assert.Empty(hauls.AllHauls);
        Assert.Equal("New Babbage", state.Location.Label);
    }

    [Fact]
    public void ShardExit_ClearsHauling()
    {
        var state = new GameState();
        using var tracker = new HaulTracker(gameState: state);
        tracker.Ingest(E(Join("pub_use1b_12030094_140")));
        tracker.Ingest(E(HaulLogParserFixtures.MarkerPickup));

        tracker.Ingest(E(EndSession()));

        Assert.Equal(GameHaulingState.Empty, state.Hauling);
        Assert.Empty(tracker.AllHauls);
    }

    [Fact]
    public void SameShardRejoin_KeepsHauling()
    {
        var state = new GameState();
        using var tracker = new HaulTracker(gameState: state);
        tracker.Ingest(E(Join("pub_use1b_12030094_140")));
        tracker.Ingest(E(HaulLogParserFixtures.MarkerPickup));

        tracker.Ingest(E(Join("pub_use1b_12030094_140")));

        var haul = Assert.Single(state.Hauling.Hauls);
        Assert.Equal(Mid, haul.MissionId);
        Assert.True(haul.IsActive);
    }

    [Fact]
    public void ConsolidationPickup_IsProjectedIntoGameState()
    {
        var state = new GameState();
        using var tracker = new HaulTracker(gameState: state);
        tracker.Ingest(E(HaulLogParserFixtures.MarkerPickup));
        tracker.Ingest(E(HaulLogParserFixtures.MarkerDropoff));
        tracker.Ingest(E(HaulLogParserFixtures.DeliverLine));
        tracker.Ingest(E(HaulLogParserFixtures.AcceptRouteLine));

        var pickup = Assert.Single(state.Hauling.Pickups);
        Assert.Equal("Ruin Station", pickup.Location);
        Assert.Equal(158, pickup.Scu);
        Assert.Equal("Carbon", pickup.Commodity);
    }
}
