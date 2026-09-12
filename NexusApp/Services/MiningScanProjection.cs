namespace NexusApp.Services;

/// <summary>
/// Pure snapshot builder for the mining GameState slice. The view model still owns WPF
/// collections; this maps unfiltered decode facts into immutable records.
/// </summary>
public static class MiningScanProjection
{
    public static GameMiningMatchKind Classify(IReadOnlyList<GameMiningHit> hits)
    {
        if (hits.Count == 0) return GameMiningMatchKind.None;
        return hits.Any(h => h.IsExact) ? GameMiningMatchKind.Exact : GameMiningMatchKind.Close;
    }

    public static string TopName(IReadOnlyList<GameMiningHit> hits)
    {
        var kind = Classify(hits);
        if (kind == GameMiningMatchKind.None) return "No match";
        return hits.FirstOrDefault(h => h.IsExact)?.ResourceName ?? hits[0].ResourceName;
    }

    public static GameMiningState FromScan(
        int rs,
        IReadOnlyList<GameMiningHit> hits,
        IReadOnlyList<GameMiningHistoryEntry> recent,
        DateTime seenUtc)
    {
        var kind = Classify(hits);
        return new GameMiningState(
            new GameMiningScan(rs, TopName(hits), kind, seenUtc, hits),
            recent);
    }

    public static GameMiningState ClearedScan(IReadOnlyList<GameMiningHistoryEntry> recent)
        => new(null, recent);

    public static GameMiningState ClearedHistory(GameMiningScan? lastScan)
        => new(lastScan, Array.Empty<GameMiningHistoryEntry>());
}
