using NexusApp.Services;
using Xunit;

namespace NexusApp.Tests;

public class GameStateMiningTests
{
    private static DateTime T => DateTime.Parse("2026-09-12T14:00:00.000Z").ToUniversalTime();

    private static GameMiningHit Hit(string name, bool exact, double error = 0, int nodes = 1, string method = "ship")
        => new(name, method, nodes, exact, error);

    [Fact]
    public void NewState_StartsWithHonestEmptyMining()
    {
        var state = new GameState();

        Assert.Equal(GameMiningState.Empty, state.Mining);
        Assert.False(state.Mining.HasScan);
        Assert.Null(state.Mining.LastScan);
        Assert.Empty(state.Mining.Recent);
    }

    [Fact]
    public void PublishMining_ReplacesImmutableSnapshot_AndNotifiesOnce()
    {
        var state = new GameState();
        var snapshot = MiningScanProjection.FromScan(
            11700,
            new[] { Hit("Torite", exact: true, nodes: 3) },
            Array.Empty<GameMiningHistoryEntry>(),
            T);
        int changed = 0;
        int miningChanged = 0;
        state.Changed += () => changed++;
        state.MiningChanged += () => miningChanged++;

        state.PublishMining(snapshot);
        state.PublishMining(MiningScanProjection.FromScan(
            11700,
            new[] { Hit("Torite", exact: true, nodes: 3) },
            Array.Empty<GameMiningHistoryEntry>(),
            T));

        Assert.Equal(1, changed);
        Assert.Equal(1, miningChanged);
        Assert.True(state.Mining.HasScan);
        Assert.Equal("Torite", state.Mining.LastScan!.TopResource);
    }

    [Fact]
    public void Classify_NamesExactOreEvenWhenACloseHitRanksFirst()
    {
        var hits = new[]
        {
            Hit("PinnedClose", exact: false, error: 0.2),
            Hit("Gold", exact: true),
        };

        Assert.Equal(GameMiningMatchKind.Exact, MiningScanProjection.Classify(hits));
        Assert.Equal("Gold", MiningScanProjection.TopName(hits));
    }

    [Fact]
    public void Classify_NoHitsIsNone()
    {
        Assert.Equal(GameMiningMatchKind.None, MiningScanProjection.Classify([]));
        Assert.Equal("No match", MiningScanProjection.TopName([]));
    }

    [Fact]
    public void FromScan_KeepsUnfilteredHits_AndExistingHistory()
    {
        var recent = new[] { new GameMiningHistoryEntry(3900, "Quantanium", GameMiningMatchKind.Exact) };
        var hits = new[]
        {
            Hit("Torite", exact: true, nodes: 3),
            Hit("CloseOre", exact: false, error: 1.5),
        };

        var snapshot = MiningScanProjection.FromScan(11700, hits, recent, T);

        Assert.Equal(11700, snapshot.LastScan!.Rs);
        Assert.Equal(2, snapshot.LastScan.Hits.Count);
        Assert.Equal("CloseOre", snapshot.LastScan.Hits[1].ResourceName);
        Assert.Equal(recent, snapshot.Recent);
    }

    [Fact]
    public void ClearedScan_DropsLastDecode_KeepsHistory()
    {
        var recent = new[] { new GameMiningHistoryEntry(11700, "Torite", GameMiningMatchKind.Exact) };

        var snapshot = MiningScanProjection.ClearedScan(recent);

        Assert.False(snapshot.HasScan);
        Assert.Equal(recent, snapshot.Recent);
    }

    [Fact]
    public void ClearedHistory_DropsRecent_KeepsLastDecode()
    {
        var last = new GameMiningScan(11700, "Torite", GameMiningMatchKind.Exact, T,
            new[] { Hit("Torite", exact: true, nodes: 3) });

        var snapshot = MiningScanProjection.ClearedHistory(last);

        Assert.Equal(last, snapshot.LastScan);
        Assert.Empty(snapshot.Recent);
    }

    [Fact]
    public void LogReset_DoesNotClearMining()
    {
        var state = new GameState();
        using var feed = new GameLogFeed(state);
        state.PublishMining(MiningScanProjection.FromScan(
            11700,
            new[] { Hit("Torite", exact: true, nodes: 3) },
            new[] { new GameMiningHistoryEntry(11700, "Torite", GameMiningMatchKind.Exact) },
            T));
        int miningChanged = 0;
        state.MiningChanged += () => miningChanged++;

        feed.HandleLogReset();

        Assert.True(state.Mining.HasScan);
        Assert.Equal("Torite", state.Mining.LastScan!.TopResource);
        Assert.Single(state.Mining.Recent);
        Assert.Equal(1, state.Session.LogGeneration);
        Assert.Equal(0, miningChanged);
    }
}
