using NexusApp.Services;
using Xunit;

namespace NexusApp.Tests;

public class MarketCatalogQueriesTests
{
    private static readonly DateTime Now = new(2026, 9, 12, 18, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Older = Now.AddHours(-6);

    private static CatalogTerminal Stanton() =>
        new(10, "TDD Lorville", "commodity", false, "Stanton", "Lorville", "Hurston", "Hurston");

    private static CatalogTerminal Pyro() =>
        new(20, "Checkmate", "commodity", false, "Pyro", "Checkmate", "Pyro I", "Pyro I");

    private static CatalogTradePrice Price(
        int terminalId, string terminal, int commodityId, string commodity,
        double buy, double sell, DateTime? observed = null,
        int stock = 100, int demand = 50) =>
        new(terminalId, commodityId, buy, sell, stock, demand, 1, 1, "",
            observed ?? Now, terminal, commodity);

    [Fact]
    public void Query_NullOrEmpty_ReturnsEmpty()
    {
        Assert.Empty(MarketCatalogQueries.Query(null, null, null));
        Assert.Empty(MarketCatalogQueries.Query(Array.Empty<CatalogTradePrice>(), null, new MarketCatalogFilter()));
    }

    [Fact]
    public void Query_IncludesRowsOmittedFromACurrentListing()
    {
        var omitted = Price(10, "TDD Lorville", 1, "Laranite", 10, 40, Older);
        var current = Price(20, "Checkmate", 1, "Laranite", 12, 35, Now);
        var rows = MarketCatalogQueries.Query(
            new[] { omitted, current },
            new[] { Stanton(), Pyro() },
            new MarketCatalogFilter(CommodityText: "Laranite"));

        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r.TerminalId == 10 && r.ObservedUtc == Older);
        Assert.Contains(rows, r => r.TerminalId == 20);
    }

    [Fact]
    public void Query_CommoditySearch_IsCaseInsensitive()
    {
        var rows = MarketCatalogQueries.Query(
            new[]
            {
                Price(10, "TDD Lorville", 1, "Laranite", 10, 40),
                Price(10, "TDD Lorville", 2, "Agricium", 5, 20),
            },
            new[] { Stanton() },
            new MarketCatalogFilter(CommodityText: "LARANITE"));

        Assert.Single(rows);
        Assert.Equal("Laranite", rows[0].CommodityName);
    }

    [Fact]
    public void Query_CommodityMatch_IsExactName()
    {
        var rows = MarketCatalogQueries.Query(
            new[] { Price(10, "TDD Lorville", 1, "Laranite", 10, 40) },
            new[] { Stanton() },
            new MarketCatalogFilter(CommodityText: "lara"));

        Assert.Empty(rows);
    }

    [Fact]
    public void Query_LocationSearch_MatchesTerminalAndHierarchy()
    {
        var prices = new[]
        {
            Price(10, "TDD Lorville", 1, "Laranite", 10, 40),
            Price(20, "Checkmate", 1, "Laranite", 12, 35),
        };
        var terminals = new[] { Stanton(), Pyro() };

        var byName = MarketCatalogQueries.Query(prices, terminals, new MarketCatalogFilter(LocationText: "lorville"));
        Assert.Single(byName);
        Assert.Equal(10, byName[0].TerminalId);

        var byOrbit = MarketCatalogQueries.Query(prices, terminals, new MarketCatalogFilter(LocationText: "hurston"));
        Assert.Single(byOrbit);
        Assert.Equal(10, byOrbit[0].TerminalId);
    }

    [Fact]
    public void Query_SystemFilter_StantonOnly()
    {
        var rows = MarketCatalogQueries.Query(
            new[]
            {
                Price(10, "TDD Lorville", 1, "Laranite", 10, 40),
                Price(20, "Checkmate", 1, "Laranite", 12, 35),
            },
            new[] { Stanton(), Pyro() },
            new MarketCatalogFilter(CommodityText: "Laranite", System: "STANTON"));

        Assert.Single(rows);
        Assert.Equal("Stanton", rows[0].System);
    }

    [Fact]
    public void Query_SystemAll_DoesNotConstrain()
    {
        var rows = MarketCatalogQueries.Query(
            new[]
            {
                Price(10, "TDD Lorville", 1, "Laranite", 10, 40),
                Price(20, "Checkmate", 1, "Laranite", 12, 35),
            },
            new[] { Stanton(), Pyro() },
            new MarketCatalogFilter(CommodityText: "Laranite", System: "ALL"));

        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public void Query_BuySide_DropsRowsWithNoBuyPrice()
    {
        var rows = MarketCatalogQueries.Query(
            new[]
            {
                Price(10, "TDD Lorville", 1, "Laranite", buy: 0, sell: 40),
                Price(20, "Checkmate", 1, "Laranite", buy: 12, sell: 0),
            },
            new[] { Stanton(), Pyro() },
            new MarketCatalogFilter(CommodityText: "Laranite", Side: MarketCatalogSide.Buy));

        Assert.Single(rows);
        Assert.Equal(20, rows[0].TerminalId);
    }

    [Fact]
    public void Query_SellSide_DropsRowsWithNoSellPrice()
    {
        var rows = MarketCatalogQueries.Query(
            new[]
            {
                Price(10, "TDD Lorville", 1, "Laranite", buy: 10, sell: 0),
                Price(20, "Checkmate", 1, "Laranite", buy: 0, sell: 35),
            },
            new[] { Stanton(), Pyro() },
            new MarketCatalogFilter(CommodityText: "Laranite", Side: MarketCatalogSide.Sell));

        Assert.Single(rows);
        Assert.Equal(20, rows[0].TerminalId);
    }

    [Fact]
    public void Query_BothSide_KeepsEitherPrice()
    {
        var rows = MarketCatalogQueries.Query(
            new[]
            {
                Price(10, "TDD Lorville", 1, "Laranite", buy: 10, sell: 0),
                Price(20, "Checkmate", 1, "Laranite", buy: 0, sell: 35),
                Price(10, "TDD Lorville", 2, "Agricium", buy: 0, sell: 0),
            },
            new[] { Stanton(), Pyro() },
            new MarketCatalogFilter(CommodityText: "Laranite", Side: MarketCatalogSide.Both));

        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public void Query_SortsSellDescendingThenCommodityThenTerminal()
    {
        var rows = MarketCatalogQueries.Query(
            new[]
            {
                Price(10, "TDD Lorville", 1, "Laranite", 10, 20),
                Price(20, "Checkmate", 1, "Laranite", 10, 40),
                Price(10, "TDD Lorville", 2, "Agricium", 10, 40),
            },
            new[] { Stanton(), Pyro() },
            new MarketCatalogFilter(LocationText: "e"));

        Assert.Equal(new[] { "Agricium", "Laranite", "Laranite" }, rows.Select(r => r.CommodityName).ToArray());
        Assert.Equal(new[] { "TDD Lorville", "Checkmate", "TDD Lorville" }, rows.Select(r => r.TerminalName).ToArray());
        Assert.Equal(new[] { 40.0, 40.0, 20.0 }, rows.Select(r => r.Sell).ToArray());
    }

    [Fact]
    public void HasBrowseConstraint_CommodityLocationOrTerminal()
    {
        Assert.False(new MarketCatalogFilter(System: "Stanton", Side: MarketCatalogSide.Buy).HasBrowseConstraint);
        Assert.True(new MarketCatalogFilter(CommodityText: "Laranite").HasBrowseConstraint);
        Assert.True(new MarketCatalogFilter(LocationText: "Lorville").HasBrowseConstraint);
        Assert.False(new MarketCatalogFilter().HasBrowseConstraint);
        Assert.True(new MarketCatalogFilter(TerminalId: 10).HasBrowseConstraint);
    }

    [Fact]
    public void Query_TerminalId_KeepsOneTerminal()
    {
        var rows = MarketCatalogQueries.Query(
            new[]
            {
                Price(10, "TDD Lorville", 1, "Laranite", 10, 40),
                Price(20, "Checkmate", 1, "Laranite", 12, 35),
                Price(10, "TDD Lorville", 2, "Agricium", 5, 20),
            },
            new[] { Stanton(), Pyro() },
            new MarketCatalogFilter(TerminalId: 10));

        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(10, r.TerminalId));
    }

    [Fact]
    public void CommodityNames_DistinctSortedIgnoreCase()
    {
        var names = MarketCatalogQueries.CommodityNames(new[]
        {
            Price(10, "A", 2, "quantanium", 1, 1),
            Price(10, "A", 1, "Laranite", 1, 1),
            Price(20, "B", 1, "laranite", 1, 1),
        });
        Assert.Equal(new[] { "Laranite", "quantanium" }, names.ToArray());
    }

    [Fact]
    public void CommodityNames_Empty_ReturnsEmpty() =>
        Assert.Empty(MarketCatalogQueries.CommodityNames(null));
}

public class MarketCatalogNoticeTests
{
    [Theory]
    [InlineData(ProviderFreshness.Fresh, MarketCatalogNotice.Fresh)]
    [InlineData(ProviderFreshness.Stale, MarketCatalogNotice.Stale)]
    [InlineData(ProviderFreshness.OfflineCached, MarketCatalogNotice.OfflineCached)]
    [InlineData(ProviderFreshness.Unavailable, MarketCatalogNotice.Unavailable)]
    public void FreshnessLabel_MatchesEnum(ProviderFreshness freshness, string expected) =>
        Assert.Equal(expected, MarketCatalogNotice.FreshnessLabel(freshness));

    [Fact]
    public void Banner_OfflineAndStaleOnly()
    {
        Assert.Equal(MarketCatalogNotice.OfflineBanner, MarketCatalogNotice.Banner(ProviderFreshness.OfflineCached));
        Assert.Equal(MarketCatalogNotice.StaleBanner, MarketCatalogNotice.Banner(ProviderFreshness.Stale));
        Assert.Null(MarketCatalogNotice.Banner(ProviderFreshness.Fresh));
        Assert.Null(MarketCatalogNotice.Banner(ProviderFreshness.Unavailable));
    }

    [Fact]
    public void LastChecked_FormatsAge()
    {
        var now = new DateTime(2026, 9, 12, 18, 0, 0, DateTimeKind.Utc);
        Assert.Equal(MarketCatalogNotice.NeverChecked, MarketCatalogNotice.LastChecked(null, now));
        Assert.Equal("Last checked 3h ago", MarketCatalogNotice.LastChecked(now.AddHours(-3), now));
    }

    [Fact]
    public void TruncationNote_NamesBothCounts() =>
        Assert.Equal("Showing 200 of 1842 rows. Narrow the filters.",
            MarketCatalogNotice.TruncationNote(200, 1842));

    [Fact]
    public void Copy_HasNoExclamationOrEmDash()
    {
        string[] copy =
        {
            MarketCatalogNotice.ConsentEmpty,
            MarketCatalogNotice.NeedFilter,
            MarketCatalogNotice.NoCachedRows,
            MarketCatalogNotice.NoMatchingRows,
            MarketCatalogNotice.OfflineBanner,
            MarketCatalogNotice.StaleBanner,
            MarketCatalogNotice.PageSubtitle,
        };
        foreach (var text in copy)
        {
            Assert.DoesNotContain("!", text);
            Assert.DoesNotContain("—", text);
            Assert.DoesNotContain("–", text);
        }
    }
}
