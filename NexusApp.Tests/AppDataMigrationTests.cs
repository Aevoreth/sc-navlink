using System.IO;
using NexusApp.Services;
using Xunit;

namespace NexusApp.Tests;

public class AppDataMigrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "navlink-migrate-" + Path.GetRandomFileName());

    public AppDataMigrationTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch { /* best effort */ }
    }

    [Fact]
    public void CopiesFullNexusAppTreeWhenScNavlinkIsAbsent()
    {
        var legacy = Path.Combine(_root, AppIdentity.LegacyAppDataFolder);
        Directory.CreateDirectory(Path.Combine(legacy, "cache"));
        File.WriteAllText(Path.Combine(legacy, "settings.json"), "{}");
        File.WriteAllText(Path.Combine(legacy, "nexus.db"), "db");
        File.WriteAllText(Path.Combine(legacy, "cache", "provider_cache.db"), "cache");

        SettingsService.MigrateLegacyAppData(_root);

        var current = Path.Combine(_root, AppIdentity.AppDataFolder);
        Assert.True(File.Exists(Path.Combine(current, "settings.json")));
        Assert.True(File.Exists(Path.Combine(current, "nexus.db")));
        Assert.True(File.Exists(Path.Combine(current, "cache", "provider_cache.db")));
        Assert.True(File.Exists(Path.Combine(legacy, "settings.json")));
    }

    [Fact]
    public void DoesNotOverwriteExistingScNavlinkFolder()
    {
        var current = Path.Combine(_root, AppIdentity.AppDataFolder);
        var legacy = Path.Combine(_root, AppIdentity.LegacyAppDataFolder);
        Directory.CreateDirectory(current);
        Directory.CreateDirectory(legacy);
        File.WriteAllText(Path.Combine(current, "settings.json"), "keep");
        File.WriteAllText(Path.Combine(legacy, "settings.json"), "old");

        SettingsService.MigrateLegacyAppData(_root);

        Assert.Equal("keep", File.ReadAllText(Path.Combine(current, "settings.json")));
    }

    [Fact]
    public void CopiesV4TopLevelFilesIntoNexusAppThenIntoScNavlink()
    {
        var v4 = Path.Combine(_root, AppIdentity.LegacyV4Folder);
        Directory.CreateDirectory(v4);
        File.WriteAllText(Path.Combine(v4, "settings.json"), "from-v4");

        SettingsService.MigrateLegacyAppData(_root);

        Assert.Equal("from-v4", File.ReadAllText(Path.Combine(_root, AppIdentity.LegacyAppDataFolder, "settings.json")));
        Assert.Equal("from-v4", File.ReadAllText(Path.Combine(_root, AppIdentity.AppDataFolder, "settings.json")));
    }

    [Fact]
    public void CopiesDemoProfileFolder()
    {
        var legacyDemo = Path.Combine(_root, AppIdentity.LegacyAppDataFolderDemo);
        Directory.CreateDirectory(legacyDemo);
        File.WriteAllText(Path.Combine(legacyDemo, "settings.json"), "demo");

        SettingsService.MigrateLegacyAppData(_root);

        Assert.Equal("demo", File.ReadAllText(Path.Combine(_root, AppIdentity.AppDataFolderDemo, "settings.json")));
    }
}
