using NexusApp.Services;
using NexusApp.Services.Map;
using Xunit;

namespace NexusApp.Tests;

public class GameStateLocationTests
{
    private static GameLogEntry E(string raw) => new() { Raw = raw, Category = LogCategory.Other };

    private static string LocationLine(string place, string timestamp) =>
        $"<{timestamp}> [Notice] <RequestLocationInventory> Player[TestPilot] requested inventory " +
        $"for Location[{place}] [Team_CoreGameplayFeatures][Inventory]";

    [Fact]
    public void NewState_StartsWithHonestEmptyLocation()
    {
        var state = new GameState();

        Assert.Equal(GameLocationState.Empty, state.Location);
        Assert.False(state.Location.HasLocation);
        Assert.Null(state.Location.SeenUtc);
    }

    [Fact]
    public void PublishLocation_ReplacesImmutableSnapshot_AndNotifiesOnce()
    {
        var state = new GameState();
        var seen = DateTime.Parse("2026-09-12T12:00:00.000Z").ToUniversalTime();
        var snapshot = new GameLocationState(
            "New Babbage", null, "Stanton4_NewBabbage", false, seen);
        int changed = 0;
        int locationChanged = 0;
        state.Changed += () => changed++;
        state.LocationChanged += () => locationChanged++;

        state.PublishLocation(snapshot);
        state.PublishLocation(snapshot); // identical evidence is not a second state transition

        Assert.Same(snapshot, state.Location);
        Assert.Equal(1, changed);
        Assert.Equal(1, locationChanged);
    }

    [Fact]
    public void LocationTracker_PublishesNormalizedLocationIntoSharedState()
    {
        var state = new GameState();
        using var feed = new GameLogFeed();
        using var tracker = new LocationTracker(feed, state);

        tracker.Ingest(E(LocationLine("Stanton4_NewBabbage", "2026-09-12T12:01:00.000Z")));

        var location = state.Location;
        Assert.Equal("New Babbage", location.Label);
        Assert.Equal("Stanton4_NewBabbage", location.RawToken);
        Assert.Null(location.UexLocation);
        Assert.False(location.IsJurisdiction);
        Assert.Equal(tracker.LastSeenUtc, location.SeenUtc);
    }

    [Fact]
    public void SamePlaceEvidence_RefreshesGameStateWhileLegacyChangedRemainsTransitionOnly()
    {
        var state = new GameState();
        using var feed = new GameLogFeed();
        using var tracker = new LocationTracker(feed, state);
        int trackerChanged = 0;
        int stateChanged = 0;
        tracker.Changed += () => trackerChanged++;
        state.LocationChanged += () => stateChanged++;

        tracker.Ingest(E(LocationLine("Stanton4_NewBabbage", "2026-09-12T12:01:00.000Z")));
        tracker.Ingest(E(LocationLine("Stanton4_NewBabbage", "2026-09-12T12:05:00.000Z")));

        Assert.Equal(1, trackerChanged); // inherited event contract is unchanged
        Assert.Equal(2, stateChanged);   // shared snapshot exposes the newer freshness evidence
        Assert.Equal(
            DateTime.Parse("2026-09-12T12:05:00.000Z").ToUniversalTime(),
            state.Location.SeenUtc);
    }

    [Fact]
    public void PlayerPlace_ReadsLocationAndRawTokenFromGameState()
    {
        var state = new GameState();
        var seen = DateTime.Parse("2026-09-12T12:10:00.000Z").ToUniversalTime();
        state.PublishLocation(new GameLocationState(
            "Pyro Gateway Station",
            "Pyro Gateway (Stanton)",
            "RR_JP_StantonPyro",
            false,
            seen));

        var player = new PlayerPlace(MapCatalog.LoadEmbedded(), state);

        Assert.Equal("Pyro Gateway Station", player.Label);
        Assert.Equal("Pyro Gateway (Stanton)", player.UexLocation);
        Assert.Equal(seen, player.SeenUtc);
        Assert.Equal("Stanton", player.System); // raw token disambiguates the repeated gateway name
        Assert.NotNull(player.MeasureFrom(sessionLive: true));
        Assert.Null(player.MeasureFrom(sessionLive: false));
    }
}
