using NexusApp.Models;
using NexusApp.Services;
using Xunit;

namespace NexusApp.Tests;

public class BlueprintCatalogTests
{
    private static Blueprint Bp(string name, string category) =>
        new() { Name = name, Category = category };

    [Fact]
    public void CategoriesFrom_KeepsPreferredOrder_ThenAddsExtraAlphabetically()
    {
        var cats = BlueprintCatalog.CategoriesFrom(
        [
            Bp("Probe", "Mission Items"),
            Bp("Shell", "Ammo"),
            Bp("Plate", "Armor"),
            Bp("Rifle", "Weapons"),
            Bp("Cooler", "Ship Components"),
        ]);

        Assert.Equal(
            new[] { "Armor", "Weapons", "Ship Components", "Ammo", "Mission Items" },
            cats);
    }

    [Fact]
    public void Classify_EmptyCatalog_IsUnavailable()
    {
        Assert.Equal(
            BlueprintCatalog.NameStatus.CatalogUnavailable,
            BlueprintCatalog.Classify("Probe", Array.Empty<string>()));
    }

    [Fact]
    public void Classify_MissingName_IsUnknown_WhenCatalogLoaded()
    {
        Assert.Equal(
            BlueprintCatalog.NameStatus.Unknown,
            BlueprintCatalog.Classify("Not A Real Blueprint", new[] { "Probe" }));
        Assert.Equal(
            BlueprintCatalog.NameStatus.InCatalog,
            BlueprintCatalog.Classify("probe", new[] { "Probe" }));
    }
}
