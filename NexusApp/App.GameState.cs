namespace NexusApp;

public partial class App
{
    /// <summary>
    /// Canonical observable operational state for this app lifetime.
    ///
    /// Location, session, shard, and hauling publishers write into this one instance. Later
    /// domain slices should publish here rather than introduce parallel state copies.
    /// </summary>
    public static Services.GameState GameState { get; } = new();
}
