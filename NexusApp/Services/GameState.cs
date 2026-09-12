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
/// App-lifetime observable operational state for SC-navLink.
///
/// Issue #3 introduces this incrementally. The first slice is location only; existing domain
/// trackers remain responsible for parsing and reconciliation and publish their resulting facts
/// here. Consumers read immutable snapshots instead of taking ownership of duplicate live state.
/// Additional slices are added only when their lifecycle and reset semantics are defined.
/// </summary>
public sealed class GameState
{
    private readonly object _gate = new();
    private GameLocationState _location = GameLocationState.Empty;

    /// <summary>Latest location snapshot. The returned record is immutable and safe to retain.</summary>
    public GameLocationState Location
    {
        get
        {
            lock (_gate) return _location;
        }
    }

    /// <summary>Raised when any shared-state slice changes.</summary>
    public event Action? Changed;

    /// <summary>Raised when the location snapshot changes.</summary>
    public event Action? LocationChanged;

    /// <summary>
    /// Publish the result of the location domain service. Internal so ordinary consumers cannot
    /// mutate shared operational state; writers live in the same service/domain assembly.
    /// </summary>
    internal void PublishLocation(GameLocationState location)
    {
        ArgumentNullException.ThrowIfNull(location);

        Action? locationChanged;
        Action? changed;
        lock (_gate)
        {
            if (_location == location) return;
            _location = location;
            locationChanged = LocationChanged;
            changed = Changed;
        }

        // Never invoke subscribers under the state lock: UI and service observers are free to
        // read GameState again from their callbacks without deadlocking the publisher.
        locationChanged?.Invoke();
        changed?.Invoke();
    }
}
