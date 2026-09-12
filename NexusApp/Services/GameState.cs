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
