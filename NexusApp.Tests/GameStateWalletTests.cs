using System.IO;
using NexusApp.Services;
using Xunit;

namespace NexusApp.Tests;

public class GameStateWalletTests : IDisposable
{
    private readonly List<string> _tempDirs = new();

    private static DateTime U(int h, int m, int s, int ms = 0) =>
        new(2026, 7, 4, h, m, s, ms, DateTimeKind.Utc);

    private static GameLogEntry E(string raw) => new() { Raw = raw, Category = LogCategory.Other };

    private static string LocationLine(string place, string timestamp) =>
        $"<{timestamp}> [Notice] <RequestLocationInventory> Player[TestPilot] requested inventory " +
        $"for Location[{place}] [Team_CoreGameplayFeatures][Inventory]";

    private sealed class Rig : IDisposable
    {
        public GameState State = new();
        public ProfitTracker Profit;
        public WalletTracker Wallet;

        public Rig(string dir, GameLogFeed? feed = null, GameState? state = null)
        {
            State = state ?? new GameState();
            Profit = new ProfitTracker(
                feed,
                historyPath: Path.Combine(dir, "profit_history.json"),
                flushScheduler: a => a());
            Wallet = new WalletTracker(
                Profit,
                feed,
                walletPath: Path.Combine(dir, "wallet.json"),
                flushScheduler: a => a(),
                gameState: State);
        }

        public void Dispose()
        {
            Wallet.Dispose();
            Profit.Dispose();
        }
    }

    private Rig NewRig(GameLogFeed? feed = null, GameState? state = null)
    {
        var dir = Path.Combine(Path.GetTempPath(), "nexus-wallet-state-test-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return new Rig(dir, feed, state);
    }

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void NewState_StartsWithHonestEmptyWallet()
    {
        var state = new GameState();

        Assert.Equal(GameWalletState.Empty, state.Wallet);
        Assert.False(state.Wallet.HasAnchor);
        Assert.Equal(GameWalletProvenance.None, state.Wallet.Provenance);
        Assert.Null(state.Wallet.Estimate);
    }

    [Fact]
    public void PublishWallet_ReplacesImmutableSnapshot_AndNotifiesOnce()
    {
        var state = new GameState();
        var snapshot = new GameWalletState(
            true, 1_000_000, 1_000_000, U(13, 0, 1), GameWalletProvenance.Ocr, 0);
        int changed = 0;
        int walletChanged = 0;
        state.Changed += () => changed++;
        state.WalletChanged += () => walletChanged++;

        state.PublishWallet(snapshot);
        state.PublishWallet(snapshot);

        Assert.Same(snapshot, state.Wallet);
        Assert.Equal(1, changed);
        Assert.Equal(1, walletChanged);
    }

    [Fact]
    public void FirstOcrCapture_PublishesEstimateAndOcrProvenance()
    {
        using var rig = NewRig();

        rig.Wallet.OnBalanceCaptured(5_000_000, U(13, 0, 0), U(13, 0, 1));

        Assert.True(rig.State.Wallet.HasAnchor);
        Assert.Equal(5_000_000, rig.State.Wallet.Estimate);
        Assert.Equal(5_000_000, rig.State.Wallet.Anchor);
        Assert.Equal(U(13, 0, 1), rig.State.Wallet.AnchorUtc);
        Assert.Equal(GameWalletProvenance.Ocr, rig.State.Wallet.Provenance);
        Assert.Equal(0, rig.State.Wallet.SessionUntrackedCount);
    }

    [Fact]
    public void ManualBalance_PublishesManualProvenance()
    {
        using var rig = NewRig();

        rig.Wallet.SetManualBalance(500_000);

        Assert.Equal(500_000, rig.State.Wallet.Estimate);
        Assert.Equal(GameWalletProvenance.Manual, rig.State.Wallet.Provenance);
    }

    [Fact]
    public void PostAnchorTrade_UpdatesEstimateWithoutChangingProvenance()
    {
        using var rig = NewRig();
        rig.Wallet.OnBalanceCaptured(1_000_000, U(13, 0, 0), U(13, 0, 1));

        rig.Profit.Ingest(E(CommodityLogFixtures.BuyLine));

        Assert.Equal(1_000_000 - 182_560, rig.State.Wallet.Estimate);
        Assert.Equal(1_000_000, rig.State.Wallet.Anchor);
        Assert.Equal(GameWalletProvenance.Ocr, rig.State.Wallet.Provenance);
    }

    [Fact]
    public void LogReset_KeepsAnchorAndEstimate_ClearsSessionUntracked()
    {
        var state = new GameState();
        using var feed = new GameLogFeed(state);
        using var rig = NewRig(feed, state);
        using var locations = new LocationTracker(feed, state);
        locations.Ingest(E(LocationLine("Stanton4_NewBabbage", "2026-09-12T12:01:00.000Z")));
        rig.Wallet.Ingest(E(WalletLogFixtures.TriggerLine));
        var t0 = new DateTime(2026, 8, 6, 1, 0, 0, DateTimeKind.Utc);
        var t1 = new DateTime(2026, 8, 6, 1, 10, 0, DateTimeKind.Utc);
        rig.Wallet.OnBalanceCaptured(1_000_000, t0, t0.AddSeconds(1));
        rig.Wallet.OnBalanceCaptured(1_080_000, t1, t1.AddSeconds(1));
        Assert.Equal(1, rig.State.Wallet.SessionUntrackedCount);
        int locationChanged = 0;
        state.LocationChanged += () => locationChanged++;

        feed.HandleLogReset();

        Assert.True(state.Wallet.HasAnchor);
        Assert.Equal(1_080_000, state.Wallet.Estimate);
        Assert.Equal(GameWalletProvenance.Ocr, state.Wallet.Provenance);
        Assert.Equal(0, state.Wallet.SessionUntrackedCount);
        Assert.Equal("New Babbage", state.Location.Label);
        Assert.Equal(1, state.Session.LogGeneration);
        Assert.Equal(0, locationChanged);
    }

    [Fact]
    public void ReloadedTracker_PublishesPersistedAnchor()
    {
        var dir = Path.Combine(Path.GetTempPath(), "nexus-wallet-state-test-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);

        using (var first = new Rig(dir))
            first.Wallet.OnBalanceCaptured(1_000_000, U(13, 0, 0), U(13, 0, 1));

        using var reloaded = new Rig(dir);
        Assert.True(reloaded.State.Wallet.HasAnchor);
        Assert.Equal(1_000_000, reloaded.State.Wallet.Estimate);
        Assert.Equal(GameWalletProvenance.Ocr, reloaded.State.Wallet.Provenance);
    }

    [Fact]
    public void UnexplainedGain_IncrementsSessionUntrackedCount()
    {
        using var rig = NewRig();
        rig.Wallet.Ingest(E(WalletLogFixtures.TriggerLine));
        var t0 = new DateTime(2026, 8, 6, 1, 0, 0, DateTimeKind.Utc);
        var t1 = new DateTime(2026, 8, 6, 1, 10, 0, DateTimeKind.Utc);
        rig.Wallet.OnBalanceCaptured(1_000_000, t0, t0.AddSeconds(1));
        rig.Wallet.OnBalanceCaptured(1_080_000, t1, t1.AddSeconds(1));

        Assert.Equal(1, rig.State.Wallet.SessionUntrackedCount);
        Assert.Equal(1_080_000, rig.State.Wallet.Estimate);
    }
}
