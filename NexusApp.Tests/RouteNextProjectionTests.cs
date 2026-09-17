using NexusApp.Services;
using Xunit;

namespace NexusApp.Tests;

// 0.2 presentation fold: Operations / overlay / Starmap copy comes from this, not from a second
// planner. Remaining-work language only — the hauling seed drops completed visits.
public class RouteNextProjectionTests
{
    private static readonly DateTime Seen = DateTime.Parse("2026-09-16T18:00:00Z").ToUniversalTime();

    private static GameLocationState At(string? label) =>
        new(label, null, null, false, label is null ? null : Seen);

    private static GameActiveShipState Ship(string id, string name, int? scu) =>
        new(id, name, scu, GameActiveShipProvenance.Hangar);

    [Fact]
    public void EmptyState_KeepsCardsHonest()
    {
        var view = RouteNextProjection.From(new GameState());

        Assert.Equal(GameRouteStatus.Empty, view.Status);
        Assert.Equal(0, view.RemainingStopCount);
        Assert.Equal("No route", view.StatusLabel);
        Assert.Null(view.LegLabel);
        Assert.Null(view.Primary);
        Assert.Equal("No hauling stop is known.", view.NextSummary);
        Assert.True(view.LocationUnknown);
        Assert.Equal(NextConfidence.Unknown, view.Confidence);
        Assert.Equal("Location unknown", view.ConfidenceLabel);
        Assert.False(view.HasShip);
        Assert.Equal("No active ship", view.ShipLabel);
        Assert.Equal("No hold", view.HoldLabel);
        Assert.Null(view.CurrentLabel);
        Assert.Null(view.NextLabel);
    }

    [Fact]
    public void MixedRemainingStops_CarryCurrentNextAndPrimary()
    {
        var state = new GameState();
        state.PublishLocation(At("Everus Harbor"));
        state.PublishRoute(RoutePlanMathTests.MixedThree());

        var view = RouteNextProjection.From(state);

        Assert.Equal(GameRouteStatus.Planned, view.Status);
        Assert.Equal(3, view.RemainingStopCount);
        Assert.Equal("3 stops remaining", view.StatusLabel);
        Assert.Equal("Everus Harbor", view.CurrentLabel);
        Assert.Equal("Area18", view.NextLabel);
        Assert.Equal("Everus Harbor → Area18", view.LegLabel);
        Assert.NotNull(view.Primary);
        Assert.Equal("Everus Harbor", view.Primary!.Location);
        Assert.Equal(NextActionKind.HaulPickup, view.Primary.Kind);
        Assert.Equal(NextConfidence.Observed, view.Confidence);
        Assert.Equal("At this stop", view.ConfidenceLabel);
        Assert.False(view.LocationUnknown);
        Assert.StartsWith("Next: collect", view.NextSummary);
        Assert.Contains("shared route plan", view.Assumptions[0]);
    }

    [Fact]
    public void UnknownLocation_StillShowsRouteAndRanksByRule()
    {
        var state = new GameState();
        state.PublishRoute(RoutePlanMathTests.MixedThree());

        var view = RouteNextProjection.From(state);

        Assert.True(view.LocationUnknown);
        Assert.Equal(NextConfidence.Unknown, view.Confidence);
        Assert.Equal("Location unknown", view.ConfidenceLabel);
        Assert.Equal(3, view.RemainingStopCount);
        Assert.NotNull(view.Primary);
        // No live place, so pickup-like actions share ScorePickup and sort by location name.
        Assert.Equal("Area18", view.Primary!.Location);
        Assert.Equal(NextActionKind.TradeBuy, view.Primary.Kind);
    }

    [Fact]
    public void NoActiveShip_KeepsHoldUnknownSeparateFromRoute()
    {
        var state = new GameState();
        state.PublishLocation(At("Area18"));
        state.PublishRoute(RoutePlanMathTests.MixedThree());

        var view = RouteNextProjection.From(state);

        Assert.False(view.HasShip);
        Assert.Equal("No active ship", view.ShipLabel);
        Assert.Equal("No hold", view.HoldLabel);
        Assert.Equal(3, view.RemainingStopCount);
        Assert.NotNull(view.Primary);
    }

    [Fact]
    public void ActiveShipAndHold_FormatUsedFreeUsable()
    {
        var state = new GameState();
        state.PublishActiveShip(Ship("c2", "C2 Hercules", 696));
        state.PublishCargo(new GameCargoState(
            "c2", "C2 Hercules", Array.Empty<GameCargoLot>(),
            UsedScu: 32, UsableScu: 696, FreeScu: 664, IsOverCapacity: false));

        var view = RouteNextProjection.From(state);

        Assert.True(view.HasShip);
        Assert.Equal("C2 Hercules", view.ShipLabel);
        Assert.Equal("32 / 696 SCU · 664 free", view.HoldLabel);
        Assert.Equal(32, view.UsedScu);
        Assert.Equal(696, view.UsableScu);
        Assert.Equal(664, view.FreeScu);
        Assert.False(view.IsOverCapacity);
    }

    [Fact]
    public void OverCapacity_IsNamedNotHidden()
    {
        var state = new GameState();
        state.PublishActiveShip(Ship("100i", "Origin 100i", 2));
        state.PublishCargo(new GameCargoState(
            "100i", "Origin 100i", Array.Empty<GameCargoLot>(),
            UsedScu: 8, UsableScu: 2, FreeScu: 0, IsOverCapacity: true));

        var view = RouteNextProjection.From(state);
        Assert.Equal("8 / 2 SCU · over capacity", view.HoldLabel);
        Assert.True(view.IsOverCapacity);
    }

    [Fact]
    public void CompleteRoute_IsNotEmpty()
    {
        var plan = RoutePlanMath.WithStopSkipped(RoutePlanMathTests.MixedThree(), "s1");
        plan = RoutePlanMath.WithStopSkipped(plan, "s2");
        plan = RoutePlanMath.WithStopSkipped(plan, "s3");
        var state = new GameState();
        state.PublishRoute(plan);

        var view = RouteNextProjection.From(state);

        Assert.Equal(GameRouteStatus.Complete, view.Status);
        Assert.Equal(0, view.RemainingStopCount);
        Assert.Equal("Route complete", view.StatusLabel);
        Assert.Null(view.CurrentLabel);
        Assert.Null(view.NextLabel);
        Assert.Null(view.LegLabel);
        Assert.Equal("No remaining route stop is known.", view.NextSummary);
        Assert.Null(view.Primary);
    }

    [Fact]
    public void OneRemainingStop_UsesSingularCopy()
    {
        var plan = RoutePlanMath.WithStopSkipped(RoutePlanMathTests.MixedThree(), "s1");
        plan = RoutePlanMath.WithStopSkipped(plan, "s2");
        var state = new GameState();
        state.PublishRoute(plan);

        var view = RouteNextProjection.From(state);
        Assert.Equal(1, view.RemainingStopCount);
        Assert.Equal("1 stop remaining", view.StatusLabel);
        Assert.Equal("Jackson's Swap", view.CurrentLabel);
        Assert.Null(view.NextLabel);
        Assert.Equal("Jackson's Swap", view.LegLabel);
    }

    [Fact]
    public void StatusCopy_AndHoldCopy_AreDeterministic()
    {
        Assert.Equal("No route", RouteNextProjection.StatusCopy(GameRouteStatus.Empty, 0));
        Assert.Equal("Route complete", RouteNextProjection.StatusCopy(GameRouteStatus.Complete, 0));
        Assert.Equal("2 stops remaining", RouteNextProjection.StatusCopy(GameRouteStatus.Planned, 2));
        Assert.Equal("Everus Harbor → Area18", RouteNextProjection.LegCopy("Everus Harbor", "Area18"));
        Assert.Equal("No hold", RouteNextProjection.HoldCopy(false, 0, null, null, false));
        Assert.Equal("Capacity unknown", RouteNextProjection.HoldCopy(true, 0, null, null, false));
        Assert.Equal("12 SCU aboard", RouteNextProjection.HoldCopy(true, 12, null, null, false));
        Assert.Equal("Location unknown", RouteNextProjection.ConfidenceCopy(NextConfidence.Unknown));
        Assert.Equal("Ranked by rule", RouteNextProjection.ConfidenceCopy(NextConfidence.Inferred));
        Assert.Equal("At this stop", RouteNextProjection.ConfidenceCopy(NextConfidence.Observed));
    }
}
