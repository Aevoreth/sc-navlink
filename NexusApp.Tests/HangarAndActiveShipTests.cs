using NexusApp.Services;
using Xunit;

namespace NexusApp.Tests;

public class HangarStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "navlink-hangar-" + Path.GetRandomFileName());

    public HangarStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void UpsertListDelete_RoundTrips()
    {
        var path = Path.Combine(_dir, "nexus.db");
        using var store = new HangarStore(path);
        var added = DateTime.Parse("2026-09-16T00:00:00Z").ToUniversalTime();
        var entry = new HangarEntry("h1", "100i", HangarAcquisition.Pledged, null, "Area 18", "manual", added);

        store.Upsert(entry);
        var listed = Assert.Single(store.List());
        Assert.Equal("100i", listed.CatalogId);
        Assert.Equal(HangarAcquisition.Pledged, listed.Acquisition);
        Assert.Equal("Area 18", listed.LastKnownLocation);
        Assert.NotNull(store.ByCatalogId("100i"));

        Assert.True(store.Delete("h1"));
        Assert.Empty(store.List());
        Assert.Null(store.ByCatalogId("100i"));
    }
}

public class HangarMetadataTests
{
    private static HangarEntry Row(HangarAcquisition acq = HangarAcquisition.Pledged, DateTime? exp = null, string? loc = null) =>
        new("h1", "100i", acq, exp, loc, "manual", DateTime.Parse("2026-09-16T00:00:00Z").ToUniversalTime());

    [Fact]
    public void WithAcquisition_ClearsExpiryUnlessRented()
    {
        var rented = Row(HangarAcquisition.Rented, DateTime.Parse("2026-10-01T00:00:00Z").ToUniversalTime());
        var bought = HangarMetadata.WithAcquisition(rented, HangarAcquisition.Purchased);
        Assert.Equal(HangarAcquisition.Purchased, bought.Acquisition);
        Assert.Null(bought.RentalExpiresUtc);

        var stillRented = HangarMetadata.WithAcquisition(rented, HangarAcquisition.Rented);
        Assert.Equal(rented.RentalExpiresUtc, stillRented.RentalExpiresUtc);
    }

    [Fact]
    public void WithLocation_TrimsOrClears()
    {
        Assert.Equal("Area 18", HangarMetadata.WithLocation(Row(), "  Area 18  ").LastKnownLocation);
        Assert.Null(HangarMetadata.WithLocation(Row(loc: "Area 18"), "  ").LastKnownLocation);
    }

    [Fact]
    public void TryParseExpiry_AcceptsIsoDate_AndEmpty()
    {
        Assert.True(HangarMetadata.TryParseExpiry("", out var empty));
        Assert.Null(empty);
        Assert.True(HangarMetadata.TryParseExpiry("2026-10-01", out var day));
        Assert.Equal(new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), day);
        Assert.False(HangarMetadata.TryParseExpiry("nope", out _));
        Assert.Equal("2026-10-01", HangarMetadata.ExpiryText(day));
    }

    [Fact]
    public void Summary_JoinsAcquisitionExpiryLocation()
    {
        var row = HangarMetadata.WithExpiry(
            HangarMetadata.WithLocation(Row(HangarAcquisition.Rented), "Lorville"),
            new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal("Rented · expires 2026-10-01 · Lorville", HangarMetadata.Summary(row));
    }
}

public class GameStateActiveShipTests
{
    [Fact]
    public void NewState_StartsEmpty()
    {
        var state = new GameState();
        Assert.Equal(GameActiveShipState.Empty, state.ActiveShip);
        Assert.False(state.ActiveShip.HasShip);
    }

    [Fact]
    public void PublishActiveShip_ReplacesOnceOnIdentical()
    {
        var state = new GameState();
        var snap = new GameActiveShipState("100i", "Origin 100i", 2, GameActiveShipProvenance.Hangar);
        int changed = 0, shipChanged = 0;
        state.Changed += () => changed++;
        state.ActiveShipChanged += () => shipChanged++;

        state.PublishActiveShip(snap);
        state.PublishActiveShip(snap);

        Assert.Equal(1, changed);
        Assert.Equal(1, shipChanged);
        Assert.True(state.ActiveShip.HasShip);
        Assert.Equal(2, state.ActiveShip.UsableCargoScu);
    }

    [Fact]
    public void Projection_RequiresHangarRow()
    {
        var catalog = new ShipCatalogEntry(1, "100i", "Origin 100i", "Origin", "Cargo", 2, "1",
            0, 0, 0, 0, false, false, true, null, null);
        Assert.Equal(GameActiveShipState.Empty, ActiveShipProjection.From("100i", null, catalog));

        var hangar = new HangarEntry("h1", "100i", HangarAcquisition.Purchased, null, null, "manual", DateTime.UtcNow);
        var live = ActiveShipProjection.From("100i", hangar, catalog);
        Assert.Equal("100i", live.ShipId);
        Assert.Equal(GameActiveShipProvenance.Hangar, live.Provenance);
    }
}
