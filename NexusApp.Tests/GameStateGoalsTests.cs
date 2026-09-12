using NexusApp.Models;
using NexusApp.Services;
using Xunit;

namespace NexusApp.Tests;

public class GameStateGoalsTests : IDisposable
{
    private readonly List<string> _tempDirs = new();

    private string NewSettingsPath()
    {
        var dir = Path.Combine(Path.GetTempPath(), "nexus-goals-state-test-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return Path.Combine(dir, "settings.json");
    }

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    private static ShoppingItem Item(string name, double qty = 2, string unit = "SCU")
        => new() { ResourceName = name, Quantity = qty, Unit = unit };

    [Fact]
    public void NewState_StartsWithHonestEmptyGoals()
    {
        var state = new GameState();

        Assert.Equal(GameGoalsState.Empty, state.Goals);
        Assert.False(state.Goals.HasShopping);
        Assert.False(state.Goals.HasOwnedBlueprints);
        Assert.Empty(state.Goals.Shopping);
        Assert.Empty(state.Goals.OwnedBlueprints);
    }

    [Fact]
    public void PublishGoals_ReplacesImmutableSnapshot_AndNotifiesOnce()
    {
        var state = new GameState();
        var snapshot = GoalsProjection.From(
            new[] { Item("Quantanium", 8) },
            new[] { "Bracket Cooler" });
        int changed = 0;
        int goalsChanged = 0;
        state.Changed += () => changed++;
        state.GoalsChanged += () => goalsChanged++;

        state.PublishGoals(snapshot);
        state.PublishGoals(GoalsProjection.From(
            new[] { Item("Quantanium", 8) },
            new[] { "Bracket Cooler" }));

        Assert.Equal(1, changed);
        Assert.Equal(1, goalsChanged);
        Assert.True(state.Goals.HasShopping);
        Assert.True(state.Goals.Owns("Bracket Cooler"));
    }

    [Fact]
    public void From_MapsShoppingFacts_AndDropsBlankOwnedNames()
    {
        var snapshot = GoalsProjection.From(
            new[] { Item("Bexalite", 4, "×") },
            new[] { "Hellion Cannon", "  ", "" });

        var item = Assert.Single(snapshot.Shopping);
        Assert.Equal("Bexalite", item.ResourceName);
        Assert.Equal(4, item.Quantity);
        Assert.Equal("×", item.Unit);
        Assert.Equal(new[] { "Hellion Cannon" }, snapshot.OwnedBlueprints);
    }

    [Fact]
    public void PublishShopping_KeepsOwnedBlueprints()
    {
        var state = new GameState();
        state.PublishGoals(GoalsProjection.From(
            new[] { Item("Gold") },
            new[] { "Bracket Cooler" }));

        state.PublishShopping(GoalsProjection.ShoppingFrom(new[] { Item("Quantanium", 8) }));

        var item = Assert.Single(state.Goals.Shopping);
        Assert.Equal("Quantanium", item.ResourceName);
        Assert.Equal(8, item.Quantity);
        Assert.True(state.Goals.Owns("Bracket Cooler"));
    }

    [Fact]
    public void PublishOwnedBlueprints_KeepsShopping()
    {
        var state = new GameState();
        state.PublishGoals(GoalsProjection.From(
            new[] { Item("Gold", 3) },
            new[] { "Bracket Cooler" }));

        state.PublishOwnedBlueprints(GoalsProjection.OwnedFrom(new[] { "Hellion Cannon" }));

        var item = Assert.Single(state.Goals.Shopping);
        Assert.Equal("Gold", item.ResourceName);
        Assert.Equal(new[] { "Hellion Cannon" }, state.Goals.OwnedBlueprints);
        Assert.False(state.Goals.Owns("Bracket Cooler"));
    }

    [Fact]
    public void Owns_IsCaseInsensitive()
    {
        var snapshot = GoalsProjection.From([], new[] { "Bracket Cooler" });

        Assert.True(snapshot.Owns("bracket cooler"));
        Assert.True(snapshot.Owns("BRACKET COOLER"));
        Assert.False(snapshot.Owns("Hellion Cannon"));
    }

    [Fact]
    public void SettingsService_PublishesPersistedOwnedBlueprints()
    {
        var path = NewSettingsPath();
        var firstState = new GameState();
        var first = new SettingsService(path, firstState);
        first.SetBlueprintOwned("Bracket Cooler", true);
        Assert.True(firstState.Goals.Owns("Bracket Cooler"));

        var reloadedState = new GameState();
        _ = new SettingsService(path, reloadedState);

        Assert.True(reloadedState.Goals.Owns("Bracket Cooler"));
        Assert.False(reloadedState.Goals.HasShopping);
    }

    [Fact]
    public void SettingsService_AlreadyOwnedDoesNotNotifyAgain()
    {
        var path = NewSettingsPath();
        var state = new GameState();
        var settings = new SettingsService(path, state);
        settings.SetBlueprintOwned("Bracket Cooler", true);
        int goalsChanged = 0;
        state.GoalsChanged += () => goalsChanged++;

        settings.SetBlueprintOwned("Bracket Cooler", true);

        Assert.Equal(0, goalsChanged);
        Assert.True(state.Goals.Owns("Bracket Cooler"));
    }

    [Fact]
    public void LogReset_DoesNotClearGoals()
    {
        var state = new GameState();
        using var feed = new GameLogFeed(state);
        state.PublishGoals(GoalsProjection.From(
            new[] { Item("Quantanium", 8) },
            new[] { "Bracket Cooler" }));
        int goalsChanged = 0;
        state.GoalsChanged += () => goalsChanged++;

        feed.HandleLogReset();

        Assert.True(state.Goals.HasShopping);
        Assert.True(state.Goals.Owns("Bracket Cooler"));
        Assert.Equal(1, state.Session.LogGeneration);
        Assert.Equal(0, goalsChanged);
    }
}
