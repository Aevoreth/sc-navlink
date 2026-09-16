using NexusApp.Models.Cargo;
using NexusApp.Services.Cargo;

namespace NexusApp.Services;

/// <summary>
/// On-grid crate fit for Aboard. Boxes rest upright and yaw only; they never tip onto a side.
/// Remaining counts pack cargo already aboard, then ask how many more of this size still fit.
/// Placement planning stays in the cargo-grid studio.
/// </summary>
internal static class CargoCrateFit
{
    public const int OffGridCountCap = 32;

    public static IReadOnlyList<int> Sizes { get; } = BoxType.SizesDesc;

    public static Dictionary<int, int> EmptyCounts() => Sizes.ToDictionary(s => s, _ => 0);

    public static int VolumeOf(IReadOnlyDictionary<int, int>? counts)
    {
        if (counts is null) return 0;
        var total = 0;
        foreach (var (size, n) in counts)
        {
            if (n > 0) total += size * n;
        }
        return total;
    }

    public static Dictionary<int, int> WithoutSize(IReadOnlyDictionary<int, int> counts, int size)
    {
        var copy = EmptyCounts();
        foreach (var (s, n) in counts)
        {
            if (s != size && n > 0) copy[s] = n;
        }
        return copy;
    }

    public static bool SizeFits(ShipCargoDef? grids, TradeShip? trade, int scu)
    {
        if (!BoxType.IsStandard(scu)) return false;
        if (grids is not null) return LatticeCount(grids, scu) > 0;
        if (trade is not null) return scu <= trade.MaxContainerScu;
        return true;
    }

    /// <summary>
    /// How many crates of this size the hull can take given cargo already occupying the grids.
    /// <paramref name="packed"/> is on-grid crate counts (this size excluded).
    /// <paramref name="volumeUsed"/> is SCU already counting against grid capacity (this size excluded).
    /// Off-grid does not use the cargo grid: holds are often larger than the grid, so counts are
    /// a user choice ("if it fits, it ships") capped only for the dropdown.
    /// </summary>
    public static int MaxCount(
        ShipCargoDef? grids,
        TradeShip? trade,
        int scu,
        bool offGrid,
        IReadOnlyDictionary<int, int>? packed = null,
        int volumeUsed = -1)
    {
        if (!BoxType.IsStandard(scu)) return 0;
        if (offGrid) return OffGridCountCap;

        var packedCounts = packed ?? EmptyCounts();
        var used = volumeUsed >= 0 ? volumeUsed : VolumeOf(packedCounts);
        var volume = grids?.TotalScu ?? trade?.TotalScu ?? 0;

        if (grids is not null)
        {
            var empty = LatticeCount(grids, scu);
            if (empty <= 0) return 0;
            var volCap = volume > 0 ? Math.Max(0, (volume - used) / scu) : empty;
            var hi = Math.Min(empty, volCap);
            if (hi <= 0) return 0;
            if (VolumeOf(packedCounts) == 0) return hi;
            return RemainingPackCount(grids, packedCounts, scu, hi);
        }

        if (trade is not null && scu <= trade.MaxContainerScu)
            return volume > 0 ? Math.Max(0, (volume - used) / scu) : 0;

        if (grids is null && trade is null) return OffGridCountCap;
        return 0;
    }

    public static int LatticeCount(ShipCargoDef ship, int scu)
    {
        if (!BoxType.IsStandard(scu)) return 0;
        var box = BoxType.Of(scu);
        var total = 0;
        foreach (var grid in ship.Grids)
        {
            if (!grid.Accepts(scu)) continue;
            var best = 0;
            foreach (var ori in box.Orientations)
            {
                if (ori.W > grid.W || ori.D > grid.D || ori.H > grid.H) continue;
                var n = (grid.W / ori.W) * (grid.D / ori.D) * (grid.H / ori.H);
                if (n > best) best = n;
            }
            total += best;
        }
        return total;
    }

    private static int RemainingPackCount(
        ShipCargoDef ship,
        IReadOnlyDictionary<int, int> packed,
        int scu,
        int hi)
    {
        var occupied = ToBoxes(packed);
        var lo = 0;
        while (lo < hi)
        {
            var mid = (lo + hi + 1) / 2;
            if (ExtraFits(ship, occupied, scu, mid))
                lo = mid;
            else
                hi = mid - 1;
        }
        return lo;
    }

    private static bool ExtraFits(ShipCargoDef ship, List<PackBox> occupied, int scu, int extraCount)
    {
        var boxes = new List<PackBox>(occupied.Count + extraCount);
        boxes.AddRange(occupied);
        var idx = occupied.Count;
        for (var i = 0; i < extraCount; i++)
        {
            boxes.Add(new PackBox { Scu = scu, ItemId = "extra", OrderIndex = idx++ });
        }
        var result = CargoPacker.AutoPack(ship.Grids, boxes);
        return result.Deferred.Count == 0;
    }

    private static List<PackBox> ToBoxes(IReadOnlyDictionary<int, int> counts)
    {
        var boxes = new List<PackBox>();
        var idx = 0;
        foreach (var size in Sizes)
        {
            if (!counts.TryGetValue(size, out var n) || n <= 0) continue;
            for (var i = 0; i < n; i++)
                boxes.Add(new PackBox { Scu = size, ItemId = "occ", OrderIndex = idx++ });
        }
        return boxes;
    }
}
