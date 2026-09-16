using NexusApp.Models;

namespace NexusApp.Services;

/// <summary>
/// Immutable last-known location slice published into <see cref="GameState"/>.
///
/// A null <see cref="Label"/> is an honest "no readable place is known" state. <see cref="SeenUtc"/>
/// may still be populated when Game.log reports location activity with only numeric ids; that signal
/// refreshes age/freshness without inventing a place name.
/// </summary>
public sealed record GameLocationState(
    string? Label,
    string? UexLocation,
    string? RawToken,
    bool IsJurisdiction,
    DateTime? SeenUtc)
{
    public static GameLocationState Empty { get; } = new(null, null, null, false, null);
    public bool HasLocation => !string.IsNullOrWhiteSpace(Label);
}

/// <summary>
/// Immutable session slice published into <see cref="GameState"/>.
///
/// <see cref="IsLive"/> is the game-process probe from <c>GameLogFeed</c>, not
/// <c>GameLogSession.IsSessionLive</c> (that flag is attachment-sensitive).
/// <see cref="LogGeneration"/> increments on a Game.log reset / new SC log session. It does not
/// clear location, shard history, wallet, or other slices.
/// </summary>
public sealed record GameSessionState(
    bool IsLive,
    GameChannel Channel,
    int LogGeneration)
{
    public static GameSessionState Empty { get; } = new(false, GameChannel.Live, 0);
}

/// <summary>
/// Immutable current-shard slice published into <see cref="GameState"/>.
///
/// When <see cref="OnShard"/> is false, identity fields are null. Recent shard history stays on
/// <c>ShardTracker</c>; this snapshot is only the live-or-not current shard.
/// </summary>
public sealed record GameShardState(
    bool OnShard,
    string? ShardId,
    string? Region,
    string? Instance,
    string? Channel,
    DateTime? JoinedUtc)
{
    public static GameShardState Empty { get; } = new(false, null, null, null, null, null);
}

/// <summary>One incomplete pickup or dropoff stop projected from the haul consolidation.</summary>
public sealed record GameHaulStop(string Location, string Commodity, int Scu, string MissionId);

/// <summary>Immutable identity of one haul. The tracker remains the owner of mutable leg detail.</summary>
public sealed record GameHaulSummary(
    string MissionId,
    string Company,
    bool IsActive,
    HaulOutcome Outcome);

/// <summary>
/// Immutable hauling slice published into <see cref="GameState"/>.
///
/// This is contract/objective state, not a cargo inventory. Active hauls and their incomplete
/// consolidated stops are projected from <c>HaulTracker</c>. The tracker still clears on log
/// reset, PU exit, and shard change.
/// </summary>
public sealed record GameHaulingState(
    IReadOnlyList<GameHaulSummary> Hauls,
    IReadOnlyList<GameHaulStop> Pickups,
    IReadOnlyList<GameHaulStop> Dropoffs)
{
    public static GameHaulingState Empty { get; } = new(
        Array.Empty<GameHaulSummary>(),
        Array.Empty<GameHaulStop>(),
        Array.Empty<GameHaulStop>());

    public bool HasActiveHauls => Hauls.Any(h => h.IsActive);

    public bool Equals(GameHaulingState? other) =>
        other is not null
        && Hauls.SequenceEqual(other.Hauls)
        && Pickups.SequenceEqual(other.Pickups)
        && Dropoffs.SequenceEqual(other.Dropoffs);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var haul in Hauls) hash.Add(haul);
        foreach (var stop in Pickups) hash.Add(stop);
        foreach (var stop in Dropoffs) hash.Add(stop);
        return hash.ToHashCode();
    }
}

/// <summary>How the current wallet anchor was obtained. None means no trusted balance exists.</summary>
public enum GameWalletProvenance { None, Manual, Ocr }

/// <summary>
/// Immutable wallet slice published into <see cref="GameState"/>.
///
/// <see cref="Estimate"/> is derived (anchor plus later settlements). The tracker remains the
/// owner of per-channel persistence and untracked rows. A log reset keeps the anchor and
/// estimate; only the session-scoped untracked display count rolls.
/// </summary>
public sealed record GameWalletState(
    bool HasAnchor,
    long? Estimate,
    long? Anchor,
    DateTime? AnchorUtc,
    GameWalletProvenance Provenance,
    int SessionUntrackedCount)
{
    public static GameWalletState Empty { get; } = new(false, null, null, null, GameWalletProvenance.None, 0);
}

/// <summary>How a decoded RS scan classified its top resource.</summary>
public enum GameMiningMatchKind { None, Close, Exact }

/// <summary>One unfiltered RS decode hit. Cart and filter state stay in the view model.</summary>
public sealed record GameMiningHit(
    string ResourceName,
    string Method,
    int Nodes,
    bool IsExact,
    double ErrorPct);

/// <summary>The last decoded RS value and its unfiltered hits.</summary>
public sealed record GameMiningScan(
    int Rs,
    string TopResource,
    GameMiningMatchKind Kind,
    DateTime SeenUtc,
    IReadOnlyList<GameMiningHit> Hits)
{
    public bool Equals(GameMiningScan? other) =>
        other is not null
        && Rs == other.Rs
        && TopResource == other.TopResource
        && Kind == other.Kind
        && SeenUtc == other.SeenUtc
        && Hits.SequenceEqual(other.Hits);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Rs);
        hash.Add(TopResource);
        hash.Add(Kind);
        hash.Add(SeenUtc);
        foreach (var hit in Hits) hash.Add(hit);
        return hash.ToHashCode();
    }
}

/// <summary>One recent-scan row, unfiltered.</summary>
public sealed record GameMiningHistoryEntry(int Rs, string TopResource, GameMiningMatchKind Kind);

/// <summary>
/// Immutable mining-scan slice published into <see cref="GameState"/>.
///
/// This is decoded RS context, not scanner mechanics or UI filter/selection state.
/// A log reset does not clear the last scan. <c>MainViewModel</c> still owns the WPF
/// collections and publishes here after a decode or clear.
/// </summary>
public sealed record GameMiningState(
    GameMiningScan? LastScan,
    IReadOnlyList<GameMiningHistoryEntry> Recent)
{
    public static GameMiningState Empty { get; } = new(null, Array.Empty<GameMiningHistoryEntry>());
    public bool HasScan => LastScan is not null;

    public bool Equals(GameMiningState? other) =>
        other is not null
        && Equals(LastScan, other.LastScan)
        && Recent.SequenceEqual(other.Recent);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(LastScan);
        foreach (var entry in Recent) hash.Add(entry);
        return hash.ToHashCode();
    }
}

/// <summary>One persisted refinery work order. Editor chrome and live timer display stay out.</summary>
public sealed record GameRefineryJob(
    string Id,
    string Label,
    string Resources,
    string Location,
    string Refinery,
    WorkOrderStatus Status,
    string Notes,
    DateTime CreatedAt,
    DateTime? TimerStart,
    DateTime? TimerEnd)
{
    public bool IsOpen => Status != WorkOrderStatus.Complete;
    public bool IsReady => Status == WorkOrderStatus.ReadyToCollect;
    public bool IsInProgress => Status is WorkOrderStatus.Mining or WorkOrderStatus.Refining;
}

/// <summary>
/// Immutable refinery-job slice published into <see cref="GameState"/>.
///
/// This is durable work-order context, not flyout filters or editor chrome.
/// A log reset does not clear jobs. <c>DataService</c> remains the persistence
/// owner and publishes here after load, save, delete, or clear.
/// </summary>
public sealed record GameRefineryState(IReadOnlyList<GameRefineryJob> Jobs)
{
    public static GameRefineryState Empty { get; } = new(Array.Empty<GameRefineryJob>());

    public bool HasOpenJobs => Jobs.Any(j => j.IsOpen);
    public int ReadyCount => Jobs.Count(j => j.IsReady);
    public int InProgressCount => Jobs.Count(j => j.IsInProgress);

    public bool Equals(GameRefineryState? other) =>
        other is not null && Jobs.SequenceEqual(other.Jobs);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var job in Jobs) hash.Add(job);
        return hash.ToHashCode();
    }
}

/// <summary>One persisted shopping-list row. Search text and cart-button UI stay out.</summary>
public sealed record GameShoppingItem(string ResourceName, double Quantity, string Unit);

/// <summary>
/// Immutable durable-goals slice published into <see cref="GameState"/>.
///
/// Shopping items come from <c>DataService</c>; owned blueprint names come from
/// <c>SettingsService</c>. Search, selection, and <c>GameLogSession</c> session
/// marks stay out. A log reset does not clear either list.
/// </summary>
public sealed record GameGoalsState(
    IReadOnlyList<GameShoppingItem> Shopping,
    IReadOnlyList<string> OwnedBlueprints)
{
    public static GameGoalsState Empty { get; } = new(
        Array.Empty<GameShoppingItem>(),
        Array.Empty<string>());

    public bool HasShopping => Shopping.Count > 0;
    public bool HasOwnedBlueprints => OwnedBlueprints.Count > 0;

    public bool Owns(string name) =>
        OwnedBlueprints.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));

    public bool Equals(GameGoalsState? other) =>
        other is not null
        && Shopping.SequenceEqual(other.Shopping)
        && OwnedBlueprints.SequenceEqual(other.OwnedBlueprints);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var item in Shopping) hash.Add(item);
        foreach (var name in OwnedBlueprints) hash.Add(name);
        return hash.ToHashCode();
    }
}

/// <summary>How the active ship was confirmed. None means no hangar selection exists.</summary>
public enum GameActiveShipProvenance { None, Hangar }

/// <summary>
/// Immutable active-ship slice published into <see cref="GameState"/>.
///
/// Disk (My Hangar + <c>AppSettings.ActiveShipId</c>) is the store of record. This snapshot is
/// the live observation other modules read. It is not the hangar list and not a cargo inventory.
/// <see cref="UsableCargoScu"/> is catalog/trade capacity until loadouts exist.
/// </summary>
public sealed record GameActiveShipState(
    string? ShipId,
    string? DisplayName,
    int? UsableCargoScu,
    GameActiveShipProvenance Provenance)
{
    public static GameActiveShipState Empty { get; } = new(null, null, null, GameActiveShipProvenance.None);
    public bool HasShip => !string.IsNullOrWhiteSpace(ShipId);
}

/// <summary>How a cargo lot was obtained. None is not a stored lot.</summary>
public enum GameCargoProvenance { None, UserConfirmed, OcrObserved, Inferred, Transaction }

/// <summary>
/// One quantity of a commodity believed to be aboard a hangar ship.
///
/// This is inventory, not a contract obligation and not a cargo-grid placement.
/// <see cref="Contracted"/> is a user flag that this lot is for a haul, not a
/// haul card. <see cref="Destination"/> is a free-text note; it is not linked to
/// a terminal, haul stop, or contract until a later issue.
/// </summary>
public sealed record GameCargoLot(
    string Id,
    string ShipId,
    string Commodity,
    int? UexCommodityId,
    int Scu,
    int? ContainerScu,
    int? ContainerCount,
    GameCargoProvenance Provenance,
    double? Confidence,
    DateTime ObservedUtc,
    bool OffGrid = false,
    bool Contracted = false,
    string Destination = "");

/// <summary>
/// Immutable carried-cargo slice published into <see cref="GameState"/>.
///
/// Disk (<c>cargo_lots</c> in nexus.db) is the store of record. This snapshot is the live
/// observation for the active hangar ship. Contract stops stay on <see cref="GameHaulingState"/>.
/// Used and free SCU are derived from lots plus <see cref="GameActiveShipState.UsableCargoScu"/>.
/// </summary>
public sealed record GameCargoState(
    string? ShipId,
    string? DisplayName,
    IReadOnlyList<GameCargoLot> Lots,
    int UsedScu,
    int? UsableScu,
    int? FreeScu,
    bool IsOverCapacity)
{
    public static GameCargoState Empty { get; } = new(
        null, null, Array.Empty<GameCargoLot>(), 0, null, null, false);

    public bool HasShip => !string.IsNullOrWhiteSpace(ShipId);
    public bool HasLots => Lots.Count > 0;

    public bool Equals(GameCargoState? other) =>
        other is not null
        && ShipId == other.ShipId
        && DisplayName == other.DisplayName
        && UsedScu == other.UsedScu
        && UsableScu == other.UsableScu
        && FreeScu == other.FreeScu
        && IsOverCapacity == other.IsOverCapacity
        && Lots.SequenceEqual(other.Lots);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ShipId);
        hash.Add(DisplayName);
        hash.Add(UsedScu);
        hash.Add(UsableScu);
        hash.Add(FreeScu);
        hash.Add(IsOverCapacity);
        foreach (var lot in Lots) hash.Add(lot);
        return hash.ToHashCode();
    }
}

/// <summary>
/// App-lifetime observable operational state for SC-navLink.
///
/// Issue #3 introduces this incrementally. Existing domain trackers remain responsible for
/// parsing and reconciliation and publish their resulting facts here. Consumers read immutable
/// snapshots instead of taking ownership of duplicate live state. Additional slices are added
/// only when their lifecycle and reset semantics are defined.
/// </summary>
public sealed class GameState
{
    private readonly object _gate = new();
    private GameLocationState _location = GameLocationState.Empty;
    private GameSessionState _session = GameSessionState.Empty;
    private GameShardState _shard = GameShardState.Empty;
    private GameHaulingState _hauling = GameHaulingState.Empty;
    private GameWalletState _wallet = GameWalletState.Empty;
    private GameMiningState _mining = GameMiningState.Empty;
    private GameRefineryState _refinery = GameRefineryState.Empty;
    private GameGoalsState _goals = GameGoalsState.Empty;
    private GameActiveShipState _activeShip = GameActiveShipState.Empty;
    private GameCargoState _cargo = GameCargoState.Empty;

    /// <summary>Latest location snapshot. The returned record is immutable and safe to retain.</summary>
    public GameLocationState Location
    {
        get
        {
            lock (_gate) return _location;
        }
    }

    /// <summary>Latest session snapshot. The returned record is immutable and safe to retain.</summary>
    public GameSessionState Session
    {
        get
        {
            lock (_gate) return _session;
        }
    }

    /// <summary>Latest shard snapshot. The returned record is immutable and safe to retain.</summary>
    public GameShardState Shard
    {
        get
        {
            lock (_gate) return _shard;
        }
    }

    /// <summary>Latest hauling snapshot. The returned record is immutable and safe to retain.</summary>
    public GameHaulingState Hauling
    {
        get
        {
            lock (_gate) return _hauling;
        }
    }

    /// <summary>Latest wallet snapshot. The returned record is immutable and safe to retain.</summary>
    public GameWalletState Wallet
    {
        get
        {
            lock (_gate) return _wallet;
        }
    }

    /// <summary>Latest mining-scan snapshot. The returned record is immutable and safe to retain.</summary>
    public GameMiningState Mining
    {
        get
        {
            lock (_gate) return _mining;
        }
    }

    /// <summary>Latest refinery-job snapshot. The returned record is immutable and safe to retain.</summary>
    public GameRefineryState Refinery
    {
        get
        {
            lock (_gate) return _refinery;
        }
    }

    /// <summary>Latest active-ship snapshot. The returned record is immutable and safe to retain.</summary>
    public GameActiveShipState ActiveShip
    {
        get
        {
            lock (_gate) return _activeShip;
        }
    }

    /// <summary>Latest durable-goals snapshot. The returned record is immutable and safe to retain.</summary>
    public GameGoalsState Goals
    {
        get
        {
            lock (_gate) return _goals;
        }
    }

    /// <summary>Latest carried-cargo snapshot. The returned record is immutable and safe to retain.</summary>
    public GameCargoState Cargo
    {
        get
        {
            lock (_gate) return _cargo;
        }
    }

    /// <summary>Raised when any shared-state slice changes.</summary>
    public event Action? Changed;

    /// <summary>Raised when the location snapshot changes.</summary>
    public event Action? LocationChanged;

    /// <summary>Raised when the session snapshot changes.</summary>
    public event Action? SessionChanged;

    /// <summary>Raised when the shard snapshot changes.</summary>
    public event Action? ShardChanged;

    /// <summary>Raised when the hauling snapshot changes.</summary>
    public event Action? HaulingChanged;

    /// <summary>Raised when the wallet snapshot changes.</summary>
    public event Action? WalletChanged;

    /// <summary>Raised when the mining-scan snapshot changes.</summary>
    public event Action? MiningChanged;

    /// <summary>Raised when the refinery-job snapshot changes.</summary>
    public event Action? RefineryChanged;

    /// <summary>Raised when the durable-goals snapshot changes.</summary>
    public event Action? GoalsChanged;

    /// <summary>Raised when the active-ship snapshot changes.</summary>
    public event Action? ActiveShipChanged;

    /// <summary>Raised when the carried-cargo snapshot changes.</summary>
    public event Action? CargoChanged;

    /// <summary>
    /// Publish the result of the location domain service. Internal so ordinary consumers cannot
    /// mutate shared operational state; writers live in the same service/domain assembly.
    /// </summary>
    internal void PublishLocation(GameLocationState location)
        => Publish(ref _location, location, () => LocationChanged);

    /// <summary>Publish the result of the Game.log session / process-live probe.</summary>
    internal void PublishSession(GameSessionState session)
        => Publish(ref _session, session, () => SessionChanged);

    /// <summary>Publish the result of the shard domain service.</summary>
    internal void PublishShard(GameShardState shard)
        => Publish(ref _shard, shard, () => ShardChanged);

    /// <summary>Publish the result of the haul domain service.</summary>
    internal void PublishHauling(GameHaulingState hauling)
        => Publish(ref _hauling, hauling, () => HaulingChanged);

    /// <summary>Publish the result of the wallet domain service.</summary>
    internal void PublishWallet(GameWalletState wallet)
        => Publish(ref _wallet, wallet, () => WalletChanged);

    /// <summary>Publish the result of the mining-scan decode path.</summary>
    internal void PublishMining(GameMiningState mining)
        => Publish(ref _mining, mining, () => MiningChanged);

    /// <summary>Publish the result of the persisted refinery work-order set.</summary>
    internal void PublishRefinery(GameRefineryState refinery)
        => Publish(ref _refinery, refinery, () => RefineryChanged);

    /// <summary>Publish the full durable-goals snapshot.</summary>
    internal void PublishGoals(GameGoalsState goals)
        => Publish(ref _goals, goals, () => GoalsChanged);

    /// <summary>Publish the hangar-confirmed active ship and usable cargo capacity.</summary>
    internal void PublishActiveShip(GameActiveShipState activeShip)
        => Publish(ref _activeShip, activeShip, () => ActiveShipChanged);

    /// <summary>Publish the hangar-backed cargo believed to be aboard the active ship.</summary>
    internal void PublishCargo(GameCargoState cargo)
        => Publish(ref _cargo, cargo, () => CargoChanged);

    /// <summary>Replace the shopping list while keeping currently published owned blueprints.</summary>
    internal void PublishShopping(IReadOnlyList<GameShoppingItem> shopping)
    {
        ArgumentNullException.ThrowIfNull(shopping);
        GameGoalsState next;
        lock (_gate) next = new GameGoalsState(shopping, _goals.OwnedBlueprints);
        Publish(ref _goals, next, () => GoalsChanged);
    }

    /// <summary>Replace owned blueprints while keeping the currently published shopping list.</summary>
    internal void PublishOwnedBlueprints(IReadOnlyList<string> ownedBlueprints)
    {
        ArgumentNullException.ThrowIfNull(ownedBlueprints);
        GameGoalsState next;
        lock (_gate) next = new GameGoalsState(_goals.Shopping, ownedBlueprints);
        Publish(ref _goals, next, () => GoalsChanged);
    }

    private void Publish<T>(ref T field, T value, Func<Action?> sliceEvent) where T : class
    {
        ArgumentNullException.ThrowIfNull(value);

        Action? slice;
        Action? changed;
        lock (_gate)
        {
            if (Equals(field, value)) return;
            field = value;
            slice = sliceEvent();
            changed = Changed;
        }

        // Never invoke subscribers under the state lock: UI and service observers are free to
        // read GameState again from their callbacks without deadlocking the publisher.
        slice?.Invoke();
        changed?.Invoke();
    }
}
