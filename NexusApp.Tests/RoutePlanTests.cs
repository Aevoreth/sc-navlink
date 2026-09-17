using NexusApp.Models;
using NexusApp.Services;
using Xunit;

namespace NexusApp.Tests;

public class RoutePlanMathTests
{
    private const string Mid = "a6c598e0-e8d2-4613-a7a3-e80f4c30d9c6";

    internal static GameRouteAction Act(
        string id,
        GameRouteActionKind kind,
        string commodity,
        int scu,
        GameRouteSkipRisk risk = GameRouteSkipRisk.None,
        string skipReason = "",
        int? remaining = null,
        string objective = Mid) =>
        new(
            id,
            kind,
            commodity,
            scu,
            remaining ?? scu,
            objective,
            kind is GameRouteActionKind.Pickup or GameRouteActionKind.Delivery ? "Hauling contract" : "Trade",
            risk,
            skipReason);

    internal static GameRouteAction Contract(string id, GameRouteActionKind kind, string commodity, int scu) =>
        Act(
            id,
            kind,
            commodity,
            scu,
            GameRouteSkipRisk.Detrimental,
            $"Skipping this {Verb(kind)} fails the hauling contract for {commodity}.");

    internal static GameRouteStop Stop(
        string id,
        string label,
        params GameRouteAction[] actions) =>
        new(id, 0, new GameRouteLocation(label), actions);

    internal static GameRoutePlan MixedThree() =>
        RoutePlanMath.Build(new[]
        {
            Stop("s1", "Everus Harbor", Contract("a1", GameRouteActionKind.Pickup, "Laranite", 32)),
            Stop("s2", "Area18", Act("a2", GameRouteActionKind.Buy, "Titanium", 16)),
            Stop("s3", "Jackson's Swap",
                Contract("a3", GameRouteActionKind.Delivery, "Laranite", 32),
                Act("a4", GameRouteActionKind.Sell, "Titanium", 16)),
        });

    private static string Verb(GameRouteActionKind kind) => kind switch
    {
        GameRouteActionKind.Pickup => "pickup",
        GameRouteActionKind.Delivery => "delivery",
        _ => kind.ToString().ToLowerInvariant(),
    };

    [Fact]
    public void MixedFixture_HasThreeStopsAndCurrentToNextLeg()
    {
        var plan = MixedThree();

        Assert.Equal(3, plan.Stops.Count);
        Assert.Equal(new[] { 0, 1, 2 }, plan.Stops.Select(s => s.Sequence));
        Assert.Equal("Everus Harbor", plan.CurrentStop?.Location.Label);
        Assert.Equal("Area18", plan.NextStop?.Location.Label);
        Assert.Equal("Everus Harbor", plan.CurrentLeg.From?.Location.Label);
        Assert.Equal("Area18", plan.CurrentLeg.To?.Location.Label);
        Assert.Equal(GameRouteStatus.Planned, plan.Status);
        Assert.Contains(plan.Stops[2].Actions, a => a.Kind == GameRouteActionKind.Delivery);
        Assert.Contains(plan.Stops[2].Actions, a => a.Kind == GameRouteActionKind.Sell);
        Assert.Equal(0, RoutePlanMath.VisitOrder(GameRouteActionKind.Pickup));
        Assert.True(RoutePlanMath.VisitOrder(GameRouteActionKind.Buy)
            < RoutePlanMath.VisitOrder(GameRouteActionKind.Delivery));
    }

    [Fact]
    public void DetrimentalWithoutReason_IsRejected()
    {
        var draft = Act("x", GameRouteActionKind.Pickup, "Carbon", 8, GameRouteSkipRisk.Detrimental, "");
        Assert.False(RoutePlanMath.TryNormalizeAction(draft, out _));
        Assert.Throws<ArgumentException>(() =>
            RoutePlanMath.Build(new[] { Stop("s", "Area18", draft) }));
    }

    [Fact]
    public void Skip_JumpsCurrentAndRecordsDetrimentalReason()
    {
        var skipped = RoutePlanMath.WithStopSkipped(MixedThree(), "s1");

        Assert.True(skipped.Stops[0].Skipped);
        Assert.Equal("Area18", skipped.CurrentStop?.Location.Label);
        Assert.Equal("Jackson's Swap", skipped.NextStop?.Location.Label);
        Assert.Equal(1, skipped.SkippedStopCount);
        Assert.Equal(1, skipped.DetrimentalSkipCount);
        Assert.Equal(
            "Skipping this pickup fails the hauling contract for Laranite.",
            Assert.Single(skipped.SkipWarnings));
    }

    [Fact]
    public void SkipNoneRisk_ProducesNoWarning()
    {
        var skipped = RoutePlanMath.WithStopSkipped(MixedThree(), "s2");

        Assert.Equal("Everus Harbor", skipped.CurrentStop?.Location.Label);
        Assert.Equal("Jackson's Swap", skipped.NextStop?.Location.Label);
        Assert.Equal(1, skipped.SkippedStopCount);
        Assert.Equal(0, skipped.DetrimentalSkipCount);
        Assert.Empty(skipped.SkipWarnings);
    }

    [Fact]
    public void PartialAction_KeepsStopCurrent_AndMarksInProgress()
    {
        var partial = RoutePlanMath.WithActionRemaining(MixedThree(), "a1", 8);

        Assert.Equal("Everus Harbor", partial.CurrentStop?.Location.Label);
        Assert.Equal(GameRouteStatus.InProgress, partial.Status);
        Assert.Equal(8, partial.Stops[0].Actions[0].RemainingScu);
        Assert.True(partial.Stops[0].Actions[0].IsPartial);
    }

    [Fact]
    public void CompletingCurrent_AdvancesCursor()
    {
        var done = RoutePlanMath.WithActionRemaining(MixedThree(), "a1", 0);

        Assert.Equal("Area18", done.CurrentStop?.Location.Label);
        Assert.Equal("Jackson's Swap", done.NextStop?.Location.Label);
        Assert.Equal(GameRouteStatus.InProgress, done.Status);
    }

    [Fact]
    public void SkippingAllWork_CompletesThePlan()
    {
        var plan = RoutePlanMath.WithStopSkipped(MixedThree(), "s1");
        plan = RoutePlanMath.WithStopSkipped(plan, "s2");
        plan = RoutePlanMath.WithStopSkipped(plan, "s3");

        Assert.Null(plan.CurrentStop);
        Assert.Equal(GameRouteStatus.Complete, plan.Status);
        Assert.Equal(3, plan.SkippedStopCount);
        Assert.Equal(2, plan.DetrimentalSkipCount);
    }

    [Fact]
    public void Insert_RenumbersAndMovesCursor()
    {
        var extra = Stop("s0", "Orison", Act("a0", GameRouteActionKind.Buy, "Agricium", 4));
        var plan = RoutePlanMath.WithStopInserted(MixedThree(), extra, 0);

        Assert.Equal(new[] { "s0", "s1", "s2", "s3" }, plan.Stops.Select(s => s.Id));
        Assert.Equal(new[] { 0, 1, 2, 3 }, plan.Stops.Select(s => s.Sequence));
        Assert.Equal("Orison", plan.CurrentStop?.Location.Label);
        Assert.Equal("Everus Harbor", plan.NextStop?.Location.Label);
        Assert.False(plan.Stops[0].Skipped);
    }

    [Fact]
    public void Reorder_FollowsNewSequence_AndKeepsSkipRisk()
    {
        var skipped = RoutePlanMath.WithStopSkipped(MixedThree(), "s1");
        var plan = RoutePlanMath.WithStopsReordered(skipped, new[] { "s3", "s1", "s2" });

        Assert.Equal(new[] { "s3", "s1", "s2" }, plan.Stops.Select(s => s.Id));
        Assert.Equal(new[] { 0, 1, 2 }, plan.Stops.Select(s => s.Sequence));
        Assert.Equal("Jackson's Swap", plan.CurrentStop?.Location.Label);
        Assert.Equal("Area18", plan.NextStop?.Location.Label);
        Assert.True(plan.Stops[1].Skipped);
        Assert.Equal(GameRouteSkipRisk.Detrimental, plan.Stops[1].Actions[0].SkipRisk);
        Assert.Single(plan.SkipWarnings);
    }

    [Fact]
    public void Reorder_RejectsUnknownOrPartialIds()
    {
        var plan = MixedThree();
        Assert.Throws<ArgumentException>(() =>
            RoutePlanMath.WithStopsReordered(plan, new[] { "s1", "s2" }));
        Assert.Throws<ArgumentException>(() =>
            RoutePlanMath.WithStopsReordered(plan, new[] { "s1", "s2", "nope" }));
    }
}

public class RouteProjectionTests
{
    private const string Mid = "a6c598e0-e8d2-4613-a7a3-e80f4c30d9c6";

    private static GameHaulingState Hauling(GameHaulStop[] pickups, GameHaulStop[] dropoffs) =>
        new(
            new[] { new GameHaulSummary(Mid, "Red Wind", true, HaulOutcome.Active) },
            pickups,
            dropoffs);

    [Fact]
    public void EmptyHauling_YieldsEmptyRoute()
    {
        Assert.Equal(GameRoutePlan.Empty, RouteProjection.FromHauling(GameHaulingState.Empty));
    }

    [Fact]
    public void TwoLocations_PickupThenDelivery_AreDetrimental()
    {
        var hauling = Hauling(
            new[] { new GameHaulStop("Everus Harbor", "Laranite", 32, Mid) },
            new[] { new GameHaulStop("Jackson's Swap", "Laranite", 32, Mid) });

        var plan = RouteProjection.FromHauling(hauling);

        Assert.Equal(2, plan.Stops.Count);
        Assert.Equal("Everus Harbor", plan.Stops[0].Location.Label);
        Assert.Equal("Jackson's Swap", plan.Stops[1].Location.Label);
        var pickup = Assert.Single(plan.Stops[0].Actions);
        var drop = Assert.Single(plan.Stops[1].Actions);
        Assert.Equal(GameRouteActionKind.Pickup, pickup.Kind);
        Assert.Equal(GameRouteActionKind.Delivery, drop.Kind);
        Assert.Equal(GameRouteSkipRisk.Detrimental, pickup.SkipRisk);
        Assert.Equal(
            "Skipping this pickup fails the hauling contract for Laranite.",
            pickup.SkipRiskReason);
        Assert.Equal(
            "Skipping this delivery fails the hauling contract for Laranite.",
            drop.SkipRiskReason);
        Assert.Equal("Everus Harbor", plan.CurrentStop?.Location.Label);
    }

    [Fact]
    public void SameLocation_MergesPickupAndDelivery()
    {
        var hauling = Hauling(
            new[] { new GameHaulStop("Area18", "Carbon", 8, Mid) },
            new[] { new GameHaulStop("Area18", "Carbon", 8, Mid) });

        var plan = RouteProjection.FromHauling(hauling);
        var stop = Assert.Single(plan.Stops);
        Assert.Equal(2, stop.Actions.Count);
        Assert.Equal(GameRouteActionKind.Pickup, stop.Actions[0].Kind);
        Assert.Equal(GameRouteActionKind.Delivery, stop.Actions[1].Kind);
    }
}

public class GameStateRouteTests
{
    private const string Mid = "a6c598e0-e8d2-4613-a7a3-e80f4c30d9c6";

    [Fact]
    public void NewState_StartsEmpty()
    {
        var state = new GameState();
        Assert.Equal(GameRoutePlan.Empty, state.Route);
        Assert.False(state.Route.HasStops);
    }

    [Fact]
    public void PublishRoute_ReplacesOnceOnIdentical()
    {
        var state = new GameState();
        var snap = RoutePlanMathTests.MixedThree();
        int changed = 0, routeChanged = 0;
        state.Changed += () => changed++;
        state.RouteChanged += () => routeChanged++;

        state.PublishRoute(snap);
        state.PublishRoute(RoutePlanMathTests.MixedThree());

        Assert.Equal(1, changed);
        Assert.Equal(1, routeChanged);
        Assert.True(state.Route.HasStops);
    }

    [Fact]
    public void HaulTracker_PublishesRouteFromContractStops()
    {
        var state = new GameState();
        using var tracker = new HaulTracker(gameState: state);
        tracker.Ingest(new GameLogEntry { Raw = HaulLogParserFixtures.MarkerDropoff, Category = LogCategory.Other });
        tracker.Ingest(new GameLogEntry { Raw = HaulLogParserFixtures.DeliverLine, Category = LogCategory.Other });

        var stop = Assert.Single(state.Route.Stops);
        Assert.Equal("Jackson's Swap", stop.Location.Label);
        var action = Assert.Single(stop.Actions);
        Assert.Equal(GameRouteActionKind.Delivery, action.Kind);
        Assert.Equal(GameRouteSkipRisk.Detrimental, action.SkipRisk);
        Assert.Equal(158, action.PlannedScu);
    }

    [Fact]
    public void HaulClear_PublishesEmptyRoute()
    {
        var state = new GameState();
        using var tracker = new HaulTracker(gameState: state);
        tracker.Ingest(new GameLogEntry { Raw = HaulLogParserFixtures.MarkerDropoff, Category = LogCategory.Other });
        tracker.Ingest(new GameLogEntry { Raw = HaulLogParserFixtures.DeliverLine, Category = LogCategory.Other });
        Assert.True(state.Route.HasStops);

        tracker.Ingest(new GameLogEntry
        {
            Raw = "<2026-06-27T14:30:51.596Z> [Notice] <CDisciplineServiceExternal::EndSession> Ending session [AntiCheat][EAC]",
            Category = LogCategory.Other,
        });

        Assert.Equal(GameRoutePlan.Empty, state.Route);
        Assert.Equal(GameHaulingState.Empty, state.Hauling);
    }
}

public class NextPlannerRouteTests
{
    private const string Mid = "a6c598e0-e8d2-4613-a7a3-e80f4c30d9c6";
    private static readonly DateTime Seen = DateTime.Parse("2026-09-13T18:00:00.000Z").ToUniversalTime();

    private static GameLocationState At(string? label) =>
        new(label, null, null, false, label is null ? null : Seen);

    private static GameHaulingState Hauling(GameHaulStop[] pickups, GameHaulStop[] dropoffs) =>
        new(
            new[] { new GameHaulSummary(Mid, "Red Wind", true, HaulOutcome.Active) },
            pickups,
            dropoffs);

    [Fact]
    public void RoutePath_RanksBuyAndSell_AndOmitsSkippedStops()
    {
        var route = RoutePlanMath.WithStopSkipped(RoutePlanMathTests.MixedThree(), "s1");
        var rec = NextPlanner.Plan(new NextPlannerInput(At("Area18"), GameHaulingState.Empty, route));

        Assert.Equal(NextActionKind.TradeBuy, rec.Primary?.Kind);
        Assert.Equal(NextModule.Trade, rec.Primary?.Module);
        Assert.Equal(NextPlanner.ScoreAtPickup, rec.Primary?.Score);
        Assert.Equal(NextConfidence.Observed, rec.Primary?.Confidence);
        Assert.DoesNotContain(rec.Actions, a => a.Location == "Everus Harbor");
        Assert.Contains(rec.Actions, a => a.Kind == NextActionKind.TradeSell);
        Assert.Contains("Recommendations use the shared route plan.", rec.Assumptions);
        Assert.StartsWith("Next: buy", rec.Summary);
    }

    [Fact]
    public void HaulingOnlyProjection_RanksTheSameAsHaulingPath()
    {
        var hauling = Hauling(
            new[] { new GameHaulStop("New Babbage", "Laranite", 32, Mid) },
            new[] { new GameHaulStop("Jackson's Swap", "Carbon", 158, Mid) });
        var location = At("New Babbage");
        var fromHauling = NextPlanner.Plan(location, hauling);
        var fromRoute = NextPlanner.Plan(new NextPlannerInput(
            location, hauling, RouteProjection.FromHauling(hauling)));

        Assert.Equal(
            fromHauling.Actions.Select(a => (a.Kind, a.Location, a.Objective, a.Scu, a.Score, a.Rank)),
            fromRoute.Actions.Select(a => (a.Kind, a.Location, a.Objective, a.Scu, a.Score, a.Rank)));
        Assert.Equal(fromHauling.Primary?.Explanation, fromRoute.Primary?.Explanation);
    }

    [Fact]
    public void PlanFromGameState_UsesRouteWhenPublished()
    {
        var state = new GameState();
        state.PublishLocation(At("Area18"));
        state.PublishRoute(RoutePlanMathTests.MixedThree());

        var rec = NextPlanner.Plan(state);
        Assert.Contains("shared route plan", rec.Assumptions[0]);
        Assert.Equal("Area18", rec.Primary?.Location);
        Assert.Equal(NextActionKind.TradeBuy, rec.Primary?.Kind);
    }
}
