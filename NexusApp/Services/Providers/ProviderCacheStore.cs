using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;

namespace NexusApp.Services;

/// <summary>
/// SQLite cache for normalized UEX market data. Merge-newer: a newer date_modified
/// updates the row; an older or equal stamp leaves values; omitted ids stay in the
/// table. Current listings are rows whose last_seen matches the slice fetch stamp.
/// Cadence is a fetch throttle, not a delete timer.
/// </summary>
internal sealed class ProviderCacheStore : IDisposable
{
    public const string SourceUex = "uex";
    public const string FileName = "provider_cache.db";

    public static string DefaultPath => Path.Combine(AppPaths.Root, "cache", FileName);

    private readonly SqliteConnection _conn;
    private readonly object _gate = new();
    private bool _disposed;

    public ProviderCacheStore(string? dbPath = null)
    {
        var path = dbPath ?? DefaultPath;
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        _conn = new SqliteConnection($"Data Source={path}");
        _conn.Open();
        Exec("PRAGMA busy_timeout=3000;");
        CreateSchema();
    }

    public bool HasAnyRows()
    {
        lock (_gate)
        {
            return ScalarInt("SELECT COUNT(*) FROM commodities;") > 0
                || ScalarInt("SELECT COUNT(*) FROM terminals;") > 0
                || ScalarInt("SELECT COUNT(*) FROM trade_prices;") > 0
                || ScalarInt("SELECT COUNT(*) FROM refined_prices;") > 0
                || ScalarInt("SELECT COUNT(*) FROM yields;") > 0
                || ScalarInt("SELECT COUNT(*) FROM raw_prices;") > 0;
        }
    }

    public bool ImportSnapshotFile(string jsonPath)
    {
        var loaded = MarketSnapshotFile.Load(jsonPath, out _);
        if (loaded is null) return false;
        ImportSnapshot(loaded);
        return true;
    }

    public void ImportSnapshot(MarketSnapshot snapshot)
    {
        lock (_gate)
        {
            using var tx = _conn.BeginTransaction();
            try
            {
                if (snapshot.Commodities.Rows.Count > 0)
                    MergeCommoditiesCore(snapshot.Commodities.Rows.Select(UexNormalizer.Commodity).ToList(), snapshot.Commodities.FetchedUtc);
                else if (snapshot.Commodities.FetchedUtc != default)
                    UpsertMetaSuccess(ProviderDataClass.Commodities, snapshot.Commodities.FetchedUtc, default);

                if (snapshot.Terminals.Rows.Count > 0)
                    MergeTerminalsCore(snapshot.Terminals.Rows.Select(UexNormalizer.Terminal).ToList(), snapshot.Terminals.FetchedUtc);
                else if (snapshot.Terminals.FetchedUtc != default)
                    UpsertMetaSuccess(ProviderDataClass.Terminals, snapshot.Terminals.FetchedUtc, default);

                if (snapshot.TradePrices.Rows.Count > 0)
                    MergeTradePricesCore(snapshot.TradePrices.Rows.Select(UexNormalizer.TradePrice).ToList(), snapshot.TradePrices.FetchedUtc);
                else if (snapshot.TradePrices.FetchedUtc != default)
                    UpsertMetaSuccess(ProviderDataClass.TradePrices, snapshot.TradePrices.FetchedUtc, default);

                if (snapshot.RefinedPrices.Rows.Count > 0)
                    MergeRefinedPricesCore(snapshot.RefinedPrices.Rows.Select(UexNormalizer.RefinedPrice).ToList(), snapshot.RefinedPrices.FetchedUtc);
                else if (snapshot.RefinedPrices.FetchedUtc != default)
                    UpsertMetaSuccess(ProviderDataClass.RefinedPrices, snapshot.RefinedPrices.FetchedUtc, default);

                if (snapshot.Yields.Rows.Count > 0)
                    MergeYieldsCore(snapshot.Yields.Rows.Select(UexNormalizer.Yield).ToList(), snapshot.Yields.FetchedUtc);
                else if (snapshot.Yields.FetchedUtc != default)
                    UpsertMetaSuccess(ProviderDataClass.Yields, snapshot.Yields.FetchedUtc, default);

                if (snapshot.RawPrices.Rows.Count > 0)
                    MergeRawPricesCore(snapshot.RawPrices.Rows.Select(UexNormalizer.RawPrice).ToList(), snapshot.RawPrices.FetchedUtc);
                else if (snapshot.RawPrices.FetchedUtc != default)
                    UpsertMetaSuccess(ProviderDataClass.RawPrices, snapshot.RawPrices.FetchedUtc, default);
                if (!string.IsNullOrWhiteSpace(snapshot.LiveGameVersion))
                    SetLiveGameVersionCore(snapshot.LiveGameVersion, NewestNonDefault(
                        snapshot.Commodities.FetchedUtc, snapshot.RefinedPrices.FetchedUtc, snapshot.TradePrices.FetchedUtc));
                tx.Commit();
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }
    }

    public void MergeCommodities(IReadOnlyList<CatalogCommodity> rows, DateTime fetchedUtc)
    {
        if (rows.Count == 0) return;
        lock (_gate)
        {
            using var tx = _conn.BeginTransaction();
            MergeCommoditiesCore(rows, fetchedUtc);
            tx.Commit();
        }
    }

    public void MergeTerminals(IReadOnlyList<CatalogTerminal> rows, DateTime fetchedUtc)
    {
        if (rows.Count == 0) return;
        lock (_gate)
        {
            using var tx = _conn.BeginTransaction();
            MergeTerminalsCore(rows, fetchedUtc);
            tx.Commit();
        }
    }

    public void MergeTradePrices(IReadOnlyList<CatalogTradePrice> rows, DateTime fetchedUtc)
    {
        if (rows.Count == 0) return;
        lock (_gate)
        {
            using var tx = _conn.BeginTransaction();
            MergeTradePricesCore(rows, fetchedUtc);
            tx.Commit();
        }
    }

    public void MergeRefinedPrices(IReadOnlyList<CatalogRefinedPrice> rows, DateTime fetchedUtc)
    {
        if (rows.Count == 0) return;
        lock (_gate)
        {
            using var tx = _conn.BeginTransaction();
            MergeRefinedPricesCore(rows, fetchedUtc);
            tx.Commit();
        }
    }

    public void MergeYields(IReadOnlyList<CatalogYield> rows, DateTime fetchedUtc)
    {
        if (rows.Count == 0) return;
        lock (_gate)
        {
            using var tx = _conn.BeginTransaction();
            MergeYieldsCore(rows, fetchedUtc);
            tx.Commit();
        }
    }

    public void SetLiveGameVersion(string version, DateTime fetchedUtc)
    {
        lock (_gate)
        {
            SetLiveGameVersionCore(version, fetchedUtc);
        }
    }

    public void NoteFailure(ProviderDataClass dataClass, DateTime failedUtc)
    {
        lock (_gate)
        {
            UpsertMetaFailure(dataClass, failedUtc);
        }
    }

    public DateTime FetchedUtc(ProviderDataClass dataClass)
    {
        lock (_gate)
        {
            return ReadMeta(dataClass).FetchedUtc;
        }
    }

    public string LiveGameVersion()
    {
        lock (_gate)
        {
            return ReadMeta(ProviderDataClass.GameVersion).LiveGameVersion ?? "";
        }
    }

    public IReadOnlyList<CatalogCommodity> AllCommodities()
    {
        lock (_gate) { return ReadCommodities(currentOnly: false); }
    }

    public IReadOnlyList<CatalogCommodity> CurrentCommodities()
    {
        lock (_gate) { return ReadCommodities(currentOnly: true); }
    }

    public IReadOnlyList<CatalogTerminal> AllTerminals()
    {
        lock (_gate) { return ReadTerminals(currentOnly: false); }
    }

    public IReadOnlyList<CatalogTerminal> CurrentTerminals()
    {
        lock (_gate) { return ReadTerminals(currentOnly: true); }
    }

    public IReadOnlyList<CatalogTradePrice> AllTradePrices()
    {
        lock (_gate) { return ReadTradePrices(currentOnly: false); }
    }

    public IReadOnlyList<CatalogTradePrice> CurrentTradePrices()
    {
        lock (_gate) { return ReadTradePrices(currentOnly: true); }
    }

    public IReadOnlyList<CatalogRefinedPrice> AllRefinedPrices()
    {
        lock (_gate) { return ReadRefinedPrices(currentOnly: false); }
    }

    public IReadOnlyList<CatalogRefinedPrice> CurrentRefinedPrices()
    {
        lock (_gate) { return ReadRefinedPrices(currentOnly: true); }
    }

    public IReadOnlyList<CatalogYield> AllYields()
    {
        lock (_gate) { return ReadYields(currentOnly: false); }
    }

    public IReadOnlyList<CatalogYield> CurrentYields()
    {
        lock (_gate) { return ReadYields(currentOnly: true); }
    }

    public CachedSlice<T> Slice<T>(
        ProviderDataClass dataClass,
        IReadOnlyList<T> currentItems,
        DateTime nowUtc)
    {
        lock (_gate)
        {
            var meta = ReadMeta(dataClass);
            var cadence = ProviderFreshnessRules.CadenceFor(dataClass);
            var freshness = ProviderFreshnessRules.Evaluate(
                currentItems.Count, meta.FetchedUtc, meta.LastFailureUtc, cadence, nowUtc);
            var fetched = meta.FetchedUtc == default ? (DateTime?)null : meta.FetchedUtc;
            var observed = meta.ObservedUtc == default ? (DateTime?)null : meta.ObservedUtc;
            return new CachedSlice<T>(currentItems, fetched, observed, freshness, SourceUex);
        }
    }

    public MarketSnapshot ProjectSnapshot()
    {
        lock (_gate)
        {
            return new MarketSnapshot
            {
                Schema = 1,
                LiveGameVersion = ReadMeta(ProviderDataClass.GameVersion).LiveGameVersion ?? "",
                Commodities = ToDataset(ReadCommodities(true), ReadMeta(ProviderDataClass.Commodities).FetchedUtc,
                    UexNormalizer.ToMarket),
                Terminals = ToDataset(ReadTerminals(true), ReadMeta(ProviderDataClass.Terminals).FetchedUtc,
                    UexNormalizer.ToMarket),
                TradePrices = ToDataset(ReadTradePrices(true), ReadMeta(ProviderDataClass.TradePrices).FetchedUtc,
                    UexNormalizer.ToMarket),
                RefinedPrices = ToDataset(ReadRefinedPrices(true), ReadMeta(ProviderDataClass.RefinedPrices).FetchedUtc,
                    UexNormalizer.ToMarket),
                Yields = ToDataset(ReadYields(true), ReadMeta(ProviderDataClass.Yields).FetchedUtc,
                    UexNormalizer.ToMarket),
                RawPrices = ToDataset(ReadRawPrices(true), ReadMeta(ProviderDataClass.RawPrices).FetchedUtc,
                    UexNormalizer.ToMarket),
            };
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _conn.Dispose();
    }

    private static MarketDataset<TMarket> ToDataset<TCat, TMarket>(
        IReadOnlyList<TCat> rows, DateTime fetchedUtc, Func<TCat, TMarket> map)
    {
        var list = new List<TMarket>(rows.Count);
        foreach (var row in rows) list.Add(map(row));
        return new MarketDataset<TMarket> { FetchedUtc = fetchedUtc, Rows = list };
    }

    private void CreateSchema() => Exec(@"
        CREATE TABLE IF NOT EXISTS slice_meta (
            data_class TEXT PRIMARY KEY,
            source TEXT NOT NULL,
            fetched_utc INTEGER NOT NULL DEFAULT 0,
            observed_utc INTEGER NOT NULL DEFAULT 0,
            last_failure_utc INTEGER NOT NULL DEFAULT 0,
            live_game_version TEXT
        );
        CREATE TABLE IF NOT EXISTS commodities (
            id INTEGER PRIMARY KEY,
            name TEXT NOT NULL,
            slug TEXT NOT NULL,
            is_raw INTEGER NOT NULL,
            is_refined INTEGER NOT NULL,
            parent_id INTEGER NOT NULL,
            last_seen_utc INTEGER NOT NULL
        );
        CREATE TABLE IF NOT EXISTS terminals (
            id INTEGER PRIMARY KEY,
            name TEXT NOT NULL,
            type TEXT NOT NULL,
            is_refinery INTEGER NOT NULL,
            system TEXT NOT NULL,
            location TEXT NOT NULL,
            orbit TEXT NOT NULL,
            planet_or_moon TEXT NOT NULL,
            last_seen_utc INTEGER NOT NULL
        );
        CREATE TABLE IF NOT EXISTS trade_prices (
            terminal_id INTEGER NOT NULL,
            commodity_id INTEGER NOT NULL,
            buy REAL NOT NULL,
            sell REAL NOT NULL,
            buy_stock_scu INTEGER NOT NULL,
            sell_demand_scu INTEGER NOT NULL,
            status_buy INTEGER NOT NULL,
            status_sell INTEGER NOT NULL,
            container_sizes TEXT NOT NULL,
            observed_utc INTEGER NOT NULL,
            terminal_name TEXT NOT NULL,
            commodity_name TEXT NOT NULL,
            last_seen_utc INTEGER NOT NULL,
            PRIMARY KEY (terminal_id, commodity_id)
        );
        CREATE TABLE IF NOT EXISTS refined_prices (
            terminal_id INTEGER NOT NULL,
            commodity_id INTEGER NOT NULL,
            sell REAL NOT NULL,
            sell_avg_week REAL NOT NULL,
            game_version TEXT NOT NULL,
            observed_utc INTEGER NOT NULL,
            terminal_name TEXT NOT NULL,
            last_seen_utc INTEGER NOT NULL,
            PRIMARY KEY (terminal_id, commodity_id)
        );
        CREATE TABLE IF NOT EXISTS yields (
            terminal_id INTEGER NOT NULL,
            commodity_id INTEGER NOT NULL,
            bonus_pct INTEGER NOT NULL,
            bonus_pct_week INTEGER NOT NULL,
            observed_utc INTEGER NOT NULL,
            terminal_name TEXT NOT NULL,
            last_seen_utc INTEGER NOT NULL,
            PRIMARY KEY (terminal_id, commodity_id)
        );
        CREATE TABLE IF NOT EXISTS raw_prices (
            terminal_id INTEGER NOT NULL,
            commodity_id INTEGER NOT NULL,
            sell REAL NOT NULL,
            sell_avg_week REAL NOT NULL,
            game_version TEXT NOT NULL,
            observed_utc INTEGER NOT NULL,
            terminal_name TEXT NOT NULL,
            last_seen_utc INTEGER NOT NULL,
            PRIMARY KEY (terminal_id, commodity_id)
        );
        CREATE INDEX IF NOT EXISTS ix_trade_seen ON trade_prices(last_seen_utc);
        CREATE INDEX IF NOT EXISTS ix_refined_seen ON refined_prices(last_seen_utc);
        CREATE INDEX IF NOT EXISTS ix_yield_seen ON yields(last_seen_utc);
        CREATE INDEX IF NOT EXISTS ix_term_seen ON terminals(last_seen_utc);
        CREATE INDEX IF NOT EXISTS ix_comm_seen ON commodities(last_seen_utc);
    ");

    private void MergeCommoditiesCore(IReadOnlyList<CatalogCommodity> rows, DateTime fetchedUtc)
    {
        const string sql = @"
            INSERT INTO commodities (id, name, slug, is_raw, is_refined, parent_id, last_seen_utc)
            VALUES (@id, @name, @slug, @raw, @refined, @parent, @seen)
            ON CONFLICT(id) DO UPDATE SET
                name = excluded.name,
                slug = excluded.slug,
                is_raw = excluded.is_raw,
                is_refined = excluded.is_refined,
                parent_id = excluded.parent_id,
                last_seen_utc = excluded.last_seen_utc;";
        foreach (var row in rows)
        {
            Exec(sql,
                ("@id", row.Id), ("@name", row.Name), ("@slug", row.Slug),
                ("@raw", row.IsRaw ? 1 : 0), ("@refined", row.IsRefined ? 1 : 0),
                ("@parent", row.ParentId), ("@seen", Ticks(fetchedUtc)));
        }
        UpsertMetaSuccess(ProviderDataClass.Commodities, fetchedUtc, default);
    }

    private void MergeTerminalsCore(IReadOnlyList<CatalogTerminal> rows, DateTime fetchedUtc)
    {
        const string sql = @"
            INSERT INTO terminals (id, name, type, is_refinery, system, location, orbit, planet_or_moon, last_seen_utc)
            VALUES (@id, @name, @type, @refinery, @system, @location, @orbit, @planet, @seen)
            ON CONFLICT(id) DO UPDATE SET
                name = excluded.name,
                type = excluded.type,
                is_refinery = excluded.is_refinery,
                system = excluded.system,
                location = excluded.location,
                orbit = excluded.orbit,
                planet_or_moon = excluded.planet_or_moon,
                last_seen_utc = excluded.last_seen_utc;";
        foreach (var row in rows)
        {
            Exec(sql,
                ("@id", row.Id), ("@name", row.Name), ("@type", row.Type),
                ("@refinery", row.IsRefinery ? 1 : 0), ("@system", row.System),
                ("@location", row.Location), ("@orbit", row.Orbit),
                ("@planet", row.PlanetOrMoon), ("@seen", Ticks(fetchedUtc)));
        }
        UpsertMetaSuccess(ProviderDataClass.Terminals, fetchedUtc, default);
    }

    private void MergeTradePricesCore(IReadOnlyList<CatalogTradePrice> rows, DateTime fetchedUtc)
    {
        const string sql = @"
            INSERT INTO trade_prices (
                terminal_id, commodity_id, buy, sell, buy_stock_scu, sell_demand_scu,
                status_buy, status_sell, container_sizes, observed_utc, terminal_name, commodity_name, last_seen_utc)
            VALUES (
                @tid, @cid, @buy, @sell, @stock, @demand, @sbuy, @ssell, @sizes, @obs, @tname, @cname, @seen)
            ON CONFLICT(terminal_id, commodity_id) DO UPDATE SET
                buy = CASE WHEN excluded.observed_utc > observed_utc THEN excluded.buy ELSE buy END,
                sell = CASE WHEN excluded.observed_utc > observed_utc THEN excluded.sell ELSE sell END,
                buy_stock_scu = CASE WHEN excluded.observed_utc > observed_utc THEN excluded.buy_stock_scu ELSE buy_stock_scu END,
                sell_demand_scu = CASE WHEN excluded.observed_utc > observed_utc THEN excluded.sell_demand_scu ELSE sell_demand_scu END,
                status_buy = CASE WHEN excluded.observed_utc > observed_utc THEN excluded.status_buy ELSE status_buy END,
                status_sell = CASE WHEN excluded.observed_utc > observed_utc THEN excluded.status_sell ELSE status_sell END,
                container_sizes = CASE WHEN excluded.observed_utc > observed_utc THEN excluded.container_sizes ELSE container_sizes END,
                terminal_name = CASE WHEN excluded.observed_utc > observed_utc THEN excluded.terminal_name ELSE terminal_name END,
                commodity_name = CASE WHEN excluded.observed_utc > observed_utc THEN excluded.commodity_name ELSE commodity_name END,
                observed_utc = CASE WHEN excluded.observed_utc > observed_utc THEN excluded.observed_utc ELSE observed_utc END,
                last_seen_utc = excluded.last_seen_utc;";
        long maxObs = 0;
        foreach (var row in rows)
        {
            var obs = Ticks(row.ObservedUtc);
            if (obs > maxObs) maxObs = obs;
            Exec(sql,
                ("@tid", row.TerminalId), ("@cid", row.CommodityId),
                ("@buy", row.Buy), ("@sell", row.Sell),
                ("@stock", row.BuyStockScu), ("@demand", row.SellDemandScu),
                ("@sbuy", row.StatusBuy), ("@ssell", row.StatusSell),
                ("@sizes", row.ContainerSizes), ("@obs", obs),
                ("@tname", row.TerminalName), ("@cname", row.CommodityName),
                ("@seen", Ticks(fetchedUtc)));
        }
        UpsertMetaSuccess(ProviderDataClass.TradePrices, fetchedUtc, FromTicks(maxObs));
    }

    private void MergeRefinedPricesCore(IReadOnlyList<CatalogRefinedPrice> rows, DateTime fetchedUtc)
    {
        const string sql = @"
            INSERT INTO refined_prices (
                terminal_id, commodity_id, sell, sell_avg_week, game_version, observed_utc, terminal_name, last_seen_utc)
            VALUES (@tid, @cid, @sell, @avg, @ver, @obs, @tname, @seen)
            ON CONFLICT(terminal_id, commodity_id) DO UPDATE SET
                sell = CASE WHEN excluded.observed_utc > observed_utc THEN excluded.sell ELSE sell END,
                sell_avg_week = CASE WHEN excluded.observed_utc > observed_utc THEN excluded.sell_avg_week ELSE sell_avg_week END,
                game_version = CASE WHEN excluded.observed_utc > observed_utc THEN excluded.game_version ELSE game_version END,
                terminal_name = CASE WHEN excluded.observed_utc > observed_utc THEN excluded.terminal_name ELSE terminal_name END,
                observed_utc = CASE WHEN excluded.observed_utc > observed_utc THEN excluded.observed_utc ELSE observed_utc END,
                last_seen_utc = excluded.last_seen_utc;";
        long maxObs = 0;
        foreach (var row in rows)
        {
            var obs = Ticks(row.ObservedUtc);
            if (obs > maxObs) maxObs = obs;
            Exec(sql,
                ("@tid", row.TerminalId), ("@cid", row.CommodityId),
                ("@sell", row.Sell), ("@avg", row.SellAvgWeek),
                ("@ver", row.GameVersion), ("@obs", obs),
                ("@tname", row.TerminalName), ("@seen", Ticks(fetchedUtc)));
        }
        UpsertMetaSuccess(ProviderDataClass.RefinedPrices, fetchedUtc, FromTicks(maxObs));
    }

    private void MergeYieldsCore(IReadOnlyList<CatalogYield> rows, DateTime fetchedUtc)
    {
        const string sql = @"
            INSERT INTO yields (
                terminal_id, commodity_id, bonus_pct, bonus_pct_week, observed_utc, terminal_name, last_seen_utc)
            VALUES (@tid, @cid, @bonus, @week, @obs, @tname, @seen)
            ON CONFLICT(terminal_id, commodity_id) DO UPDATE SET
                bonus_pct = CASE WHEN excluded.observed_utc > observed_utc THEN excluded.bonus_pct ELSE bonus_pct END,
                bonus_pct_week = CASE WHEN excluded.observed_utc > observed_utc THEN excluded.bonus_pct_week ELSE bonus_pct_week END,
                terminal_name = CASE WHEN excluded.observed_utc > observed_utc THEN excluded.terminal_name ELSE terminal_name END,
                observed_utc = CASE WHEN excluded.observed_utc > observed_utc THEN excluded.observed_utc ELSE observed_utc END,
                last_seen_utc = excluded.last_seen_utc;";
        long maxObs = 0;
        foreach (var row in rows)
        {
            var obs = Ticks(row.ObservedUtc);
            if (obs > maxObs) maxObs = obs;
            Exec(sql,
                ("@tid", row.TerminalId), ("@cid", row.CommodityId),
                ("@bonus", row.BonusPct), ("@week", row.BonusPctWeek),
                ("@obs", obs), ("@tname", row.TerminalName),
                ("@seen", Ticks(fetchedUtc)));
        }
        UpsertMetaSuccess(ProviderDataClass.Yields, fetchedUtc, FromTicks(maxObs));
    }

    private void MergeRawPricesCore(IReadOnlyList<CatalogRawPrice> rows, DateTime fetchedUtc)
    {
        const string sql = @"
            INSERT INTO raw_prices (
                terminal_id, commodity_id, sell, sell_avg_week, game_version, observed_utc, terminal_name, last_seen_utc)
            VALUES (@tid, @cid, @sell, @avg, @ver, @obs, @tname, @seen)
            ON CONFLICT(terminal_id, commodity_id) DO UPDATE SET
                sell = CASE WHEN excluded.observed_utc > observed_utc THEN excluded.sell ELSE sell END,
                sell_avg_week = CASE WHEN excluded.observed_utc > observed_utc THEN excluded.sell_avg_week ELSE sell_avg_week END,
                game_version = CASE WHEN excluded.observed_utc > observed_utc THEN excluded.game_version ELSE game_version END,
                terminal_name = CASE WHEN excluded.observed_utc > observed_utc THEN excluded.terminal_name ELSE terminal_name END,
                observed_utc = CASE WHEN excluded.observed_utc > observed_utc THEN excluded.observed_utc ELSE observed_utc END,
                last_seen_utc = excluded.last_seen_utc;";
        long maxObs = 0;
        foreach (var row in rows)
        {
            var obs = Ticks(row.ObservedUtc);
            if (obs > maxObs) maxObs = obs;
            Exec(sql,
                ("@tid", row.TerminalId), ("@cid", row.CommodityId),
                ("@sell", row.Sell), ("@avg", row.SellAvgWeek),
                ("@ver", row.GameVersion), ("@obs", obs),
                ("@tname", row.TerminalName), ("@seen", Ticks(fetchedUtc)));
        }
        UpsertMetaSuccess(ProviderDataClass.RawPrices, fetchedUtc, FromTicks(maxObs));
    }

    private void SetLiveGameVersionCore(string version, DateTime fetchedUtc)
    {
        Exec(@"
            INSERT INTO slice_meta (data_class, source, fetched_utc, observed_utc, last_failure_utc, live_game_version)
            VALUES (@cls, @src, @fetched, 0, 0, @ver)
            ON CONFLICT(data_class) DO UPDATE SET
                fetched_utc = excluded.fetched_utc,
                last_failure_utc = 0,
                live_game_version = excluded.live_game_version;",
            ("@cls", Name(ProviderDataClass.GameVersion)),
            ("@src", SourceUex),
            ("@fetched", Ticks(fetchedUtc)),
            ("@ver", version));
    }

    private void UpsertMetaSuccess(ProviderDataClass dataClass, DateTime fetchedUtc, DateTime observedUtc)
    {
        Exec(@"
            INSERT INTO slice_meta (data_class, source, fetched_utc, observed_utc, last_failure_utc, live_game_version)
            VALUES (@cls, @src, @fetched, @obs, 0, NULL)
            ON CONFLICT(data_class) DO UPDATE SET
                fetched_utc = excluded.fetched_utc,
                observed_utc = excluded.observed_utc,
                last_failure_utc = 0;",
            ("@cls", Name(dataClass)),
            ("@src", SourceUex),
            ("@fetched", Ticks(fetchedUtc)),
            ("@obs", Ticks(observedUtc)));
    }

    private void UpsertMetaFailure(ProviderDataClass dataClass, DateTime failedUtc)
    {
        Exec(@"
            INSERT INTO slice_meta (data_class, source, fetched_utc, observed_utc, last_failure_utc, live_game_version)
            VALUES (@cls, @src, 0, 0, @fail, NULL)
            ON CONFLICT(data_class) DO UPDATE SET
                last_failure_utc = excluded.last_failure_utc;",
            ("@cls", Name(dataClass)),
            ("@src", SourceUex),
            ("@fail", Ticks(failedUtc)));
    }

    private SliceMeta ReadMeta(ProviderDataClass dataClass)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT fetched_utc, observed_utc, last_failure_utc, live_game_version FROM slice_meta WHERE data_class = @cls;";
        cmd.Parameters.AddWithValue("@cls", Name(dataClass));
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return default;
        return new SliceMeta(
            FromTicks(r.GetInt64(0)),
            FromTicks(r.GetInt64(1)),
            FromTicks(r.GetInt64(2)),
            r.IsDBNull(3) ? null : r.GetString(3));
    }

    private List<CatalogCommodity> ReadCommodities(bool currentOnly)
    {
        var sql = currentOnly
            ? "SELECT id, name, slug, is_raw, is_refined, parent_id FROM commodities WHERE last_seen_utc = (SELECT fetched_utc FROM slice_meta WHERE data_class = @cls);"
            : "SELECT id, name, slug, is_raw, is_refined, parent_id FROM commodities;";
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@cls", Name(ProviderDataClass.Commodities));
        using var r = cmd.ExecuteReader();
        var list = new List<CatalogCommodity>();
        while (r.Read())
            list.Add(new CatalogCommodity(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetInt32(3) != 0, r.GetInt32(4) != 0, r.GetInt32(5)));
        return list;
    }

    private List<CatalogTerminal> ReadTerminals(bool currentOnly)
    {
        var sql = currentOnly
            ? "SELECT id, name, type, is_refinery, system, location, orbit, planet_or_moon FROM terminals WHERE last_seen_utc = (SELECT fetched_utc FROM slice_meta WHERE data_class = @cls);"
            : "SELECT id, name, type, is_refinery, system, location, orbit, planet_or_moon FROM terminals;";
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@cls", Name(ProviderDataClass.Terminals));
        using var r = cmd.ExecuteReader();
        var list = new List<CatalogTerminal>();
        while (r.Read())
            list.Add(new CatalogTerminal(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetInt32(3) != 0, r.GetString(4), r.GetString(5), r.GetString(6), r.GetString(7)));
        return list;
    }

    private List<CatalogTradePrice> ReadTradePrices(bool currentOnly)
    {
        var sql = currentOnly
            ? @"SELECT terminal_id, commodity_id, buy, sell, buy_stock_scu, sell_demand_scu, status_buy, status_sell, container_sizes, observed_utc, terminal_name, commodity_name
               FROM trade_prices WHERE last_seen_utc = (SELECT fetched_utc FROM slice_meta WHERE data_class = @cls);"
            : @"SELECT terminal_id, commodity_id, buy, sell, buy_stock_scu, sell_demand_scu, status_buy, status_sell, container_sizes, observed_utc, terminal_name, commodity_name
               FROM trade_prices;";
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@cls", Name(ProviderDataClass.TradePrices));
        using var r = cmd.ExecuteReader();
        var list = new List<CatalogTradePrice>();
        while (r.Read())
            list.Add(new CatalogTradePrice(r.GetInt32(0), r.GetInt32(1), r.GetDouble(2), r.GetDouble(3), r.GetInt32(4), r.GetInt32(5), r.GetInt32(6), r.GetInt32(7), r.GetString(8), FromTicks(r.GetInt64(9)), r.GetString(10), r.GetString(11)));
        return list;
    }

    private List<CatalogRefinedPrice> ReadRefinedPrices(bool currentOnly)
    {
        var sql = currentOnly
            ? @"SELECT terminal_id, commodity_id, sell, sell_avg_week, game_version, observed_utc, terminal_name
               FROM refined_prices WHERE last_seen_utc = (SELECT fetched_utc FROM slice_meta WHERE data_class = @cls);"
            : @"SELECT terminal_id, commodity_id, sell, sell_avg_week, game_version, observed_utc, terminal_name
               FROM refined_prices;";
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@cls", Name(ProviderDataClass.RefinedPrices));
        using var r = cmd.ExecuteReader();
        var list = new List<CatalogRefinedPrice>();
        while (r.Read())
            list.Add(new CatalogRefinedPrice(r.GetInt32(0), r.GetInt32(1), r.GetDouble(2), r.GetDouble(3), r.GetString(4), FromTicks(r.GetInt64(5)), r.GetString(6)));
        return list;
    }

    private List<CatalogYield> ReadYields(bool currentOnly)
    {
        var sql = currentOnly
            ? @"SELECT terminal_id, commodity_id, bonus_pct, bonus_pct_week, observed_utc, terminal_name
               FROM yields WHERE last_seen_utc = (SELECT fetched_utc FROM slice_meta WHERE data_class = @cls);"
            : @"SELECT terminal_id, commodity_id, bonus_pct, bonus_pct_week, observed_utc, terminal_name
               FROM yields;";
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@cls", Name(ProviderDataClass.Yields));
        using var r = cmd.ExecuteReader();
        var list = new List<CatalogYield>();
        while (r.Read())
            list.Add(new CatalogYield(r.GetInt32(0), r.GetInt32(1), r.GetInt32(2), r.GetInt32(3), FromTicks(r.GetInt64(4)), r.GetString(5)));
        return list;
    }

    private List<CatalogRawPrice> ReadRawPrices(bool currentOnly)
    {
        var sql = currentOnly
            ? @"SELECT terminal_id, commodity_id, sell, sell_avg_week, game_version, observed_utc, terminal_name
               FROM raw_prices WHERE last_seen_utc = (SELECT fetched_utc FROM slice_meta WHERE data_class = @cls);"
            : @"SELECT terminal_id, commodity_id, sell, sell_avg_week, game_version, observed_utc, terminal_name
               FROM raw_prices;";
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@cls", Name(ProviderDataClass.RawPrices));
        using var r = cmd.ExecuteReader();
        var list = new List<CatalogRawPrice>();
        while (r.Read())
            list.Add(new CatalogRawPrice(r.GetInt32(0), r.GetInt32(1), r.GetDouble(2), r.GetDouble(3), r.GetString(4), FromTicks(r.GetInt64(5)), r.GetString(6)));
        return list;
    }

    private void Exec(string sql, params (string, object)[] parms)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (k, v) in parms) cmd.Parameters.AddWithValue(k, v);
        cmd.ExecuteNonQuery();
    }

    private int ScalarInt(string sql)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static string Name(ProviderDataClass dataClass) => dataClass.ToString();
    private static long Ticks(DateTime utc) => utc == default ? 0 : utc.Ticks;
    private static DateTime FromTicks(long ticks) => ticks <= 0 ? default : new DateTime(ticks, DateTimeKind.Utc);

    private static DateTime NewestNonDefault(params DateTime[] stamps)
    {
        var max = default(DateTime);
        foreach (var s in stamps)
        {
            if (s != default && s > max) max = s;
        }
        return max;
    }

    private readonly record struct SliceMeta(DateTime FetchedUtc, DateTime ObservedUtc, DateTime LastFailureUtc, string? LiveGameVersion);
}
