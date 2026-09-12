using NexusApp.Services;

namespace NexusApp.Services.Map;

/// <summary>
/// "Where is the player, and how far is that from X." One small read-only seam over the geometry
/// catalog and shared GameState, so any surface can ask without owning a second copy of live
/// operational state.
///
/// Deliberately holds NO state of its own and caches nothing: GameState is the single source of
/// truth and updates on its own schedule, so every property here reads through on each call. That
/// keeps this correct without a subscription, an invalidation rule, or a stale-cache bug.
///
/// <para>Current == null is a FIRST-CLASS answer meaning "we do not know", not an error. It is the
/// normal state with Star Citizen closed, before the first boundary crossing of a session, and
/// whenever the log names somewhere the catalog cannot place. Every caller must render it as
/// silence - no placeholder, no zero, no "unknown" chip - matching the absent-not-placeholder rule
/// the trade surfaces already follow.</para>
/// </summary>
public sealed class PlayerPlace
{
    private readonly MapCatalog _map;
    private readonly GameState _state;

    public PlayerPlace(MapCatalog map, GameState state)
    {
        _map = map ?? throw new ArgumentNullException(nameof(map));
        _state = state ?? throw new ArgumentNullException(nameof(state));
    }

    // Compatibility constructor for the inherited composition root. It deliberately extracts the
    // shared state store once; all reads below are from GameState, not LocationTracker.
    public PlayerPlace(MapCatalog map, LocationTracker locations)
        : this(map, locations?.GameState ?? throw new ArgumentNullException(nameof(locations)))
    {
    }

    /// <summary>The map object the player was last seen at, or null when nothing resolves. Resolved
    /// through the catalog's raw-token tier, so gateway names that repeat across systems land in the
    /// right one.</summary>
    public MapObject? Current
    {
        get
        {
            var location = _state.Location;
            return _map.ResolvePlayerLocation(location.Label, location.RawToken);
        }
    }

    /// <summary>The display name the log gave, even when it does not resolve to an object - a
    /// jurisdiction like "Rough and Ready", or a gateway with no captured token. Callers that want to
    /// SAY where the player is should prefer this; callers that want to MEASURE need
    /// <see cref="MeasureFrom"/>, and callers that want to POSITION a marker need Current.</summary>
    public string? Label => _state.Location.Label;

    /// <summary>The measuring read (offline-distances ruling, 2026-08-17): the player's place while
    /// a session is live, null when it is not. Last-known location intentionally survives the live
    /// session, so Current can keep resolving after the game exits - honest for placing a grey
    /// last-known marker, dishonest as the origin of a distance, an ordering, or a proximity tier.
    /// Every distance call site folds the process probe (App.GameLogFeed.IsSessionLive) through this
    /// one rule, so a withheld distance falls out of the existing null-player silence path rather
    /// than a second code path. Kept pure (the probe is an argument, not a dependency) like
    /// StatusChips' folds.</summary>
    public MapObject? MeasureFrom(bool sessionLive) => sessionLive ? Current : null;

    /// <summary>True when <see cref="Label"/> is a JURISDICTION reading - whose space the player
    /// crossed into, not a place they are standing (2026-08-01: "Crusader Industries" rendered as
    /// a location on the LOCATION chip). Display surfaces should qualify these, not hide them: a
    /// coarse reading is still the freshest fact available.</summary>
    public bool LabelIsJurisdiction => _state.Location.IsJurisdiction;

    /// <summary>When that reading was taken. The timeline is sparse and boundary-driven by nature, so
    /// anything shown to the user should be dated rather than implied to be live.</summary>
    public DateTime? SeenUtc => _state.Location.SeenUtc;

    /// <summary>The UEX Location string for the current place, when one is known. This is the hint
    /// that lets a UEX lookup succeed at a gateway whose in-game name UEX does not use.</summary>
    public string? UexLocation => _state.Location.UexLocation;

    /// <summary>The star system the player is in, or null when unknown.</summary>
    public string? System => Current?.System;

    /// <summary>Formatted straight-line distance from the player to <paramref name="target"/>, or
    /// null when either end is unknown or the two are in different systems. Null is the honest answer
    /// across a jump point: that route is not Euclidean and a number would be a lie.</summary>
    public string? DistanceFrom(MapObject? target)
        => _map.DistanceMeters(Current, target) is { } m ? MapCatalog.FormatGm(m) : null;

    /// <summary>The same, for a market terminal.</summary>
    public string? DistanceFrom(MarketTerminal? terminal)
        => DistanceFrom(_map.ResolveTerminal(terminal));
}
