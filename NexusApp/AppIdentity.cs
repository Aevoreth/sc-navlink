namespace NexusApp;

/// <summary>
/// Product identity for the first SC-navLink rebrand pass. Display names, package
/// ids, URLs, folder names, and brand asset names live here so chrome, packaging,
/// and migration do not drift. The <c>NexusApp</c> namespace stays.
/// </summary>
public static class AppIdentity
{
    public const string ProductName = "SC-navLink";
    public const string PackageId = "sc-navlink";
    public const string OverlayWordmark = "NAVLINK";

    public const string RepoUrl = "https://github.com/Aevoreth/sc-navlink";
    public const string IssuesUrl = RepoUrl + "/issues";
    public const string ReleasesLatestUrl = RepoUrl + "/releases/latest";
    public const string SecurityUrl = RepoUrl + "/security";
    public const string IssuesHostPath = "github.com/Aevoreth/sc-navlink/issues";
    public const string RepoHostPath = "github.com/Aevoreth/sc-navlink";

    public const string AppDataFolder = "sc-navlink";
    public const string AppDataFolderDemo = "sc-navlink_demo";
    public const string LegacyAppDataFolder = "NexusApp";
    public const string LegacyAppDataFolderDemo = "NexusApp_demo";
    public const string LegacyV4Folder = "Nexus_v4";

    public const string InstallFolderName = "SC-navLink";
    public const string LegacyInstallFolderName = "Nexus";

    public const string ExeFileName = "SC-navLink.exe";
    public const string SetupAssetName = "SC-navLink_Setup.exe";
    public const string PortableAssetName = "sc-navlink_portable.zip";
    public const string PortableZipRoot = "sc-navlink";

    public const string IconFileName = "sc-navlink.ico";
    public const string IconPngFileName = "sc-navlink_icon.png";
    public const string LogoPngFileName = "sc-navlink_logo.png";

    public const string LogoPackUri = "pack://application:,,,/Assets/" + LogoPngFileName;
    public const string WindowIconPackPath = "/Assets/" + IconPngFileName;

    public const string AttributionName = "Nexus";
    public const string AttributionUrl = "https://github.com/T3SoD/NexusApp";

    public const string ManifestUrl = RepoUrl + "/releases/latest/download/update_manifest.json";
    public const string SignatureUrl = ManifestUrl + ".sig";

    public static string AssetUrl(Version version, string assetName) =>
        $"{RepoUrl}/releases/download/v{version.ToString(3)}/{assetName}";

    public static string AppDataLogHint =>
        @"%AppData%\" + AppDataFolder + @"\logs\nexus.log";
}
