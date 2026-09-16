namespace NexusApp.Services;

// SHIPS page sub-tab persistence (AppSettings.ShipsActiveFlow). Same shape as TradeFlows /
// SettingsTabs / OverlayTabs - one small file per page with its own tab strip.
public static class ShipsFlows
{
    public static readonly string[] Ids = ["browser", "hangar", "loadout"];
    public const string Default = "browser";

    public static string NormalizeForRestore(string? saved)
        => saved is "browser" or "hangar" or "loadout" ? saved : Default;
}
