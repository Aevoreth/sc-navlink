using NexusApp.Services;
using Xunit;

namespace NexusApp.Tests;

public class ProviderCacheStoreTests : IDisposable
{
    private readonly List<string> _tempDirs = new();

    public void Dispose()
    {
        foreach (var d in _tempDirs)
        {
            try { if (Directory.Exists(d)) Directory.Delete(d, recursive: true); }
            catch { /* best effort */ }
        }
    }

    private string TempDb()
    {
        var dir = Path.Combine(Path.GetTempPath(), "nexus-provider-cache-test-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return Path.Combine(dir, "provider_cache.db");
    }

    private static CatalogTradePrice Trade(int terminal, int commodity, double sell, DateTime observed, string name = "Term") =>
        new(terminal, commodity, 0, sell, 0, 10, 0, 3, "1,2,4", observed, name, "Bexalite");

    [Fact]
    public void EmptyStore_HasNoRows_AndProjectsEmptySnapshot()
    {
        using var store = new ProviderCacheStore(TempDb());

        Assert.False(store.HasAnyRows());
        var snap = store.ProjectSnapshot();
        Assert.Empty(snap.Commodities.Rows);
        Assert.Empty(snap.TradePrices.Rows);
        Assert.Equal("", snap.LiveGameVersion);
    }

    [Fact]
    public void MergeTradePrices_NewerObservation_UpdatesValues()
    {
        using var store = new ProviderCacheStore(TempDb());
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var t1 = t0.AddHours(1);
        var t2 = t0.AddHours(2);

        store.MergeTradePrices(new[] { Trade(1, 11, 100, t0) }, t1);
        store.MergeTradePrices(new[] { Trade(1, 11, 250, t2) }, t2);

        var row = Assert.Single(store.CurrentTradePrices());
        Assert.Equal(250, row.Sell);
        Assert.Equal(t2, row.ObservedUtc);
    }

    [Fact]
    public void MergeTradePrices_OlderOrEqualObservation_LeavesCachedValues_AndRefreshesLastSeen()
    {
        using var store = new ProviderCacheStore(TempDb());
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var t1 = t0.AddHours(1);
        var t2 = t0.AddHours(2);

        store.MergeTradePrices(new[] { Trade(1, 11, 100, t1, "Alpha") }, t1);
        store.MergeTradePrices(new[] { Trade(1, 11, 999, t0, "Beta") }, t2);
        store.MergeTradePrices(new[] { Trade(1, 11, 888, t1, "Gamma") }, t2);

        var row = Assert.Single(store.CurrentTradePrices());
        Assert.Equal(100, row.Sell);
        Assert.Equal("Alpha", row.TerminalName);
        Assert.Equal(t1, row.ObservedUtc);
        Assert.Equal(t2, store.FetchedUtc(ProviderDataClass.TradePrices));
    }

    [Fact]
    public void MergeTradePrices_OmittedId_StaysInSqlite_AndDropsFromSnapshot()
    {
        using var store = new ProviderCacheStore(TempDb());
        var t1 = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var t2 = t1.AddHours(1);

        store.MergeTradePrices(new[] { Trade(1, 11, 100, t1), Trade(2, 11, 200, t1) }, t1);
        store.MergeTradePrices(new[] { Trade(1, 11, 110, t2) }, t2);

        Assert.Equal(2, store.AllTradePrices().Count);
        var current = Assert.Single(store.CurrentTradePrices());
        Assert.Equal(1, current.TerminalId);
        Assert.Equal(110, current.Sell);

        var snap = store.ProjectSnapshot();
        var listed = Assert.Single(snap.TradePrices.Rows);
        Assert.Equal(1, listed.TerminalId);
        Assert.Equal(t2, snap.TradePrices.FetchedUtc);
    }

    [Fact]
    public void MergeEmpty_DoesNotWipeOrStamp()
    {
        using var store = new ProviderCacheStore(TempDb());
        var t1 = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        store.MergeCommodities(new[] { new CatalogCommodity(11, "Bexalite", "bexalite", false, true, 0) }, t1);

        store.MergeCommodities(Array.Empty<CatalogCommodity>(), t1.AddHours(1));

        Assert.Equal(t1, store.FetchedUtc(ProviderDataClass.Commodities));
        Assert.Equal("Bexalite", Assert.Single(store.CurrentCommodities()).Name);
    }

    [Fact]
    public void MergeCommodities_UpsertsById_WithoutDeletingMissing()
    {
        using var store = new ProviderCacheStore(TempDb());
        var t1 = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var t2 = t1.AddHours(1);
        store.MergeCommodities(new[]
        {
            new CatalogCommodity(10, "Old", "old", true, false, 11),
            new CatalogCommodity(11, "Bexalite", "bexalite", false, true, 0),
        }, t1);
        store.MergeCommodities(new[]
        {
            new CatalogCommodity(11, "Bexalite Refined", "bexalite", false, true, 0),
        }, t2);

        Assert.Equal(2, store.AllCommodities().Count);
        var current = Assert.Single(store.CurrentCommodities());
        Assert.Equal("Bexalite Refined", current.Name);
        Assert.Contains(store.AllCommodities(), c => c.Id == 10 && c.Name == "Old");
    }

    [Fact]
    public void NoteFailure_MarksOfflineCached_WithoutDeletingRows()
    {
        using var store = new ProviderCacheStore(TempDb());
        var fetched = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var failed = fetched.AddMinutes(10);
        store.MergeTradePrices(new[] { Trade(1, 11, 100, fetched) }, fetched);
        store.NoteFailure(ProviderDataClass.TradePrices, failed);

        var slice = store.Slice(ProviderDataClass.TradePrices, store.CurrentTradePrices(), failed);
        Assert.Equal(ProviderFreshness.OfflineCached, slice.Freshness);
        Assert.Single(slice.Items);
    }

    [Fact]
    public void Slice_InsideCadence_IsFresh_OutsideCadence_IsStale()
    {
        using var store = new ProviderCacheStore(TempDb());
        var fetched = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        store.MergeTradePrices(new[] { Trade(1, 11, 100, fetched) }, fetched);

        var fresh = store.Slice(ProviderDataClass.TradePrices, store.CurrentTradePrices(), fetched.AddMinutes(5));
        Assert.Equal(ProviderFreshness.Fresh, fresh.Freshness);

        var stale = store.Slice(ProviderDataClass.TradePrices, store.CurrentTradePrices(), fetched.AddHours(2));
        Assert.Equal(ProviderFreshness.Stale, stale.Freshness);
    }

    [Fact]
    public void ImportSnapshotFile_LoadsJsonOnce_IntoSqlite()
    {
        var dir = Path.GetDirectoryName(TempDb())!;
        var jsonPath = Path.Combine(dir, "uex_snapshot.json");
        var stamp = new DateTime(2026, 7, 27, 12, 0, 0, DateTimeKind.Utc);
        var snap = new MarketSnapshot
        {
            Schema = 1,
            LiveGameVersion = "4.9.1",
            Commodities = new MarketDataset<MarketCommodity>
            {
                FetchedUtc = stamp,
                Rows = new List<MarketCommodity> { new(11, "Bexalite", "bexalite", false, true, 0) },
            },
            TradePrices = new MarketDataset<TradePriceRow>
            {
                FetchedUtc = stamp,
                Rows = new List<TradePriceRow>
                {
                    new(400, 11, 0, 8500, 0, 1200, 0, 3, "1,2,4", stamp, "T400", "Bexalite"),
                },
            },
        };
        Assert.True(MarketSnapshotFile.Save(jsonPath, snap));

        using var store = new ProviderCacheStore(Path.Combine(dir, "provider_cache.db"));
        Assert.True(store.ImportSnapshotFile(jsonPath));
        Assert.True(store.HasAnyRows());

        var projected = store.ProjectSnapshot();
        Assert.Equal("4.9.1", projected.LiveGameVersion);
        Assert.Equal("Bexalite", Assert.Single(projected.Commodities.Rows).Name);
        Assert.Equal(8500, Assert.Single(projected.TradePrices.Rows).Sell);
        Assert.Equal(stamp, projected.TradePrices.FetchedUtc);
    }

    [Fact]
    public void EmptyCache_FreshnessIsUnavailable()
    {
        using var store = new ProviderCacheStore(TempDb());
        var slice = store.Slice(ProviderDataClass.TradePrices, store.CurrentTradePrices(), DateTime.UtcNow);
        Assert.Equal(ProviderFreshness.Unavailable, slice.Freshness);
        Assert.Empty(slice.Items);
    }
}

public class ProviderFreshnessRulesTests
{
    [Fact]
    public void IsDue_NeverFetchedOrStaleOrFutureStamp()
    {
        var now = new DateTime(2026, 9, 12, 12, 0, 0, DateTimeKind.Utc);
        var cadence = TimeSpan.FromHours(1);

        Assert.True(ProviderFreshnessRules.IsDue(default, cadence, now));
        Assert.True(ProviderFreshnessRules.IsDue(now - TimeSpan.FromHours(2), cadence, now));
        Assert.True(ProviderFreshnessRules.IsDue(now - cadence, cadence, now));
        Assert.False(ProviderFreshnessRules.IsDue(now - TimeSpan.FromMinutes(5), cadence, now));
        Assert.True(ProviderFreshnessRules.IsDue(now + TimeSpan.FromDays(1), cadence, now));
    }

    [Fact]
    public void CadenceFor_ReferenceClassesUse24h()
    {
        Assert.Equal(TimeSpan.FromHours(24), ProviderFreshnessRules.CadenceFor(ProviderDataClass.Terminals));
        Assert.Equal(TimeSpan.FromHours(24), ProviderFreshnessRules.CadenceFor(ProviderDataClass.Yields));
        Assert.Equal(TimeSpan.FromHours(1), ProviderFreshnessRules.CadenceFor(ProviderDataClass.TradePrices));
        Assert.Equal(TimeSpan.FromHours(1), ProviderFreshnessRules.CadenceFor(ProviderDataClass.Commodities));
    }
}
