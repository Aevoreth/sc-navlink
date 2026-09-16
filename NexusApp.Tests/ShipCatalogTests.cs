using NexusApp.Services;
using Xunit;

namespace NexusApp.Tests;

public class VehicleParseTests
{
    [Fact]
    public void ParseVehicles_MapsSlugFlagsAndPhoto()
    {
        const string body = """
            {"status":"ok","data":[
              {"id":10,"name":"100i","name_full":"Origin 100i","slug":"100i","company_name":"Origin Jumpworks",
               "scu":2,"crew":"1","mass":1000,"width":8,"height":4,"length":16,
               "is_concept":0,"is_ground_vehicle":0,"is_spaceship":1,"is_cargo":1,
               "url_store":"https://robertsspaceindustries.com/pledge/ships/100i",
               "url_photo":"https://media.robertsspaceindustries.com/100i.jpg"}
            ]}
            """;
        var parsed = MarketParse.ParseVehicles(body, out var skipped);
        Assert.Equal(0, skipped);
        var row = Assert.Single(parsed);
        Assert.Equal("100i", row.Slug);
        Assert.Equal("Origin 100i", row.Name);
        Assert.Equal("Cargo", row.Role);
        Assert.Equal(2, row.CargoScu);
        Assert.False(row.IsConcept);
        Assert.True(row.IsSpaceship);
        Assert.Contains("100i.jpg", row.PhotoUrl);

        var catalog = UexNormalizer.Vehicle(row);
        Assert.Equal("100i", catalog.Id);
        Assert.True(catalog.IsFlyable);
        Assert.Equal("https://media.robertsspaceindustries.com/100i.jpg", catalog.PhotoUrl);
    }

    [Fact]
    public void ParseVehicles_SkipsRowWithoutSlug()
    {
        const string body = """
            {"status":"ok","data":[
              {"id":1,"name":"Mystery"},
              {"id":2,"name":"Cutlass Black","slug":"cutlass-black","is_concept":1}
            ]}
            """;
        var parsed = MarketParse.ParseVehicles(body, out var skipped);
        Assert.Equal(1, skipped);
        var row = Assert.Single(parsed);
        Assert.True(row.IsConcept);
        Assert.Equal("cutlass-black", row.Slug);
    }

    [Fact]
    public void ParseVehiclePurchases_KeepsPrice()
    {
        const string body = """
            {"status":"ok","data":[
              {"id_vehicle":10,"id_terminal":77,"terminal_name":"New Babbage","price_buy":123456}
            ]}
            """;
        var rows = MarketParse.ParseVehiclePurchases(body, out var skipped);
        Assert.Equal(0, skipped);
        var row = Assert.Single(rows);
        Assert.Equal(10, row.VehicleId);
        Assert.Equal(123456, row.PriceBuy);
    }
}

public class ShipCatalogQueryTests
{
    private static ShipCatalogEntry Ship(string id, bool concept = false, bool ground = false, string role = "Cargo") =>
        new(1, id, id, "Origin Jumpworks", role, 8, "1", 0, 0, 0, 0, concept, ground, !ground, null, null);

    [Fact]
    public void Filter_HidesConceptAndGroundByDefault()
    {
        var rows = new[]
        {
            Ship("100i"),
            Ship("pioneer", concept: true),
            Ship("cyclone", ground: true),
        };
        var visible = ShipCatalogQueries.Filter(rows, new ShipCatalogQuery());
        Assert.Equal(["100i"], visible.Select(s => s.Id));
    }

    [Fact]
    public void Filter_SearchMatchesName()
    {
        var rows = new[] { Ship("100i"), Ship("125a") };
        var visible = ShipCatalogQueries.Filter(rows, new ShipCatalogQuery(Search: "125"));
        Assert.Equal(["125a"], visible.Select(s => s.Id));
    }

    [Fact]
    public void Filter_ManufacturerAndRoleChips()
    {
        var rows = new[]
        {
            new ShipCatalogEntry(1, "100i", "100i", "Origin Jumpworks", "Cargo", 2, "1", 0, 0, 0, 0, false, false, true, null, null),
            new ShipCatalogEntry(2, "gladius", "Gladius", "Aegis Dynamics", "Combat", 0, "1", 0, 0, 0, 0, false, false, true, null, null),
        };
        var origin = ShipCatalogQueries.Filter(rows, new ShipCatalogQuery(Manufacturer: "Origin Jumpworks"));
        Assert.Equal(["100i"], origin.Select(s => s.Id));
        var combat = ShipCatalogQueries.Filter(rows, new ShipCatalogQuery(Role: "Combat"));
        Assert.Equal(["gladius"], combat.Select(s => s.Id));
    }

    [Fact]
    public void Facets_IgnoreSelectedManufacturerAndSearch()
    {
        var rows = new[]
        {
            Ship("100i", role: "Cargo"),
            new ShipCatalogEntry(2, "gladius", "Gladius", "Aegis Dynamics", "Combat", 0, "1", 0, 0, 0, 0, false, false, true, null, null),
            Ship("pioneer", concept: true, role: "Industrial"),
        };
        var (mfr, roles) = ShipCatalogQueries.Facets(rows, includeConcept: false, includeGround: false);
        Assert.Equal(["Aegis Dynamics", "Origin Jumpworks"], mfr);
        Assert.Equal(["Cargo", "Combat"], roles);
    }
}

public class ShipCatalogIdsTests
{
    [Fact]
    public void ToTradeShipId_MatchesOrigPrefix()
    {
        var trade = TradeShipCatalog.LoadEmbedded();
        Assert.Equal("orig-100i", ShipCatalogIds.ToTradeShipId("100i", trade));
        var mapped = ShipCatalogIds.ToTradeShip("100i", trade);
        Assert.NotNull(mapped);
        Assert.Equal(mapped!.TotalScu, ShipCatalogIds.UsableScu(
            new ShipCatalogEntry(1, "100i", "100i", "Origin", "Cargo", 999, "", 0, 0, 0, 0, false, false, true, null, null),
            trade));
    }

    [Fact]
    public void OverlappingMappedHulls_PreferTradeCapacity()
    {
        var trade = TradeShipCatalog.LoadEmbedded();
        Assert.All(trade.Ships.Take(5), t =>
        {
            var slug = t.Id.Contains('-') ? t.Id[(t.Id.IndexOf('-') + 1)..] : t.Id;
            var catalog = new ShipCatalogEntry(1, slug, t.DisplayName, t.Manufacturer, "Cargo",
                t.TotalScu, "", 0, 0, 0, 0, false, false, true, null, null);
            Assert.Equal(t.TotalScu, ShipCatalogIds.UsableScu(catalog, trade));
        });
    }
}

public class TradeShipSeedTests
{
    [Fact]
    public void MaybeSeed_FillsEmptyTradeShipId()
    {
        var settings = new NexusApp.Models.AppSettings();
        Assert.True(TradeShipSeed.MaybeSeed(settings, "100i"));
        Assert.Equal("orig-100i", settings.TradeShipId);
    }

    [Fact]
    public void MaybeSeed_DoesNotOverwriteExistingSelection()
    {
        var settings = new NexusApp.Models.AppSettings { TradeShipId = "drak-cutlass-black" };
        Assert.False(TradeShipSeed.MaybeSeed(settings, "100i"));
        Assert.Equal("drak-cutlass-black", settings.TradeShipId);
    }
}

public class ShipsFlowsTests
{
    [Theory]
    [InlineData("browser", "browser")]
    [InlineData("hangar", "hangar")]
    [InlineData("loadout", "loadout")]
    [InlineData("nope", "browser")]
    [InlineData(null, "browser")]
    public void NormalizeForRestore_WhitelistsTabs(string? saved, string expected) =>
        Assert.Equal(expected, ShipsFlows.NormalizeForRestore(saved));
}

public class ShipDetailCopyTests
{
    private static ShipCatalogEntry Ship(
        bool concept = false, bool ground = false,
        string crew = "1", double mass = 1000,
        double length = 16, double beam = 8, double height = 4,
        int scu = 2, string? store = "https://robertsspaceindustries.com/pledge/ships/100i") =>
        new(1, "test-hull", "Test Hull", "Origin Jumpworks", "Cargo", scu, crew, mass,
            length, beam, height, concept, ground, !ground, store, null);

    [Fact]
    public void StatusBadge_PrefersConceptThenGround()
    {
        Assert.Equal("FLYABLE", ShipDetailCopy.StatusBadge(Ship()));
        Assert.Equal("CONCEPT", ShipDetailCopy.StatusBadge(Ship(concept: true)));
        Assert.Equal("GROUND", ShipDetailCopy.StatusBadge(Ship(ground: true)));
    }

    [Fact]
    public void Specs_FormatOrDash()
    {
        Assert.Equal("1", ShipDetailCopy.Crew(Ship()));
        Assert.Equal("—", ShipDetailCopy.Crew(Ship(crew: "  ")));
        Assert.Equal("1,000 kg", ShipDetailCopy.Mass(Ship()));
        Assert.Equal("—", ShipDetailCopy.Mass(Ship(mass: 0)));
        Assert.Equal("16.0 × 8.0 × 4.0 m", ShipDetailCopy.Dimensions(Ship()));
        Assert.Equal("—", ShipDetailCopy.Dimensions(Ship(length: 0, beam: 0, height: 0)));
        Assert.Equal("2 SCU", ShipDetailCopy.Cargo(Ship()));
    }

    [Fact]
    public void SafeStoreUrl_AllowlistsHttpsOnly()
    {
        Assert.Equal(
            "https://robertsspaceindustries.com/pledge/ships/100i",
            ShipDetailCopy.SafeStoreUrl("https://robertsspaceindustries.com/pledge/ships/100i"));
        Assert.Null(ShipDetailCopy.SafeStoreUrl("http://robertsspaceindustries.com/pledge/ships/100i"));
        Assert.Null(ShipDetailCopy.SafeStoreUrl("https://evil.example/store"));
        Assert.Null(ShipDetailCopy.SafeStoreUrl(null));
    }

    [Fact]
    public void RankPurchases_DropsZero_AndSortsByPrice()
    {
        var ranked = ShipDetailCopy.RankPurchases(
        [
            new(1, 2, "Lorville", 200_000),
            new(1, 3, "Area18", 0),
            new(1, 1, "New Babbage", 123_456),
        ]);
        Assert.Equal(2, ranked.Count);
        Assert.Equal("New Babbage", ranked[0].TerminalName);
        Assert.Equal("Lorville  ·  200,000 aUEC", ShipDetailCopy.ListingLine(ranked[1].TerminalName, ranked[1].PriceBuy));
    }
}
