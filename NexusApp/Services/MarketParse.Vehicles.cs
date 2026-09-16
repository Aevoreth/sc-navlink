namespace NexusApp.Services;

internal sealed record MarketVehicle(
    int Id,
    string Slug,
    string Name,
    string Manufacturer,
    string Role,
    int CargoScu,
    string Crew,
    double Mass,
    double Length,
    double Beam,
    double Height,
    bool IsConcept,
    bool IsGroundVehicle,
    bool IsSpaceship,
    string StoreUrl,
    string PhotoUrl);

internal sealed record MarketVehiclePurchase(int VehicleId, int TerminalId, string TerminalName, double PriceBuy);
internal sealed record MarketVehicleRental(int VehicleId, int TerminalId, string TerminalName, double PriceRent);

internal static partial class MarketParse
{
    public static List<MarketVehicle> ParseVehicles(string body, out int skipped)
    {
        var result = new List<MarketVehicle>();
        skipped = 0;
        try
        {
            if (!TryGetData(body, out var data) || data.ValueKind != System.Text.Json.JsonValueKind.Array)
                return result;
            foreach (var row in data.EnumerateArray())
            {
                if (row.ValueKind == System.Text.Json.JsonValueKind.Object
                    && TryInt(row, "id", out var id)
                    && id > 0)
                {
                    var slug = OptionalStr(row, "slug");
                    var name = FirstNonEmptyStr(row, "name_full", "name");
                    if (slug.Length == 0 || name.Length == 0)
                    {
                        skipped++;
                        continue;
                    }

                    result.Add(new MarketVehicle(
                        id,
                        slug,
                        name,
                        OptionalStr(row, "company_name"),
                        RoleOf(row),
                        CargoScuOf(row),
                        CrewOf(row),
                        OptionalDouble(row, "mass"),
                        OptionalDouble(row, "length"),
                        OptionalDouble(row, "width"),
                        OptionalDouble(row, "height"),
                        OptionalFlag(row, "is_concept"),
                        OptionalFlag(row, "is_ground_vehicle"),
                        OptionalFlag(row, "is_spaceship"),
                        OptionalStr(row, "url_store"),
                        OptionalStr(row, "url_photo")));
                }
                else
                {
                    skipped++;
                }
            }
        }
        catch (Exception)
        {
            skipped = 0;
            return [];
        }
        return result;
    }

    public static List<MarketVehiclePurchase> ParseVehiclePurchases(string body, out int skipped)
    {
        var result = new List<MarketVehiclePurchase>();
        skipped = 0;
        try
        {
            if (!TryGetData(body, out var data) || data.ValueKind != System.Text.Json.JsonValueKind.Array)
                return result;
            foreach (var row in data.EnumerateArray())
            {
                if (row.ValueKind == System.Text.Json.JsonValueKind.Object
                    && TryInt(row, "id_vehicle", out var vehicleId)
                    && TryInt(row, "id_terminal", out var terminalId)
                    && TryDouble(row, "price_buy", out var price))
                {
                    var terminal = OptionalStr(row, "terminal_name");
                    result.Add(new MarketVehiclePurchase(vehicleId, terminalId, terminal, price));
                }
                else skipped++;
            }
        }
        catch (Exception)
        {
            skipped = 0;
            return [];
        }
        return result;
    }

    public static List<MarketVehicleRental> ParseVehicleRentals(string body, out int skipped)
    {
        var result = new List<MarketVehicleRental>();
        skipped = 0;
        try
        {
            if (!TryGetData(body, out var data) || data.ValueKind != System.Text.Json.JsonValueKind.Array)
                return result;
            foreach (var row in data.EnumerateArray())
            {
                if (row.ValueKind == System.Text.Json.JsonValueKind.Object
                    && TryInt(row, "id_vehicle", out var vehicleId)
                    && TryInt(row, "id_terminal", out var terminalId)
                    && TryDouble(row, "price_rent", out var price))
                {
                    var terminal = OptionalStr(row, "terminal_name");
                    result.Add(new MarketVehicleRental(vehicleId, terminalId, terminal, price));
                }
                else skipped++;
            }
        }
        catch (Exception)
        {
            skipped = 0;
            return [];
        }
        return result;
    }

    private static string RoleOf(System.Text.Json.JsonElement row)
    {
        if (OptionalFlag(row, "is_cargo")) return "Cargo";
        if (OptionalFlag(row, "is_mining")) return "Mining";
        if (OptionalFlag(row, "is_salvage")) return "Salvage";
        if (OptionalFlag(row, "is_exploration")) return "Exploration";
        if (OptionalFlag(row, "is_military") || OptionalFlag(row, "is_bomber")) return "Combat";
        if (OptionalFlag(row, "is_racing")) return "Racing";
        if (OptionalFlag(row, "is_passenger")) return "Passenger";
        if (OptionalFlag(row, "is_industrial") || OptionalFlag(row, "is_refinery") || OptionalFlag(row, "is_construction"))
            return "Industrial";
        if (OptionalFlag(row, "is_medical")) return "Medical";
        if (OptionalFlag(row, "is_ground_vehicle")) return "Ground";
        return "Utility";
    }

    private static int CargoScuOf(System.Text.Json.JsonElement row)
    {
        var scu = OptionalDouble(row, "scu");
        if (scu <= 0) return 0;
        return (int)Math.Round(scu, MidpointRounding.AwayFromZero);
    }

    private static string CrewOf(System.Text.Json.JsonElement row)
    {
        var s = OptionalStr(row, "crew");
        if (s.Length > 0) return s;
        return OptionalInt(row, "crew") is { } n ? n.ToString() : "";
    }

    private static string OptionalStr(System.Text.Json.JsonElement row, string prop)
    {
        if (!row.TryGetProperty(prop, out var el)) return "";
        if (el.ValueKind == System.Text.Json.JsonValueKind.Null) return "";
        if (el.ValueKind == System.Text.Json.JsonValueKind.String) return el.GetString() ?? "";
        return "";
    }

    private static bool OptionalFlag(System.Text.Json.JsonElement row, string prop)
        => TryBool01(row, prop, out var v) && v;

    private static double OptionalDouble(System.Text.Json.JsonElement row, string prop)
        => TryDouble(row, prop, out var v) ? v : 0;

    private static int? OptionalInt(System.Text.Json.JsonElement row, string prop)
        => TryInt(row, prop, out var v) ? v : null;
}
