using System.Globalization;

namespace NexusApp.Services;

/// <summary>
/// UI-free hangar field rules. Named loadouts stay off this row until the Loadout Calculator exists.
/// </summary>
public static class HangarMetadata
{
    public static HangarEntry WithAcquisition(HangarEntry row, HangarAcquisition acquisition)
    {
        var expiry = acquisition == HangarAcquisition.Rented ? row.RentalExpiresUtc : null;
        return row with { Acquisition = acquisition, RentalExpiresUtc = expiry };
    }

    public static HangarEntry WithLocation(HangarEntry row, string? location)
    {
        var loc = string.IsNullOrWhiteSpace(location) ? null : location.Trim();
        return row with { LastKnownLocation = loc };
    }

    public static HangarEntry WithExpiry(HangarEntry row, DateTime? expiresUtc)
    {
        if (row.Acquisition != HangarAcquisition.Rented)
            return row with { RentalExpiresUtc = null };
        return row with { RentalExpiresUtc = expiresUtc };
    }

    public static bool TryParseExpiry(string? text, out DateTime? utc)
    {
        utc = null;
        var s = (text ?? "").Trim();
        if (s.Length == 0) return true;
        if (!DateTime.TryParseExact(
                s,
                ["yyyy-MM-dd", "yyyy-M-d"],
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var day))
            return false;
        utc = DateTime.SpecifyKind(day.Date, DateTimeKind.Utc);
        return true;
    }

    public static string ExpiryText(DateTime? utc) =>
        utc is { } day ? day.ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "";

    public static string Summary(HangarEntry row)
    {
        var bits = new List<string> { row.Acquisition.ToString() };
        if (row.Acquisition == HangarAcquisition.Rented && row.RentalExpiresUtc is { } exp)
            bits.Add("expires " + ExpiryText(exp));
        if (!string.IsNullOrEmpty(row.LastKnownLocation))
            bits.Add(row.LastKnownLocation);
        return string.Join(" · ", bits);
    }
}
