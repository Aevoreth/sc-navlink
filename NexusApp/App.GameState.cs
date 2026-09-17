namespace NexusApp;

public partial class App
{
    /// <summary>
    /// Canonical observable operational state for this app lifetime.
    ///
    /// Location, session, shard, hauling, wallet, mining, refinery, goals, active ship,
    /// cargo, and route publishers write into this one instance. Later domain slices must
    /// publish here rather than introduce parallel state copies.
    /// </summary>
    public static Services.GameState GameState { get; } = new();
}
