namespace NexusApp.Services;

/// <summary>
/// Cadence is a fetch throttle. It does not delete cached rows.
/// Freshness is metadata for callers (the Market view shows age).
/// </summary>
internal static class ProviderFreshnessRules
{
    public static readonly TimeSpan HourlyCadence = TimeSpan.FromHours(1);
    public static readonly TimeSpan ReferenceCadence = TimeSpan.FromHours(24);

    public static TimeSpan CadenceFor(ProviderDataClass dataClass) =>
        dataClass is ProviderDataClass.Terminals or ProviderDataClass.Yields
            ? ReferenceCadence
            : HourlyCadence;

    /// <summary>
    /// True when this data class is due for an HTTP call. A stamp in the future
    /// (clock rollback) counts as due so the auto path cannot freeze.
    /// </summary>
    public static bool IsDue(DateTime fetchedUtc, TimeSpan cadence, DateTime nowUtc) =>
        fetchedUtc == default || fetchedUtc > nowUtc || nowUtc - fetchedUtc >= cadence;

    public static ProviderFreshness Evaluate(
        int rowCount,
        DateTime fetchedUtc,
        DateTime lastFailureUtc,
        TimeSpan cadence,
        DateTime nowUtc)
    {
        if (rowCount <= 0)
            return ProviderFreshness.Unavailable;

        if (lastFailureUtc != default && (fetchedUtc == default || lastFailureUtc >= fetchedUtc))
            return ProviderFreshness.OfflineCached;

        if (IsDue(fetchedUtc, cadence, nowUtc))
            return ProviderFreshness.Stale;

        return ProviderFreshness.Fresh;
    }
}
