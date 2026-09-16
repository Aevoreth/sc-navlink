namespace NexusApp.Services;

/// <summary>Normalized vehicle/ship catalogue row. Not a UEX DTO.</summary>
public sealed record ShipCatalogEntry(
    int ProviderId,
    string Id,
    string DisplayName,
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
    string? StoreUrl,
    string? PhotoUrl)
{
    public bool IsFlyable => !IsConcept;
}

/// <summary>Normalized in-game purchase listing. Joined to a catalog row by <see cref="VehicleProviderId"/>.</summary>
public sealed record CatalogVehiclePurchase(
    int VehicleProviderId,
    int TerminalId,
    string TerminalName,
    double PriceBuy);

/// <summary>Normalized in-game rental listing. Joined to a catalog row by <see cref="VehicleProviderId"/>.</summary>
public sealed record CatalogVehicleRental(
    int VehicleProviderId,
    int TerminalId,
    string TerminalName,
    double PriceRent);

/// <summary>Filter for the Ship Browser. Concept and ground vehicles are off by default.</summary>
public sealed record ShipCatalogQuery(
    string Search = "",
    bool IncludeConcept = false,
    bool IncludeGround = false,
    string? Manufacturer = null,
    string? Role = null);

/// <summary>
/// Provider-neutral ship catalogue. Views read this. They must not call UEX or wiki HTTP.
/// Source: UEX API 2.0 /vehicles* (https://uexcorp.space/api).
/// </summary>
public interface IShipCatalog
{
    CachedSlice<ShipCatalogEntry> Vehicles { get; }
    CachedSlice<CatalogVehiclePurchase> Purchases { get; }
    CachedSlice<CatalogVehicleRental> Rentals { get; }
    ShipCatalogEntry? ById(string id);
    IReadOnlyList<ShipCatalogEntry> Query(ShipCatalogQuery query);
    IReadOnlyList<CatalogVehiclePurchase> PurchasesFor(string catalogId);
    IReadOnlyList<CatalogVehicleRental> RentalsFor(string catalogId);
    event Action? Changed;
}

/// <summary>How a hangar ship was acquired. My Hangar is the confirmed source until ASOP Vision.</summary>
public enum HangarAcquisition { Pledged, Purchased, Rented }

/// <summary>One user-owned hull in My Hangar. Catalog id is the UEX slug.</summary>
public sealed record HangarEntry(
    string Id,
    string CatalogId,
    HangarAcquisition Acquisition,
    DateTime? RentalExpiresUtc,
    string? LastKnownLocation,
    string Provenance,
    DateTime AddedUtc);
