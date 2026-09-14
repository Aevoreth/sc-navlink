using NexusApp.Models;
using NexusApp.Services;
using Xunit;

namespace NexusApp.Tests;

public class NextPlannerTests
{
    private const string Mid = "a6c598e0-e8d2-4613-a7a3-e80f4c30d9c6";
    private const string Mid2 = "b7d609f1-f9e3-5724-b8b4-f91f5d41e0d7";
    private static readonly DateTime Seen = DateTime.Parse("2026-09-13T18:00:00.000Z").ToUniversalTime();

    private static GameLocationState At(string? label, string? raw = null) =>
        new(label, null, raw, false, label is null ? null : Seen);

    private static GameHaulStop Stop(string location, string commodity, int scu, string? mission = null) =>
        new(location, commodity, scu, mission ?? Mid);

    private static GameHaulingState Hauling(GameHaulStop[] pickups, GameHaulStop[] dropoffs) =>
        new(
            new[] { new GameHaulSummary(Mid, "Red Wind", true, HaulOutcome.Active) },
            pickups,
            dropoffs);

    [Fact]
    public void EmptyHauling_ReturnsNoActionsAndHonestSummary()
    {
        var plan = NextPlanner.Plan(GameLocationState.Empty, GameHaulingState.Empty);

        Assert.Empty(plan.Actions);
        Assert.Null(plan.Primary);
        Assert.Equal("No hauling stop is known.", plan.Summary);
        Assert.Contains("Current location is unknown.", plan.Assumptions);
        Assert.Contains("Cargo inventory is not used.", plan.Assumptions);
        Assert.Null(plan.LocationSeenUtc);
    }

    [Fact]
    public void PickupAtCurrentLocation_RanksAheadOfDistantDropoff()
    {
        var hauling = Hauling(
            new[] { Stop("New Babbage", "Laranite", 32) },
            new[] { Stop("Jackson's Swap", "Carbon", 158) });

        var plan = NextPlanner.Plan(At("New Babbage"), hauling);

        var primary = plan.Primary;
        Assert.NotNull(primary);
        Assert.Equal(NextActionKind.HaulPickup, primary.Kind);
        Assert.Equal("New Babbage", primary.Location);
        Assert.Equal(1, primary.Rank);
        Assert.Equal(NextPlanner.ScoreAtPickup, primary.Score);
        Assert.Equal(NextConfidence.Observed, primary.Confidence);
        Assert.Equal("You are at New Babbage. Collect 32 SCU of Laranite.", primary.Explanation);
        Assert.Equal("Next: collect 32 SCU of Laranite at New Babbage.", plan.Summary);
        Assert.Equal(Seen, plan.LocationSeenUtc);
        Assert.Equal(2, plan.Actions.Count);
        Assert.Equal(NextActionKind.HaulDropoff, plan.Actions[1].Kind);
        Assert.Equal(NextConfidence.Inferred, plan.Actions[1].Confidence);
    }

    [Fact]
    public void DropoffAtCurrentLocation_RanksAheadOfDistantPickup()
    {
        var hauling = Hauling(
            new[] { Stop("Everus Harbor", "Laranite", 32) },
            new[] { Stop("Jackson's Swap", "Carbon", 158) });

        var plan = NextPlanner.Plan(At("Jackson's Swap"), hauling);

        Assert.Equal(NextActionKind.HaulDropoff, plan.Actions[0].Kind);
        Assert.Equal("Jackson's Swap", plan.Actions[0].Location);
        Assert.Equal(NextPlanner.ScoreAtDropoff, plan.Actions[0].Score);
        Assert.Equal(NextActionKind.HaulPickup, plan.Actions[1].Kind);
        Assert.Equal("You are at Jackson's Swap. Deliver 158 SCU of Carbon.", plan.Actions[0].Explanation);
    }

    [Fact]
    public void UnknownLocation_OrdersPickupsBeforeDropoffs()
    {
        var hauling = Hauling(
            new[] { Stop("Everus Harbor", "Laranite", 32) },
            new[] { Stop("Jackson's Swap", "Carbon", 158) });

        var plan = NextPlanner.Plan(GameLocationState.Empty, hauling);

        Assert.Equal(NextActionKind.HaulPickup, plan.Actions[0].Kind);
        Assert.Equal(NextPlanner.ScorePickup, plan.Actions[0].Score);
        Assert.Equal(NextConfidence.Unknown, plan.Actions[0].Confidence);
        Assert.Equal(NextActionKind.HaulDropoff, plan.Actions[1].Kind);
        Assert.Equal("Collect 32 SCU of Laranite at Everus Harbor.", plan.Actions[0].Explanation);
    }

    [Fact]
    public void TwoPickups_HaveStableLocationThenMissionOrder()
    {
        var hauling = Hauling(
            new[]
            {
                Stop("Lorville", "Agricium", 8, Mid2),
                Stop("Area18", "Laranite", 16, Mid),
                Stop("Area18", "Titanium", 4, Mid2),
            },
            Array.Empty<GameHaulStop>());

        var first = NextPlanner.Plan(GameLocationState.Empty, hauling).Actions;
        var second = NextPlanner.Plan(GameLocationState.Empty, hauling).Actions;

        Assert.Equal(new[] { "Area18", "Area18", "Lorville" }, first.Select(a => a.Location));
        Assert.Equal(new[] { "Laranite", "Titanium", "Agricium" }, first.Select(a => a.Objective));
        Assert.Equal(first.Select(a => (a.MissionId, a.Objective, a.Rank)), second.Select(a => (a.MissionId, a.Objective, a.Rank)));
        Assert.Equal(new[] { 1, 2, 3 }, first.Select(a => a.Rank));
    }

    [Fact]
    public void MatchesLocation_IsCaseInsensitive_AndAcceptsRawToken()
    {
        var hauling = Hauling(
            Array.Empty<GameHaulStop>(),
            new[] { Stop("Jackson's Swap", "Carbon", 158) });

        var byLabel = NextPlanner.Plan(At("jackson's swap"), hauling);
        var byRaw = NextPlanner.Plan(At("Unknown Pad", "Jackson's Swap"), hauling);

        Assert.Equal(NextConfidence.Observed, byLabel.Actions[0].Confidence);
        Assert.Equal(NextConfidence.Observed, byRaw.Actions[0].Confidence);
        Assert.True(NextPlanner.MatchesLocation(At("Jackson's Swap"), "jackson's swap"));
    }

    [Fact]
    public void PlanFromGameState_ReadsLocationAndHaulingSlices()
    {
        var state = new GameState();
        state.PublishLocation(At("New Babbage"));
        state.PublishHauling(Hauling(
            new[] { Stop("New Babbage", "Laranite", 32) },
            Array.Empty<GameHaulStop>()));

        var plan = NextPlanner.Plan(state);

        Assert.Equal("New Babbage", plan.Primary?.Location);
        Assert.Equal(NextModule.Hauling, plan.Primary?.Module);
    }

    [Fact]
    public void NextAction_AcceptsFutureModulesWithoutChangingShape()
    {
        var trade = new NextAction(
            NextModule.Trade,
            NextActionKind.TradeBuy,
            "Buy Laranite",
            "Area18",
            "Laranite",
            16,
            "",
            2,
            80,
            "Buy 16 SCU of Laranite at Area18.",
            NextConfidence.Inferred,
            Array.Empty<string>());

        Assert.Equal(NextModule.Trade, trade.Module);
        Assert.Equal(NextActionKind.TradeBuy, trade.Kind);
        Assert.Equal("Laranite", trade.Objective);
    }
}
