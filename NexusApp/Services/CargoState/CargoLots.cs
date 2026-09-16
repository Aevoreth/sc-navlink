using NexusApp.Models.Cargo;

namespace NexusApp.Services;

/// <summary>
/// UI-free cargo lot rules. Contract obligations and cargo-grid placement stay out.
/// </summary>
internal static class CargoLots
{
    public static bool TryNormalize(GameCargoLot draft, out GameCargoLot lot)
    {
        lot = draft;
        var ship = (draft.ShipId ?? "").Trim();
        if (ship.Length == 0) return false;

        var name = (draft.Commodity ?? "").Trim();
        if (draft.Scu < 0) return false;
        if (draft.Provenance == GameCargoProvenance.None) return false;

        var box = draft.ContainerScu is > 0 ? draft.ContainerScu : null;
        var count = draft.ContainerCount is > 0 ? draft.ContainerCount : null;
        double? conf = draft.Confidence;
        if (conf is < 0 or > 1)
            conf = Math.Clamp(conf.GetValueOrDefault(), 0, 1);

        var id = string.IsNullOrWhiteSpace(draft.Id) ? Guid.NewGuid().ToString("N") : draft.Id.Trim();
        var observed = draft.ObservedUtc == default
            ? DateTime.UtcNow
            : draft.ObservedUtc.ToUniversalTime();

        lot = draft with
        {
            Id = id,
            ShipId = ship,
            Commodity = name,
            Scu = draft.Scu,
            ContainerScu = box,
            ContainerCount = count,
            Confidence = conf,
            ObservedUtc = observed,
            OffGrid = draft.OffGrid,
            Contracted = draft.Contracted,
            Destination = NormalizeDestination(draft.Destination),
        };
        return true;
    }

    public const int DestinationMaxLength = 160;

    public static string NormalizeDestination(string? value)
    {
        var dest = (value ?? "").Trim();
        return dest.Length <= DestinationMaxLength ? dest : dest[..DestinationMaxLength];
    }

    public static string FirstDestination(IEnumerable<GameCargoLot> lots)
    {
        foreach (var lot in lots)
        {
            var dest = NormalizeDestination(lot.Destination);
            if (dest.Length > 0) return dest;
        }
        return "";
    }

    public static string CombineDestination(IEnumerable<GameCargoLot> a, IEnumerable<GameCargoLot> b)
    {
        var x = FirstDestination(a);
        var y = FirstDestination(b);
        if (x.Length == 0) return y;
        if (y.Length == 0 || string.Equals(x, y, StringComparison.OrdinalIgnoreCase)) return x;
        return x + " / " + y;
    }

    public static Dictionary<int, int> CountsBySize(IEnumerable<GameCargoLot> lots)
    {
        var counts = CargoCrateFit.EmptyCounts();
        foreach (var lot in lots)
        {
            if (lot.ContainerScu is int size && BoxType.IsStandard(size) && lot.Scu > 0)
            {
                var n = lot.ContainerCount is > 0 ? lot.ContainerCount.Value : lot.Scu / size;
                if (n > 0) counts[size] += n;
            }
        }
        return counts;
    }

    public static Dictionary<int, int> PackedCounts(IEnumerable<GameCargoLot> lots) =>
        CountsBySize(lots.Where(l => !l.OffGrid));

    public static bool SameCommodity(string? a, string? b) =>
        string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);

    public static IEnumerable<GameCargoLot> Others(IEnumerable<GameCargoLot> lots, string? commodity) =>
        lots.Where(l => !SameCommodity(l.Commodity, commodity));

    public static int UnspecifiedScu(IEnumerable<GameCargoLot> lots)
    {
        var extra = 0;
        foreach (var lot in lots)
        {
            if (lot.ContainerScu is int size && BoxType.IsStandard(size)) continue;
            extra += Math.Max(0, lot.Scu);
        }
        return extra;
    }

    public static bool AnyOffGrid(IEnumerable<GameCargoLot> lots)
    {
        foreach (var lot in lots)
            if (lot.OffGrid) return true;
        return false;
    }

    public static bool AnyContracted(IEnumerable<GameCargoLot> lots)
    {
        foreach (var lot in lots)
            if (lot.Contracted) return true;
        return false;
    }

    public static int? ResolveUexId(string? commodity, IEnumerable<CatalogCommodity>? catalog)
    {
        var name = (commodity ?? "").Trim();
        if (name.Length == 0 || catalog is null) return null;
        foreach (var row in catalog)
        {
            if (string.Equals(row.Name, name, StringComparison.OrdinalIgnoreCase))
                return row.Id;
        }
        return null;
    }

    public static string ProvenanceLabel(GameCargoProvenance provenance) => provenance switch
    {
        GameCargoProvenance.UserConfirmed => "confirmed",
        GameCargoProvenance.OcrObserved => "ocr",
        GameCargoProvenance.Inferred => "inferred",
        GameCargoProvenance.Transaction => "transaction",
        _ => "",
    };
}
