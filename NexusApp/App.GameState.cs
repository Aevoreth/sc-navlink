namespace NexusApp;

public partial class App
{
    /// <summary>
    /// Canonical observable operational state for this app lifetime.
    ///
    /// The first Issue #3 implementation slice is produced by LocationTracker, so the store shares
    /// that service's app lifetime. Later domain slices should publish into this same instance rather
    /// than introduce parallel state copies.
    /// </summary>
    public static Services.GameState GameState => Locations.GameState;
}
