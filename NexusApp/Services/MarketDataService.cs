using System.IO;
using System.Net.Http;
using System.Text;

namespace NexusApp.Services;

// Seam between the fetch cycle and the network, so every branch of the cycle is testable with a
// fake and the real HTTP code stays in one small class (the IUpdateTransport pattern).
internal interface IMarketDataTransport
{
    Task<string> GetStringAsync(string url, int maxBytes, CancellationToken ct);
}

internal sealed class HttpMarketTransport : IMarketDataTransport
{
    // One client for the process: a new HttpClient per call leaks sockets. 15s covers a slow
    // endpoint; the streamed body read below carries its own token because HttpClient.Timeout
    // does not govern reads under ResponseHeadersRead.
    private static readonly HttpClient _http = Create();

    private static HttpClient Create()
    {
        var handler = new SocketsHttpHandler { AllowAutoRedirect = true, MaxAutomaticRedirections = 5 };
        var c = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        // Identifies the app to UEX. Carries the app version only, nothing about the user.
        c.DefaultRequestHeaders.UserAgent.ParseAdd($"NexusApp-Market/{NexusApp.AppInfo.Version}");
        return c;
    }

    public async Task<string> GetStringAsync(string url, int maxBytes, CancellationToken ct)
    {
        // Explicit time bound: a dribbling endpoint must not wedge the single-flight flag, and
        // the cycle's own deadline is linked in through ct.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(30));
        using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
        EnsureHttps(resp);
        resp.EnsureSuccessStatusCode();
        if (resp.Content.Headers.ContentLength is { } len && len > maxBytes)
            throw new InvalidOperationException("response larger than expected");
        await using var stream = await resp.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false);
        using var ms = new MemoryStream();
        await CopyCappedAsync(stream, ms, maxBytes, cts.Token).ConfigureAwait(false);
        return Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
    }

    // Defense in depth: even if the handler ever followed a downgrade redirect, the final
    // response must have arrived over https or it is discarded. The guarantee is ours,
    // not the framework's.
    private static void EnsureHttps(HttpResponseMessage resp)
    {
        if (resp.RequestMessage?.RequestUri?.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("response did not arrive over https");
    }

    // Copies at most maxBytes; one byte more aborts, so a lying or hostile endpoint cannot
    // flood memory. A Content-Length header is advisory and is not trusted on its own.
    private static async Task CopyCappedAsync(Stream from, Stream to, long maxBytes, CancellationToken ct)
    {
        var buf = new byte[81920];
        long total = 0;
        int n;
        while ((n = await from.ReadAsync(buf, ct).ConfigureAwait(false)) > 0)
        {
            total += n;
            if (total > maxBytes) throw new InvalidOperationException("response larger than expected");
            await to.WriteAsync(buf.AsMemory(0, n), ct).ConfigureAwait(false);
        }
    }
}

// Owns the UEX fetch cycle: the hourly throttle, single-flight, the whole-cycle deadline, and
// the in-memory snapshot that the UI reads. Durable rows live in ProviderCacheStore (SQLite).
// The cycle is deliberately fault tolerant per DATASET: one endpoint failing never costs the
// others their data, because stale prices with a visible age are worth more to a miner than an
// empty panel.
public sealed class MarketDataService : IDisposable, IMarketCatalog, IShipCatalog
{
    public const string Tag = "[NET]";
    public const string BaseUrl = "https://api.uexcorp.uk/2.0/";

    // The largest response (terminals, ~800 rows) is a few hundred KB today; 8 MB leaves room
    // for growth while still capping a hostile or broken endpoint.
    public const int MaxResponseBytes = 8 * 1024 * 1024;

    public static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(1);

    // Yields and terminals are reference data that changes with a game patch, not with trading.
    public static readonly TimeSpan ReferenceInterval = TimeSpan.FromHours(24);

    // A cycle is roughly 30 sequential requests: four whole-list endpoints and about 25 refined
    // prices (one per distinct refined parent id). Two minutes is a BACKSTOP against a wedged
    // endpoint, not a budget the cycle is expected to fit in comfortably; when it fires, every
    // dataset and every per-id fetch that already completed is still published.
    public static readonly TimeSpan CycleDeadline = TimeSpan.FromMinutes(2);

    private const string GameVersionsEndpoint = "game_versions";
    private const string CommoditiesEndpoint = "commodities";
    private const string RefinedPricesEndpoint = "commodities_prices";
    private const string YieldsEndpoint = "refineries_yields";
    private const string TerminalsEndpoint = "terminals";
    private const string CommoditiesPricesAllEndpoint = "commodities_prices_all";
    private const string VehiclesEndpoint = "vehicles";
    private const string VehiclePurchasesEndpoint = "vehicles_purchases_prices_all";
    private const string VehicleRentalsEndpoint = "vehicles_rentals_prices_all";

    private readonly SettingsService _settings;
    private readonly IMarketDataProvider _provider;
    private readonly ProviderCacheStore _cache;
    private readonly string _snapshotPath;
    private readonly bool _demo;
    private readonly Func<bool>? _isForegroundRelevant;

    // Cancelled by Dispose. Every cycle links its own deadline source to this one, so shutting
    // the service down also unwinds a cycle that is in flight.
    private readonly CancellationTokenSource _life = new();

    private int _busy;                          // Interlocked single-flight across the whole cycle
    // Completes when the in-flight cycle has finished ALL of its writes; null when idle. Dispose
    // waits on it so a cycle cannot write settings.json or the snapshot after App.OnExit.
    private volatile TaskCompletionSource<bool>? _cycleDone;
    private volatile MarketSnapshot? _snapshot;
    private volatile string? _lastError;
    private volatile bool _disposed;
    private bool _started;
    private System.Windows.Threading.DispatcherTimer? _timer;

    // The whole snapshot is swapped as one reference at the end of a cycle, never edited in
    // place, so a UI reader either sees the previous cycle's data or this cycle's, never a
    // half-updated mix. volatile makes that publication visible to other threads without a lock.
    internal MarketSnapshot? Snapshot => _snapshot;

    public string Source => ProviderCacheStore.SourceUex;

    public string LiveGameVersion => _snapshot?.LiveGameVersion ?? "";

    public CachedSlice<CatalogCommodity> Commodities =>
        _cache.Slice(ProviderDataClass.Commodities, _cache.CurrentCommodities(), DateTime.UtcNow);

    public CachedSlice<CatalogTerminal> Terminals =>
        _cache.Slice(ProviderDataClass.Terminals, _cache.CurrentTerminals(), DateTime.UtcNow);

    public CachedSlice<CatalogTradePrice> TradePrices =>
        _cache.Slice(ProviderDataClass.TradePrices, _cache.CurrentTradePrices(), DateTime.UtcNow);

    public CachedSlice<CatalogRefinedPrice> RefinedPrices =>
        _cache.Slice(ProviderDataClass.RefinedPrices, _cache.CurrentRefinedPrices(), DateTime.UtcNow);

    public CachedSlice<CatalogYield> Yields =>
        _cache.Slice(ProviderDataClass.Yields, _cache.CurrentYields(), DateTime.UtcNow);

    public CachedSlice<ShipCatalogEntry> Vehicles =>
        _cache.Slice(ProviderDataClass.Vehicles, _cache.CurrentVehicles(), DateTime.UtcNow);

    public CachedSlice<CatalogVehiclePurchase> Purchases =>
        _cache.Slice(ProviderDataClass.VehiclePurchases, _cache.CurrentVehiclePurchases(), DateTime.UtcNow);

    public CachedSlice<CatalogVehicleRental> Rentals =>
        _cache.Slice(ProviderDataClass.VehicleRentals, _cache.CurrentVehicleRentals(), DateTime.UtcNow);

    public ShipCatalogEntry? ById(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        foreach (var row in _cache.CurrentVehicles())
        {
            if (string.Equals(row.Id, id, StringComparison.OrdinalIgnoreCase))
                return row;
        }
        return null;
    }

    public IReadOnlyList<ShipCatalogEntry> Query(ShipCatalogQuery query) =>
        ShipCatalogQueries.Filter(_cache.CurrentVehicles(), query);

    public IReadOnlyList<CatalogVehiclePurchase> PurchasesFor(string catalogId)
    {
        var ship = ById(catalogId);
        if (ship is null) return [];
        var id = ship.ProviderId;
        return _cache.CurrentVehiclePurchases().Where(p => p.VehicleProviderId == id).ToList();
    }

    public IReadOnlyList<CatalogVehicleRental> RentalsFor(string catalogId)
    {
        var ship = ById(catalogId);
        if (ship is null) return [];
        var id = ship.ProviderId;
        return _cache.CurrentVehicleRentals().Where(r => r.VehicleProviderId == id).ToList();
    }

    public bool FetchInProgress => Volatile.Read(ref _busy) != 0;

    // One short sentence naming the first thing that went wrong in the last cycle, for the
    // Settings status row. Null after a clean cycle. Never an exception dump.
    public string? LastError => _lastError;

    // Raised on a worker thread once per cycle, after the new snapshot is published; UI
    // subscribers marshal with Dispatcher.Invoke themselves (the UpdateService.Changed contract).
    public event Action? Changed;

    public MarketDataService(SettingsService settings, Func<bool>? isForegroundRelevant = null)
        : this(settings, new RateLimitedTransport(new HttpMarketTransport()),
               Path.Combine(AppPaths.Root, "cache", "uex_snapshot.json"), AppPaths.IsDemoProfile,
               isForegroundRelevant)
    { }

    internal MarketDataService(SettingsService settings, IMarketDataTransport transport, string snapshotPath,
                               bool isDemoProfile, Func<bool>? isForegroundRelevant = null)
    {
        _settings = settings;
        _provider = new UexMarketProvider(transport);
        _snapshotPath = snapshotPath;
        var cacheDir = Path.GetDirectoryName(snapshotPath);
        var cachePath = string.IsNullOrEmpty(cacheDir)
            ? ProviderCacheStore.FileName
            : Path.Combine(cacheDir, ProviderCacheStore.FileName);
        _cache = new ProviderCacheStore(cachePath);
        _demo = isDemoProfile;
        _isForegroundRelevant = isForegroundRelevant;
    }

    // Pure gate for the automatic path: consent must be an explicit yes, the demo profile is
    // always inert, and a cycle runs as soon as EITHER hourly dataset is an hour old. Both
    // stamps, not the newest one across the snapshot: a partial cycle that refreshed only the
    // reference data would otherwise read as "fresh" and suppress the auto path for an hour
    // while every price surface sat empty (observed live, amendment 2026-07-27).
    internal static bool ShouldFetch(bool? enabled, bool isDemoProfile, DateTime commoditiesFetchedUtc,
                                     DateTime refinedPricesFetchedUtc, DateTime nowUtc)
    {
        if (isDemoProfile || enabled != true) return false;
        return IsStale(commoditiesFetchedUtc, nowUtc) || IsStale(refinedPricesFetchedUtc, nowUtc);
    }

    // A stamp in the FUTURE (a clock rollback, or data fetched while the clock was wrong) counts
    // as stale: otherwise the subtraction stays negative and the auto refresh freezes forever.
    private static bool IsStale(DateTime fetchedUtc, DateTime nowUtc) =>
        fetchedUtc > nowUtc || nowUtc - fetchedUtc >= RefreshInterval;

    // Called once from app startup, on the UI thread. The timer is created HERE and not in the
    // constructor so tests (and any non-UI caller) can build the service without a dispatcher.
    public void Start()
    {
        if (_started || _disposed) return;
        _started = true;

        LoadSnapshotFromDisk();
        MaybeAutoRefresh();

        try
        {
            _timer = new System.Windows.Threading.DispatcherTimer { Interval = RefreshInterval };
            _timer.Tick += (_, _) => { MaybeAutoRefresh(); RaiseAutoRefreshTick(); };
            _timer.Start();
        }
        catch (Exception ex)
        {
            // A missing dispatcher costs the hourly tick, not the feature: manual refresh and the
            // snapshot already on disk both still work.
            Logger.Error($"{Tag} market refresh timer could not start", ex);
        }
    }

    /// <summary>The one market refresh tick, announced so a sibling feed can ride the same clock
    /// instead of running a second timer (2026-08-03: one toggle and one refresh timer cover both
    /// feeds). Deliberately just "the cycle ran" and not "data changed" - a subscriber
    /// decides for itself whether it is due, so this service knows nothing about who listens.</summary>
    public event Action? AutoRefreshTick;

    // A subscriber must never be able to fault the cycle, same fail-closed contract RaiseChanged
    // documents. Type only in the log line: an exception Message can carry a %AppData% path.
    private void RaiseAutoRefreshTick()
    {
        try { AutoRefreshTick?.Invoke(); }
        catch (Exception ex) { Logger.Error($"{Tag} an auto-refresh tick subscriber threw ({ex.GetType().Name})"); }
    }

    // The disk half of Start, exposed as an internal seam so the load path is testable without
    // a dispatcher. SQLite is the durable store. A leftover uex_snapshot.json is imported once
    // when the cache is empty. A discarded file is an expected state (first run), not an error.
    internal void LoadSnapshotFromDisk()
    {
        try
        {
            if (!_cache.HasAnyRows() && File.Exists(_snapshotPath))
            {
                if (_cache.ImportSnapshotFile(_snapshotPath))
                    Logger.Info($"{Tag} market cache imported from the previous JSON snapshot");
                else
                    Logger.Info($"{Tag} market snapshot not loaded: JSON snapshot was not usable");
            }

            if (!_cache.HasAnyRows())
            {
                Logger.Info($"{Tag} market snapshot not loaded: no snapshot");
                return;
            }

            var loaded = _cache.ProjectSnapshot();
            _snapshot = loaded;
            Logger.Info($"{Tag} market snapshot loaded: {loaded.Commodities.Rows.Count} commodities, " +
                        $"{loaded.RawPrices.Rows.Count} raw prices, {loaded.RefinedPrices.Rows.Count} refined prices");
            RaiseChanged();
        }
        catch (Exception ex)
        {
            Logger.Error($"{Tag} market cache load failed ({ex.GetType().Name})");
        }
    }

    // The auto path: the toggle, the demo profile, and the hourly throttle all gate it. Fire and
    // forget on the thread pool so a UI-thread caller (startup, timer tick) never blocks.
    public void MaybeAutoRefresh()
    {
        if (_disposed) return;
        // Trading tab (2026-07-29): no background polling while neither Nexus nor Star Citizen
        // has focus. Reuses the existing foreground facility (App.IsForegroundRelevant) rather
        // than a new one; null (no func given, e.g. every pre-#trading-tab construction site and
        // every existing test) means "always relevant," so this is a no-op change for callers
        // that never opt in.
        if (_isForegroundRelevant is not null && !_isForegroundRelevant())
        {
            Logger.Info($"{Tag} market auto refresh skipped: Nexus/Star Citizen not in the foreground");
            return;
        }

        // Read the snapshot once: it can be swapped by a cycle finishing on another thread.
        var snap = _snapshot;
        if (!ShouldFetch(_settings.Current.MarketDataEnabled, _demo,
                         snap?.Commodities.FetchedUtc ?? DateTime.MinValue,
                         snap?.RefinedPrices.FetchedUtc ?? DateTime.MinValue,
                         DateTime.UtcNow)) return;

        _ = Task.Run(async () =>
        {
            try { await RefreshAsync(manual: false).ConfigureAwait(false); }
            catch (Exception ex) { Logger.Error($"{Tag} market auto refresh failed", ex); }
        });
    }

    // One fetch cycle. Runs regardless of the toggle when called directly: the Settings refresh
    // button is only reachable while market data is on, so the toggle gates the AUTO path only.
    public async Task RefreshAsync(bool manual)
    {
        if (_disposed) return;
        if (Interlocked.Exchange(ref _busy, 1) != 0) return;   // second caller returns without fetching

        var cycleRan = false;
        CancellationTokenSource? cts = null;
        // Published before any await so a Dispose racing this call always finds the cycle it
        // needs to wait for. Completed in the finally below, never faulted.
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _cycleDone = done;
        try
        {
            cts = CancellationTokenSource.CreateLinkedTokenSource(_life.Token);
            cts.CancelAfter(CycleDeadline);
            cycleRan = true;
            Logger.Info($"{Tag} market refresh started ({(manual ? "manual" : "auto")})");

            var utcNow = DateTime.UtcNow;
            var cycle = new CycleResult();

            try
            {
                await RunCycleAsync(utcNow, cycle, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Deadline (or shutdown): whatever completed still lands below.
                cycle.Note(null, "the refresh ran out of time before it finished");
                Logger.Error($"{Tag} market refresh stopped: the cycle exceeded {CycleDeadline.TotalMinutes:0} minutes");
            }
            catch (Exception ex)
            {
                // The per-endpoint helpers already swallow their own failures, so reaching here
                // means something unforeseen. The partial snapshot is still worth publishing.
                cycle.Note(null, Shorten(ex.Message));
                Logger.Error($"{Tag} market refresh failed", ex);
            }

            // Error first, THEN the snapshot: the snapshot swap is what a reader notices, so
            // publishing it last guarantees nobody pairs this cycle's data with the previous
            // cycle's error text.
            _lastError = cycle.FirstError;
            _snapshot = _cache.ProjectSnapshot();
            Logger.Info($"{Tag} market refresh finished: {cycle.Refreshed} datasets refreshed, {cycle.Failures} failed");
            RaiseChanged();
        }
        finally
        {
            // Stamped whether the cycle succeeded or not, so the Settings status line can say
            // when Nexus last TRIED. It does not drive the hourly throttle: ShouldFetch reads
            // the snapshot's own per-dataset stamps, so a cycle where every endpoint failed is
            // retried on the next tick rather than being suppressed for an hour by this stamp.
            if (cycleRan)
            {
                _settings.Current.LastMarketFetchUtc = DateTime.UtcNow;
                _settings.Save();
            }
            cts?.Dispose();
            Interlocked.Exchange(ref _busy, 0);
            // Last: Dispose waits on this, and everything it must not race (the snapshot file
            // and settings writes above) is finished by the time it completes.
            _cycleDone = null;
            done.TrySetResult(true);
        }
    }

    private async Task RunCycleAsync(DateTime utcNow, CycleResult cycle, CancellationToken ct)
    {
        var snap = _cache.ProjectSnapshot();

        // 1. Live game version: labels the prices in the UI ("patch 4.9"). A failure keeps the
        //    previous label rather than blanking it.
        var (live, liveError, liveMs, liveBytes) = await _provider.FetchLiveGameVersionAsync(ct).ConfigureAwait(false);
        if (live is not null)
        {
            _cache.SetLiveGameVersion(live, utcNow);
            Logger.Info($"{Tag} market fetch {GameVersionsEndpoint}: live {live} ({liveBytes} bytes, {liveMs}ms)");
        }
        else if (liveError is not null)
        {
            cycle.Note(GameVersionsEndpoint, liveError);
            Logger.Error($"{Tag} market fetch {GameVersionsEndpoint} failed: {liveError}");
            _cache.NoteFailure(ProviderDataClass.GameVersion, utcNow);
        }

        // 2. Commodity catalogue: also the input to the refined leg below.
        var commodities = await _provider.FetchCommoditiesAsync(ct).ConfigureAwait(false);
        ApplyCatalogFetch(CommoditiesEndpoint, commodities, utcNow, cycle, ProviderDataClass.Commodities,
            rows => _cache.MergeCommodities(rows, utcNow));

        snap = _cache.ProjectSnapshot();

        // 3. Refined prices, one call per refined commodity the seed data cares about.
        await FetchPricesByIdAsync("refined", RefinedIdsFor(snap.Commodities.Rows),
            snap.RefinedPrices, utcNow, cycle, ct).ConfigureAwait(false);

        // 4. Trade tab: every commodity at every terminal, one bulk call.
        var trade = await _provider.FetchTradePricesAsync(ct).ConfigureAwait(false);
        ApplyCatalogFetch(CommoditiesPricesAllEndpoint, trade, utcNow, cycle, ProviderDataClass.TradePrices,
            rows => _cache.MergeTradePrices(rows, utcNow));

        // 5. Reference data, on its own much slower clock.
        snap = _cache.ProjectSnapshot();
        if (utcNow - snap.Yields.FetchedUtc >= ReferenceInterval)
        {
            var yields = await _provider.FetchYieldsAsync(ct).ConfigureAwait(false);
            ApplyCatalogFetch(YieldsEndpoint, yields, utcNow, cycle, ProviderDataClass.Yields,
                rows => _cache.MergeYields(rows, utcNow));
        }

        snap = _cache.ProjectSnapshot();
        if (utcNow - snap.Terminals.FetchedUtc >= ReferenceInterval)
        {
            var terminals = await _provider.FetchTerminalsAsync(ct).ConfigureAwait(false);
            ApplyCatalogFetch(TerminalsEndpoint, terminals, utcNow, cycle, ProviderDataClass.Terminals,
                rows => _cache.MergeTerminals(rows, utcNow));
        }

        var vehicleFetched = _cache.FetchedUtc(ProviderDataClass.Vehicles);
        if (utcNow - vehicleFetched >= ProviderFreshnessRules.VehicleCadence)
        {
            var vehicles = await _provider.FetchVehiclesAsync(ct).ConfigureAwait(false);
            ApplyCatalogFetch(VehiclesEndpoint, vehicles, utcNow, cycle, ProviderDataClass.Vehicles,
                rows => _cache.MergeVehicles(rows, utcNow));

            var purchases = await _provider.FetchVehiclePurchasesAsync(ct).ConfigureAwait(false);
            ApplyCatalogFetch(VehiclePurchasesEndpoint, purchases, utcNow, cycle, ProviderDataClass.VehiclePurchases,
                rows => _cache.MergeVehiclePurchases(rows, utcNow));

            var rentals = await _provider.FetchVehicleRentalsAsync(ct).ConfigureAwait(false);
            ApplyCatalogFetch(VehicleRentalsEndpoint, rentals, utcNow, cycle, ProviderDataClass.VehicleRentals,
                rows => _cache.MergeVehicleRentals(rows, utcNow));
        }
    }

    private void ApplyCatalogFetch<T>(string endpoint, ProviderFetch<T> fetch, DateTime utcNow, CycleResult cycle,
                                      ProviderDataClass dataClass, Action<IReadOnlyList<T>> merge)
    {
        if (!fetch.Ok)
        {
            var reason = fetch.Error ?? "the response was not in the expected format";
            Logger.Error($"{Tag} market fetch {endpoint} failed: {reason}");
            cycle.Note(endpoint, reason);
            _cache.NoteFailure(dataClass, utcNow);
            return;
        }

        LogRows(endpoint, fetch.Rows.Count, fetch.Skipped, fetch.ByteCount, fetch.ElapsedMs);
        if (fetch.Rows.Count > 0)
        {
            merge(fetch.Rows);
            cycle.Refreshed++;
        }
        else
        {
            KeptOnEmpty(endpoint);
        }
    }

    // A price dataset is the union of per-commodity fetches (no bulk endpoint returns the full
    // row shape), so it merges instead of replacing: an id that failed keeps ONLY its own
    // previous rows, every id that succeeded replaces its own, and ids that are no longer mapped
    // drop out, which is how a commodity renamed or removed by a patch stops haunting the
    // dataset. The union is then written through merge-newer so last_seen matches FetchedUtc
    // for every id that belongs in the current listing.
    private async Task FetchPricesByIdAsync(string kind, List<int> ids,
                                            MarketDataset<MarketPriceRow> previous,
                                            DateTime utcNow, CycleResult cycle, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            Logger.Info($"{Tag} market fetch {RefinedPricesEndpoint} skipped: no mapped {kind} commodities yet");
            return;
        }

        var fresh = new List<MarketPriceRow>();
        var replaced = new HashSet<int>();
        try
        {
            foreach (var id in ids)
            {
                var label = $"{RefinedPricesEndpoint} id {id}";
                var fetch = await _provider.FetchRefinedPricesAsync(id, ct).ConfigureAwait(false);
                if (!fetch.Ok)
                {
                    var reason = fetch.Error ?? "the response was not in the expected format";
                    Logger.Error($"{Tag} market fetch {label} failed: {reason}");
                    cycle.Note(label, reason);
                    continue;
                }

                LogRows(label, fetch.Rows.Count, fetch.Skipped, fetch.ByteCount, fetch.ElapsedMs);
                if (fetch.Rows.Count == 0)
                {
                    KeptOnEmpty(label);
                    continue;
                }
                replaced.Add(id);
                foreach (var row in fetch.Rows)
                    fresh.Add(UexNormalizer.ToMarket(row));
            }
        }
        finally
        {
            MergeRefinedIntoCache(previous, ids, fresh, replaced, utcNow, cycle);
        }
    }

    private void MergeRefinedIntoCache(MarketDataset<MarketPriceRow> previous, List<int> ids, List<MarketPriceRow> fresh,
                                       HashSet<int> replaced, DateTime utcNow, CycleResult cycle)
    {
        if (replaced.Count == 0) return;

        var wanted = new HashSet<int>(ids);
        var merged = new List<MarketPriceRow>(fresh);
        foreach (var row in previous.Rows)
        {
            if (wanted.Contains(row.CommodityId) && !replaced.Contains(row.CommodityId)) merged.Add(row);
        }
        _cache.MergeRefinedPrices(merged.Select(UexNormalizer.RefinedPrice).ToList(), utcNow);
        cycle.Refreshed++;
    }

    // Seed resource -> UEX raw commodity (by name) -> its refined parent. Distinct, because
    // several raw ores can share a refined parent.
    private static List<int> RefinedIdsFor(List<MarketCommodity> commodities)
    {
        var ids = new List<int>();
        if (commodities.Count == 0) return ids;

        var byName = new Dictionary<string, MarketCommodity>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in commodities) byName.TryAdd(c.Name, c);

        var seen = new HashSet<int>();
        foreach (var uexRawName in MarketNameMap.SeedToUexRaw.Values)
        {
            if (!byName.TryGetValue(uexRawName, out var raw)) continue;
            var refined = MarketNameMap.RefinedFor(raw, commodities);
            if (refined is null || refined.Id <= 0) continue;
            if (seen.Add(refined.Id)) ids.Add(refined.Id);
        }
        return ids;
    }

    private static void LogRows(string endpoint, int kept, int skipped, int bytes, long ms) =>
        Logger.Info($"{Tag} market fetch {endpoint}: {kept} rows ({skipped} skipped, {bytes} bytes, {ms}ms)");

    private static void KeptOnEmpty(string endpoint) =>
        Logger.Info($"{Tag} market fetch {endpoint} returned no rows; keeping the previous rows");

    // Keeps LastError to one readable line: the Settings status row renders it inline.
    private static string Shorten(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return "unknown error";
        var line = message.Split('\n')[0].Trim();
        return line.Length <= 120 ? line : line[..120];
    }

    // A subscriber must never be able to fault the fetch cycle (fail-closed). The real case is a
    // UI handler calling Dispatcher.Invoke while the app is shutting down.
    private void RaiseChanged()
    {
        // Type only, no ex argument: a subscriber's exception Message can carry a full
        // %AppData% path (the Windows username) into nexus.log - same rule as the snapshot
        // file's own reason strings.
        try { Changed?.Invoke(); }
        catch (Exception ex) { Logger.Error($"{Tag} a market data subscriber threw ({ex.GetType().Name})"); }
    }

    // How long Dispose waits for a cancelled cycle to finish unwinding. Long enough for an
    // aborted request to throw and the snapshot save to complete, short enough that shutdown
    // never visibly stalls.
    private static readonly TimeSpan DisposeDrainTimeout = TimeSpan.FromSeconds(3);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // Stop must run on the timer's own dispatcher thread; on shutdown from anywhere else it
        // throws, and a failed stop on a service that is going away is not worth an error.
        try { _timer?.Stop(); } catch { /* best effort */ }
        _timer = null;

        var pending = _cycleDone?.Task;   // captured BEFORE the cancel, which may clear the field
        // Cancels the in-flight cycle through its linked source. _life is deliberately NOT
        // disposed: that cycle still holds a token derived from it.
        try { _life.Cancel(); } catch { /* best effort */ }

        // Then WAIT for it. Cancelling alone is not enough: the cycle still publishes what
        // landed and writes settings.json and the snapshot file on its way out, and those
        // writes must not race App.OnExit (which saves settings itself and then relaunches for
        // a portable update). Bounded, because a subscriber that blocks inside Changed must not
        // be able to hang shutdown: a Changed handler should marshal with BeginInvoke, since a
        // blocking Dispatcher.Invoke while the UI thread sits here costs the full timeout.
        try
        {
            if (pending is not null && !pending.Wait(DisposeDrainTimeout))
                Logger.Error($"{Tag} market refresh did not stop within {DisposeDrainTimeout.TotalSeconds:0}s; leaving it to finish");
        }
        catch (Exception ex) { Logger.Error($"{Tag} market refresh did not stop cleanly: {Shorten(ex.Message)}"); }
        _cache.Dispose();
    }

    // What one cycle did, so the end-of-cycle log line and LastError read from the same record.
    private sealed class CycleResult
    {
        public string? FirstError { get; private set; }
        public int Refreshed { get; set; }
        public int Failures { get; private set; }

        // The FIRST failure is the one the user sees: it is usually the cause, and the ones
        // after it are usually the same outage repeating.
        public void Note(string? endpoint, string reason)
        {
            Failures++;
            FirstError ??= endpoint is null ? reason : $"{endpoint} failed: {reason}";
        }
    }
}
