namespace NexusApp;

public partial class App
{
    /// <summary>
    /// Canonical observable operational state for this app lifetime.
    ///
    /// Location, session, and shard publishers write into this one instance. Later domain slices
    /// should publish here rather than introduce parallel state copies.
    /// </summary>
    public static Services.GameState GameState { get; } = new();
}
