namespace NexusApp.Services;

/// <summary>
/// Maps UEX parse records (MarketParse output) onto NavLink catalog types.
/// UEX JSON field names stay in MarketParse. Public docs: https://uexcorp.space/api
/// </summary>
internal static class UexNormalizer
{
    public static CatalogCommodity Commodity(MarketCommodity c) =>
        new(c.Id, c.Name, c.Slug, c.IsRaw, c.IsRefined, c.IdParent);

    public static CatalogTerminal Terminal(MarketTerminal t) =>
        new(t.Id, t.Name, t.Type, t.IsRefinery, t.System, t.Location, t.Orbit, t.PlanetOrMoon);

    public static CatalogTradePrice TradePrice(TradePriceRow r) =>
        new(r.TerminalId, r.CommodityId, r.Buy, r.Sell, r.BuyStockScu, r.SellDemandScu,
            r.StatusBuy, r.StatusSell, r.ContainerSizes, r.ModifiedUtc, r.TerminalName, r.CommodityName);

    public static CatalogRefinedPrice RefinedPrice(MarketPriceRow r) =>
        new(r.TerminalId, r.CommodityId, r.Sell, r.SellAvgWeek, r.GameVersion, r.ModifiedUtc, r.TerminalName);

    public static CatalogYield Yield(MarketYieldRow r) =>
        new(r.TerminalId, r.CommodityId, r.BonusPct, r.BonusPctWeek, r.ModifiedUtc, r.TerminalName);

    public static ShipCatalogEntry Vehicle(MarketVehicle v) =>
        new(v.Id, v.Slug, v.Name, v.Manufacturer, v.Role, v.CargoScu, v.Crew, v.Mass, v.Length,
            v.Beam, v.Height, v.IsConcept, v.IsGroundVehicle, v.IsSpaceship,
            string.IsNullOrEmpty(v.StoreUrl) ? null : v.StoreUrl,
            string.IsNullOrEmpty(v.PhotoUrl) ? null : v.PhotoUrl);

    public static CatalogVehiclePurchase VehiclePurchase(MarketVehiclePurchase r) =>
        new(r.VehicleId, r.TerminalId, r.TerminalName, r.PriceBuy);

    public static CatalogVehicleRental VehicleRental(MarketVehicleRental r) =>
        new(r.VehicleId, r.TerminalId, r.TerminalName, r.PriceRent);

    public static CatalogRawPrice RawPrice(MarketPriceRow r) =>
        new(r.TerminalId, r.CommodityId, r.Sell, r.SellAvgWeek, r.GameVersion, r.ModifiedUtc, r.TerminalName);

    public static MarketCommodity ToMarket(CatalogCommodity c) =>
        new(c.Id, c.Name, c.Slug, c.IsRaw, c.IsRefined, c.ParentId);

    public static MarketTerminal ToMarket(CatalogTerminal t) =>
        new(t.Id, t.Name, t.Type, t.IsRefinery, t.System, t.Location, t.Orbit, t.PlanetOrMoon);

    public static TradePriceRow ToMarket(CatalogTradePrice r) =>
        new(r.TerminalId, r.CommodityId, r.Buy, r.Sell, r.BuyStockScu, r.SellDemandScu,
            r.StatusBuy, r.StatusSell, r.ContainerSizes, r.ObservedUtc, r.TerminalName, r.CommodityName);

    public static MarketPriceRow ToMarket(CatalogRefinedPrice r) =>
        new(r.TerminalId, r.CommodityId, r.Sell, r.SellAvgWeek, r.GameVersion, r.ObservedUtc, r.TerminalName);

    public static MarketYieldRow ToMarket(CatalogYield r) =>
        new(r.TerminalId, r.CommodityId, r.BonusPct, r.BonusPctWeek, r.ObservedUtc, r.TerminalName);

    public static MarketPriceRow ToMarket(CatalogRawPrice r) =>
        new(r.TerminalId, r.CommodityId, r.Sell, r.SellAvgWeek, r.GameVersion, r.ObservedUtc, r.TerminalName);
}
