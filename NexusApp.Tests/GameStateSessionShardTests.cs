using NexusApp.Models;
using NexusApp.Services;
using Xunit;

namespace NexusApp.Tests;

public class GameStateSessionShardTests
{
    private static GameLogEntry E(string raw) => new() { Raw = raw, Category = LogCategory.Other };

    private static string Join(string shard) =>
        $"<2026-06-27T13:14:51.882Z> [Notice] <Join PU> address[10.0.0.1] port[64318] shard[{shard}] locationId[1] [x]";

    private static string Leave() =>
        "<2026-06-27T14:30:51.596Z> [Notice] <CDisciplineServiceExternal::EndSession> Ending session [AntiCheat][EAC]";

    private static string LocationLine(string place, string timestamp) =>
        $"<{timestamp}> [Notice] <RequestLocationInventory> Player[TestPilot] requested inventory " +
        $"for Location[{place}] [Team_CoreGameplayFeatures][Inventory]";

    [Fact]
    public void NewState_StartsWithHonestEmptySessionAndShard()
    {
        var state = new GameState();

        Assert.Equal(GameSessionState.Empty, state.Session);
        Assert.False(state.Session.IsLive);
        Assert.Equal(0, state.Session.LogGeneration);
        Assert.Equal(GameShardState.Empty, state.Shard);
        Assert.False(state.Shard.OnShard);
    }

    [Fact]
    public void PublishSession_ReplacesImmutableSnapshot_AndNotifiesOnce()
    {
        var state = new GameState();
        var snapshot = new GameSessionState(true, GameChannel.Ptu, 1);
        int changed = 0;
        int sessionChanged = 0;
        state.Changed += () => changed++;
        state.SessionChanged += () => sessionChanged++;

        state.PublishSession(snapshot);
        state.PublishSession(snapshot);

        Assert.Same(snapshot, state.Session);
        Assert.Equal(1, changed);
        Assert.Equal(1, sessionChanged);
    }

    [Fact]
    public void GameLogFeed_LogReset_IncrementsGeneration_WithoutClearingLocationOrShard()
    {
        var state = new GameState();
        using var feed = new GameLogFeed(state);
        using var locations = new LocationTracker(feed, state);
        using var shards = new ShardTracker(() => new List<ShardSession>(), _ => { }, feed, gameState: state);
        var seen = DateTime.Parse("2026-09-12T12:00:00.000Z").ToUniversalTime();
        state.PublishLocation(new GameLocationState(
            "New Babbage", null, "Stanton4_NewBabbage", false, seen));
        shards.Ingest(E(Join("pub_use1b_12030094_140")));
        int sessionChanged = 0;
        int locationChanged = 0;
        int shardChanged = 0;
        state.SessionChanged += () => sessionChanged++;
        state.LocationChanged += () => locationChanged++;
        state.ShardChanged += () => shardChanged++;

        feed.HandleLogReset();

        Assert.Equal(1, state.Session.LogGeneration);
        Assert.Equal(1, sessionChanged);
        Assert.Equal(0, locationChanged);
        Assert.Equal(0, shardChanged);
        Assert.Equal("New Babbage", state.Location.Label);
        Assert.True(state.Shard.OnShard);
        Assert.Equal("pub_use1b_12030094_140", state.Shard.ShardId);
    }

    [Fact]
    public void GameLogFeed_Start_PublishesChannelWithoutClaimingLive()
    {
        var state = new GameState();
        using var feed = new GameLogFeed(state);

        feed.Start(@"X:\SC\PTU\Game.log");

        Assert.Equal(GameChannel.Ptu, state.Session.Channel);
        Assert.False(state.Session.IsLive);
        Assert.Equal(0, state.Session.LogGeneration);
    }

    [Fact]
    public void GameLogFeed_LogReset_StillFansOutToSubscribers()
    {
        var state = new GameState();
        using var feed = new GameLogFeed(state);
        int resets = 0;
        using var sub = feed.Subscribe(_ => { }, includeReplay: false, onLogReset: () => resets++);

        feed.HandleLogReset();
        feed.HandleLogReset();

        Assert.Equal(2, resets);
        Assert.Equal(2, state.Session.LogGeneration);
    }

    [Fact]
    public void ShardTracker_PublishesCurrentShardIntoSharedState()
    {
        var state = new GameState();
        using var tracker = new ShardTracker(
            () => new List<ShardSession>(), _ => { }, gameState: state);

        tracker.Ingest(E(Join("pub_use1b_12030094_140")));

        Assert.True(state.Shard.OnShard);
        Assert.Equal("pub_use1b_12030094_140", state.Shard.ShardId);
        Assert.Equal(tracker.Current!.Region, state.Shard.Region);
        Assert.Equal(tracker.Current.Instance, state.Shard.Instance);
        Assert.Equal(tracker.Current.JoinedAt, state.Shard.JoinedUtc);
    }

    [Fact]
    public void ShardLeave_ClearsGameStateShard_WithoutTouchingLocationOrSession()
    {
        var state = new GameState();
        using var feed = new GameLogFeed(state);
        using var locations = new LocationTracker(feed, state);
        using var shards = new ShardTracker(() => new List<ShardSession>(), _ => { }, feed, gameState: state);
        locations.Ingest(E(LocationLine("Stanton4_NewBabbage", "2026-09-12T12:01:00.000Z")));
        shards.Ingest(E(Join("pub_use1b_12030094_140")));
        state.PublishSession(new GameSessionState(true, GameChannel.Live, 0));
        int locationChanged = 0;
        int sessionChanged = 0;
        state.LocationChanged += () => locationChanged++;
        state.SessionChanged += () => sessionChanged++;

        shards.Ingest(E(Leave()));

        Assert.False(state.Shard.OnShard);
        Assert.Equal(GameShardState.Empty, state.Shard);
        Assert.Null(shards.Current);
        Assert.Equal("New Babbage", state.Location.Label);
        Assert.True(state.Session.IsLive);
        Assert.Equal(0, locationChanged);
        Assert.Equal(0, sessionChanged);
        Assert.Equal("pub_use1b_12030094_140", shards.Recent[0].ShardId);
    }

    [Fact]
    public void ColdReplay_RecordsTrackerHistory_ButDoesNotClaimOnShard()
    {
        var state = new GameState();
        using var tracker = new ShardTracker(
            () => new List<ShardSession>(), _ => { }, gameState: state);
        int shardChanged = 0;
        state.ShardChanged += () => shardChanged++;

        tracker.BeginStaleReplay();
        tracker.Ingest(E(Join("pub_use1b_12030094_140")));

        Assert.False(tracker.OnShard);
        Assert.Null(tracker.Current);
        Assert.Equal("pub_use1b_12030094_140", tracker.Recent[0].ShardId);
        Assert.False(state.Shard.OnShard);
        Assert.Equal(GameShardState.Empty, state.Shard);
        Assert.Equal(0, shardChanged);
    }

    [Fact]
    public void RejoinAfterLeave_PublishesCurrentShardAgain()
    {
        var state = new GameState();
        using var tracker = new ShardTracker(
            () => new List<ShardSession>(), _ => { }, gameState: state);

        tracker.Ingest(E(Join("pub_use1b_12030094_140")));
        tracker.Ingest(E(Leave()));
        tracker.Ingest(E(Join("pub_use1b_12030094_140")));

        Assert.True(state.Shard.OnShard);
        Assert.Equal("pub_use1b_12030094_140", state.Shard.ShardId);
        Assert.Single(tracker.All);
    }

    [Fact]
    public void SameShardEvidence_DoesNotNotifyGameStateTwice()
    {
        var state = new GameState();
        using var tracker = new ShardTracker(
            () => new List<ShardSession>(), _ => { }, gameState: state);
        int shardChanged = 0;
        tracker.Ingest(E(Join("pub_use1b_12030094_140")));
        state.ShardChanged += () => shardChanged++;

        tracker.Ingest(E(Join("pub_use1b_12030094_140")));

        Assert.Equal(0, shardChanged);
        Assert.True(state.Shard.OnShard);
    }
}
