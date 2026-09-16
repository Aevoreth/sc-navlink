using NexusApp.Services;
using Xunit;

namespace NexusApp.Tests;

public class CargoStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "navlink-cargo-" + Path.GetRandomFileName());

    public CargoStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void UpsertListDelete_RoundTrips()
    {
        var path = Path.Combine(_dir, "nexus.db");
        using var store = new CargoStore(path);
        var observed = DateTime.Parse("2026-09-16T00:00:00Z").ToUniversalTime();
        var lot = new GameCargoLot(
            "c1", "100i", "Carbon", 12, 16, 8, 2,
            GameCargoProvenance.UserConfirmed, null, observed);

        store.Upsert(lot);
        var listed = Assert.Single(store.List());
        Assert.Equal("100i", listed.ShipId);
        Assert.Equal("Carbon", listed.Commodity);
        Assert.Equal(12, listed.UexCommodityId);
        Assert.Equal(16, listed.Scu);
        Assert.Equal(8, listed.ContainerScu);
        Assert.Equal(2, listed.ContainerCount);
        Assert.Equal(GameCargoProvenance.UserConfirmed, listed.Provenance);
        Assert.False(listed.OffGrid);
        Assert.NotNull(store.ListByShip("100i"));
        Assert.Empty(store.ListByShip("c2"));

        Assert.True(store.Delete("c1"));
        Assert.Empty(store.List());
    }

    [Fact]
    public void Upsert_UpdatesExistingLot()
    {
        var path = Path.Combine(_dir, "nexus.db");
        using var store = new CargoStore(path);
        var t = DateTime.Parse("2026-09-16T00:00:00Z").ToUniversalTime();
        store.Upsert(new GameCargoLot("c1", "100i", "Carbon", null, 16, null, null,
            GameCargoProvenance.UserConfirmed, null, t));
        store.Upsert(new GameCargoLot("c1", "100i", "Carbon", null, 32, 16, null,
            GameCargoProvenance.UserConfirmed, null, t));

        var listed = Assert.Single(store.List());
        Assert.Equal(32, listed.Scu);
        Assert.Equal(16, listed.ContainerScu);
    }

    [Fact]
    public void DeleteByShip_ClearsOnlyThatHull()
    {
        var path = Path.Combine(_dir, "nexus.db");
        using var store = new CargoStore(path);
        var t = DateTime.Parse("2026-09-16T00:00:00Z").ToUniversalTime();
        store.Upsert(new GameCargoLot("a", "100i", "Carbon", null, 8, null, null,
            GameCargoProvenance.UserConfirmed, null, t));
        store.Upsert(new GameCargoLot("b", "c2", "Laranite", null, 32, null, null,
            GameCargoProvenance.UserConfirmed, null, t));

        Assert.Equal(1, store.DeleteByShip("100i"));
        var left = Assert.Single(store.List());
        Assert.Equal("c2", left.ShipId);
    }

    [Fact]
    public void OffGrid_RoundTrips()
    {
        var path = Path.Combine(_dir, "nexus.db");
        using var store = new CargoStore(path);
        var t = DateTime.Parse("2026-09-16T00:00:00Z").ToUniversalTime();
        store.Upsert(new GameCargoLot("c1", "cutlass-black", "Carbon", null, 32, 32, 1,
            GameCargoProvenance.UserConfirmed, null, t, OffGrid: true));
        Assert.True(Assert.Single(store.List()).OffGrid);
    }

    [Fact]
    public void Contracted_RoundTrips()
    {
        var path = Path.Combine(_dir, "nexus.db");
        using var store = new CargoStore(path);
        var t = DateTime.Parse("2026-09-16T00:00:00Z").ToUniversalTime();
        store.Upsert(new GameCargoLot("c1", "raft", "Copper", null, 16, 8, 2,
            GameCargoProvenance.UserConfirmed, null, t, Contracted: true));
        var listed = Assert.Single(store.List());
        Assert.True(listed.Contracted);
        Assert.False(listed.OffGrid);
    }

    [Fact]
    public void Destination_RoundTrips()
    {
        var path = Path.Combine(_dir, "nexus.db");
        using var store = new CargoStore(path);
        var t = DateTime.Parse("2026-09-16T00:00:00Z").ToUniversalTime();
        store.Upsert(new GameCargoLot("c1", "raft", "Copper", null, 16, 8, 2,
            GameCargoProvenance.UserConfirmed, null, t, Destination: "Area 18"));
        var listed = Assert.Single(store.List());
        Assert.Equal("Area 18", listed.Destination);
        Assert.False(listed.Contracted);
    }

    [Fact]
    public void ZeroScuUnnamed_RoundTrips()
    {
        var path = Path.Combine(_dir, "nexus.db");
        using var store = new CargoStore(path);
        var t = DateTime.Parse("2026-09-16T00:00:00Z").ToUniversalTime();
        store.Upsert(new GameCargoLot("c1", "raft", "", null, 0, null, null,
            GameCargoProvenance.UserConfirmed, null, t));
        var listed = Assert.Single(store.List());
        Assert.Equal("", listed.Commodity);
        Assert.Equal(0, listed.Scu);
    }
}

public class CargoLotsTests
{
    private static GameCargoLot Draft(
        string ship = "100i",
        string commodity = "Carbon",
        int scu = 16,
        GameCargoProvenance provenance = GameCargoProvenance.UserConfirmed) =>
        new("id1", ship, commodity, null, scu, null, null, provenance, null,
            DateTime.Parse("2026-09-16T00:00:00Z").ToUniversalTime());

    [Fact]
    public void TryNormalize_TrimsAndAccepts()
    {
        Assert.True(CargoLots.TryNormalize(Draft(commodity: "  Carbon  "), out var lot));
        Assert.Equal("Carbon", lot.Commodity);
        Assert.Equal("100i", lot.ShipId);
    }

    [Theory]
    [InlineData("", "Carbon", 16)]
    [InlineData("100i", "Carbon", -4)]
    public void TryNormalize_RejectsBlankShipOrNegativeScu(string ship, string commodity, int scu)
    {
        Assert.False(CargoLots.TryNormalize(Draft(ship, commodity, scu), out _));
    }

    [Fact]
    public void TryNormalize_AcceptsZeroScuAndBlankCommodity()
    {
        Assert.True(CargoLots.TryNormalize(Draft(commodity: "", scu: 0), out var unnamed));
        Assert.Equal("", unnamed.Commodity);
        Assert.Equal(0, unnamed.Scu);
        Assert.True(CargoLots.TryNormalize(Draft(commodity: "Carbon", scu: 0), out var empty));
        Assert.Equal(0, empty.Scu);
        Assert.True(CargoLots.TryNormalize(Draft(commodity: "", scu: 8), out var namedLater));
        Assert.Equal("", namedLater.Commodity);
        Assert.Equal(8, namedLater.Scu);
    }

    [Fact]
    public void TryNormalize_CopiesOffGridAndContracted()
    {
        var draft = Draft() with { OffGrid = true, Contracted = true };
        Assert.True(CargoLots.TryNormalize(draft, out var lot));
        Assert.True(lot.OffGrid);
        Assert.True(lot.Contracted);
    }

    [Fact]
    public void TryNormalize_TrimsAndCapsDestination()
    {
        Assert.True(CargoLots.TryNormalize(Draft() with { Destination = "  Area 18  " }, out var lot));
        Assert.Equal("Area 18", lot.Destination);
        Assert.True(CargoLots.TryNormalize(Draft() with { Destination = new string('x', 200) }, out var longDest));
        Assert.Equal(CargoLots.DestinationMaxLength, longDest.Destination.Length);
    }

    [Fact]
    public void CombineDestination_JoinsDistinctNotes()
    {
        var t = DateTime.Parse("2026-09-16T00:00:00Z").ToUniversalTime();
        var a = new[] { Draft() with { Destination = "Area 18" } };
        var b = new[] { new GameCargoLot("id2", "100i", "Laranite", null, 8, null, null,
            GameCargoProvenance.UserConfirmed, null, t, Destination: "Lorville") };
        Assert.Equal("Area 18 / Lorville", CargoLots.CombineDestination(a, b));
        Assert.Equal("Area 18", CargoLots.CombineDestination(a, Array.Empty<GameCargoLot>()));
        Assert.Equal("Area 18", CargoLots.CombineDestination(a, a));
    }

    [Fact]
    public void TryNormalize_RejectsNoneProvenance()
    {
        Assert.False(CargoLots.TryNormalize(Draft(provenance: GameCargoProvenance.None), out _));
    }

    [Fact]
    public void TryNormalize_ClearsNonPositiveContainerFields()
    {
        var draft = Draft() with { ContainerScu = 0, ContainerCount = -1 };
        Assert.True(CargoLots.TryNormalize(draft, out var lot));
        Assert.Null(lot.ContainerScu);
        Assert.Null(lot.ContainerCount);
    }

    [Fact]
    public void ResolveUexId_MatchesCatalogName()
    {
        var catalog = new[] { new CatalogCommodity(41, "Carbon", "carbon", true, false, 0) };
        Assert.Equal(41, CargoLots.ResolveUexId("carbon", catalog));
        Assert.Null(CargoLots.ResolveUexId("Titanium", catalog));
        Assert.Null(CargoLots.ResolveUexId("Carbon", null));
    }

    [Fact]
    public void CountsBySize_SumsStandardCrates()
    {
        var t = DateTime.Parse("2026-09-16T00:00:00Z").ToUniversalTime();
        var lots = new[]
        {
            new GameCargoLot("a", "raft", "Carbon", null, 64, 32, 2,
                GameCargoProvenance.UserConfirmed, null, t),
            new GameCargoLot("b", "raft", "Carbon", null, 8, 8, 1,
                GameCargoProvenance.UserConfirmed, null, t),
            new GameCargoLot("c", "raft", "Carbon", null, 5, null, null,
                GameCargoProvenance.UserConfirmed, null, t),
        };
        var counts = CargoLots.CountsBySize(lots);
        Assert.Equal(2, counts[32]);
        Assert.Equal(1, counts[8]);
        Assert.Equal(0, counts[16]);
        Assert.Equal(5, CargoLots.UnspecifiedScu(lots));
    }
}

public class GameStateCargoTests
{
    private static GameActiveShipState Ship(string id, string name, int scu) =>
        new(id, name, scu, GameActiveShipProvenance.Hangar);

    private static GameCargoLot Lot(string id, string ship, string commodity, int scu) =>
        new(id, ship, commodity, null, scu, null, null,
            GameCargoProvenance.UserConfirmed, null,
            DateTime.Parse("2026-09-16T00:00:00Z").ToUniversalTime());

    [Fact]
    public void NewState_StartsEmpty()
    {
        var state = new GameState();
        Assert.Equal(GameCargoState.Empty, state.Cargo);
        Assert.False(state.Cargo.HasShip);
        Assert.False(state.Cargo.HasLots);
    }

    [Fact]
    public void PublishCargo_ReplacesOnceOnIdentical()
    {
        var state = new GameState();
        state.PublishActiveShip(Ship("100i", "Origin 100i", 2));
        var snap = CargoProjection.From(state.ActiveShip, new[] { Lot("c1", "100i", "Carbon", 2) });
        int changed = 0, cargoChanged = 0;
        state.Changed += () => changed++;
        state.CargoChanged += () => cargoChanged++;

        state.PublishCargo(snap);
        state.PublishCargo(snap);

        Assert.Equal(1, changed);
        Assert.Equal(1, cargoChanged);
        Assert.True(state.Cargo.HasLots);
        Assert.Equal(2, state.Cargo.UsedScu);
        Assert.Equal(0, state.Cargo.FreeScu);
    }

    [Fact]
    public void Projection_EmptyWithoutActiveShip()
    {
        var lots = new[] { Lot("c1", "100i", "Carbon", 8) };
        Assert.Equal(GameCargoState.Empty, CargoProjection.From(GameActiveShipState.Empty, lots));
    }

    [Fact]
    public void Projection_SumsActiveShipLots_AndFlagsOverCapacity()
    {
        var ship = Ship("c2", "C2 Hercules", 96);
        var lots = new[]
        {
            Lot("a", "c2", "Carbon", 64),
            Lot("b", "c2", "Laranite", 48),
            Lot("c", "100i", "Titanium", 8),
        };
        var snap = CargoProjection.From(ship, lots);
        Assert.Equal("c2", snap.ShipId);
        Assert.Equal(2, snap.Lots.Count);
        Assert.Equal(112, snap.UsedScu);
        Assert.Equal(96, snap.UsableScu);
        Assert.Equal(-16, snap.FreeScu);
        Assert.True(snap.IsOverCapacity);
    }

    [Fact]
    public void Projection_UnknownUsable_DoesNotFlagOverCapacity()
    {
        var ship = new GameActiveShipState("100i", "Origin 100i", null, GameActiveShipProvenance.Hangar);
        var snap = CargoProjection.From(ship, new[] { Lot("a", "100i", "Carbon", 8) });
        Assert.Equal(8, snap.UsedScu);
        Assert.Null(snap.UsableScu);
        Assert.Null(snap.FreeScu);
        Assert.False(snap.IsOverCapacity);
    }

    [Fact]
    public void ActiveShipSwitch_ShowsThatHullsLots()
    {
        var state = new GameState();
        var lots = new[]
        {
            Lot("a", "100i", "Carbon", 2),
            Lot("b", "c2", "Laranite", 32),
        };

        CargoSync.Publish(state, lots);
        Assert.Equal(GameCargoState.Empty, state.Cargo);

        state.PublishActiveShip(Ship("100i", "Origin 100i", 2));
        CargoSync.Publish(state, lots);
        Assert.Equal("Carbon", Assert.Single(state.Cargo.Lots).Commodity);

        state.PublishActiveShip(Ship("c2", "C2 Hercules", 696));
        CargoSync.Publish(state, lots);
        Assert.Equal("Laranite", Assert.Single(state.Cargo.Lots).Commodity);

        state.PublishActiveShip(GameActiveShipState.Empty);
        CargoSync.Publish(state, lots);
        Assert.Equal(GameCargoState.Empty, state.Cargo);
    }
}

public class GameStateCargoHaulingIsolationTests
{
    private static GameLogEntry E(string raw) => new() { Raw = raw, Category = LogCategory.Other };

    private static string Join(string shard) =>
        $"<2026-06-27T13:14:51.882Z> [Notice] <Join PU> address[10.0.0.1] port[64318] shard[{shard}] locationId[1] [x]";

    private static GameCargoLot Lot() =>
        new("c1", "100i", "Carbon", null, 8, null, null,
            GameCargoProvenance.UserConfirmed, null,
            DateTime.Parse("2026-09-16T00:00:00Z").ToUniversalTime());

    private static void SeedCargo(GameState state)
    {
        state.PublishActiveShip(new GameActiveShipState(
            "100i", "Origin 100i", 2, GameActiveShipProvenance.Hangar));
        CargoSync.Publish(state, new[] { Lot() });
    }

    [Fact]
    public void HaulIngest_DoesNotCreateCargoLots()
    {
        var state = new GameState();
        using var tracker = new HaulTracker(gameState: state);
        tracker.Ingest(E(HaulLogParserFixtures.MarkerDropoff));
        tracker.Ingest(E(HaulLogParserFixtures.DeliverLine));

        Assert.Single(state.Hauling.Hauls);
        Assert.Equal(GameCargoState.Empty, state.Cargo);
    }

    [Fact]
    public void LogReset_ClearsHauling_KeepsCargo()
    {
        var state = new GameState();
        SeedCargo(state);
        using var feed = new GameLogFeed(state);
        using var hauls = new HaulTracker(feed, state);
        hauls.Ingest(E(HaulLogParserFixtures.MarkerPickup));

        feed.HandleLogReset();

        Assert.Equal(GameHaulingState.Empty, state.Hauling);
        Assert.Equal("Carbon", Assert.Single(state.Cargo.Lots).Commodity);
        Assert.Equal(8, state.Cargo.UsedScu);
    }

    [Fact]
    public void ShardChange_ClearsHauling_KeepsCargo()
    {
        var state = new GameState();
        SeedCargo(state);
        using var tracker = new HaulTracker(gameState: state);
        tracker.Ingest(E(Join("pub_use1b_12030094_140")));
        tracker.Ingest(E(HaulLogParserFixtures.MarkerPickup));

        tracker.Ingest(E(Join("pub_use1b_12030094_150")));

        Assert.Equal(GameHaulingState.Empty, state.Hauling);
        Assert.Equal("Carbon", Assert.Single(state.Cargo.Lots).Commodity);
    }
}
