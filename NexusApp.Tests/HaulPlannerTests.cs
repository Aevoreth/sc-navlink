using NexusApp.Models;
using NexusApp.Services;
using Xunit;

namespace NexusApp.Tests;

public class HaulPlannerTests
{
    private const string MidA = "a6c598e0-e8d2-4613-a7a3-e80f4c30d9c6";
    private const string MidB = "b7d6a9f1-e8d2-4613-a7a3-e80f4c30d9c6";
    private const string MidC = "c8e7b0a2-e8d2-4613-a7a3-e80f4c30d9c6";

    private static GameHaulSummary Sum(string id, string company = "Red Wind", int? cap = null)
        => new(id, company, true, HaulOutcome.Active, cap);

    private static GameHaulStop P(string loc, string commodity, int scu, string mid)
        => new(loc, commodity, scu, mid);

    private static GameHaulingState State(
        GameHaulSummary[] hauls,
        GameHaulStop[] pickups,
        GameHaulStop[] dropoffs)
        => new(hauls, pickups, dropoffs);

    private static GameHaulingState TwoSharedAb()
        => State(
            new[] { Sum(MidA), Sum(MidB, "Covalex") },
            new[]
            {
                P("Everus Harbor", "Laranite", 16, MidA),
                P("Everus Harbor", "Titanium", 16, MidB),
            },
            new[]
            {
                P("Area18", "Laranite", 16, MidA),
                P("Area18", "Titanium", 16, MidB),
            });

    private static GameHaulingState TwoIncompatible()
        => State(
            new[] { Sum(MidA), Sum(MidB, "Covalex") },
            new[]
            {
                P("Everus Harbor", "Laranite", 16, MidA),
                P("Orison", "Agricium", 8, MidB),
            },
            new[]
            {
                P("Area18", "Laranite", 16, MidA),
                P("Checkmate", "Agricium", 8, MidB),
            });

    [Fact]
    public void Parse_Unknown_IsModerate()
    {
        Assert.Equal(HaulComplexity.Moderate, HaulPlanner.Parse(null));
        Assert.Equal(HaulComplexity.Moderate, HaulPlanner.Parse(""));
        Assert.Equal(HaulComplexity.Simple, HaulPlanner.Parse("simple"));
        Assert.Equal("MODERATE", HaulPlanner.ToStored(HaulComplexity.Moderate));
    }

    [Fact]
    public void Moderate_CombinesTwoContractsSharingAB()
    {
        var result = HaulPlanner.Plan(TwoSharedAb(), HaulComplexity.Moderate, HaulCapacity.Unknown);

        Assert.Equal(new[] { MidA, MidB }, result.SelectedMissionIds);
        Assert.Empty(result.Holds);
        Assert.Equal(2, result.Plan.Stops.Count);
        Assert.Equal("Everus Harbor", result.Plan.CurrentStop?.Location.Label);
        Assert.Equal("Area18", result.Plan.NextStop?.Location.Label);
        Assert.Equal(2, result.Plan.Stops[0].Actions.Count);
        Assert.Equal(2, result.Plan.Stops[1].Actions.Count);
        Assert.True(result.CapacityUnknown);
    }

    [Fact]
    public void Simple_KeepsOne_WhenOriginsDiffer()
    {
        var result = HaulPlanner.Plan(TwoIncompatible(), HaulComplexity.Simple, HaulCapacity.Unknown);

        Assert.Equal(new[] { MidA }, result.SelectedMissionIds);
        var hold = Assert.Single(result.Holds);
        Assert.Equal(MidB, hold.MissionId);
        Assert.Equal(HaulHoldReason.Complexity, hold.Reason);
        Assert.Equal("Everus Harbor", result.Plan.Stops[0].Location.Label);
        Assert.DoesNotContain(result.Plan.Stops, s => s.Location.Label == "Orison");
    }

    [Fact]
    public void Complex_KeepsBothIncompatible_WhenCapacityAllows()
    {
        var hull = new HaulCapacity(64, 32);
        var simple = HaulPlanner.Plan(TwoIncompatible(), HaulComplexity.Simple, hull);
        var complex = HaulPlanner.Plan(TwoIncompatible(), HaulComplexity.Complex, hull);

        Assert.Single(simple.SelectedMissionIds);
        Assert.Equal(new[] { MidA, MidB }, complex.SelectedMissionIds);
        Assert.Empty(complex.Holds);
        Assert.True(complex.Plan.Stops.Count >= 3);
    }

    [Fact]
    public void OverCapacity_NeverPublishesMoreThanTheHull()
    {
        var hull = new HaulCapacity(20, 32);
        var result = HaulPlanner.Plan(TwoSharedAb(), HaulComplexity.Moderate, hull);

        Assert.Equal(new[] { MidA }, result.SelectedMissionIds);
        var hold = Assert.Single(result.Holds);
        Assert.Equal(MidB, hold.MissionId);
        Assert.Equal(HaulHoldReason.Capacity, hold.Reason);
        Assert.Equal(16, HaulPlanner.PlannedPickupScu(result.Plan));
        Assert.True(HaulPlanner.PlannedPickupScu(result.Plan) <= 20);
    }

    [Fact]
    public void SwitchingModerateToSimple_ChangesThePlanShape()
    {
        var hull = new HaulCapacity(128, 32);
        var mixed = State(
            new[] { Sum(MidA), Sum(MidB, "Covalex"), Sum(MidC, "Headhunters") },
            new[]
            {
                P("Everus Harbor", "Laranite", 8, MidA),
                P("Everus Harbor", "Titanium", 8, MidB),
                P("Orison", "Agricium", 8, MidC),
            },
            new[]
            {
                P("Area18", "Laranite", 8, MidA),
                P("Checkmate", "Titanium", 8, MidB),
                P("Grim HEX", "Agricium", 8, MidC),
            });

        var moderate = HaulPlanner.Plan(mixed, HaulComplexity.Moderate, hull);
        var simple = HaulPlanner.Plan(mixed, HaulComplexity.Simple, hull);

        Assert.Equal(new[] { MidA, MidB }, moderate.SelectedMissionIds);
        Assert.Equal(new[] { MidA }, simple.SelectedMissionIds);
        Assert.Contains(simple.Holds, h => h.MissionId == MidB && h.Reason == HaulHoldReason.Complexity);
        Assert.Contains(simple.Holds, h => h.MissionId == MidC && h.Reason == HaulHoldReason.Complexity);
    }

    [Fact]
    public void Advanced_TakesAPartialSlice_WithoutDroppingTheRestOfTheObligationFromState()
    {
        var hauling = TwoSharedAb();
        var result = HaulPlanner.Plan(hauling, HaulComplexity.Advanced, new HaulCapacity(24, 32));

        Assert.Equal(new[] { MidA, MidB }, result.SelectedMissionIds);
        Assert.Empty(result.Holds);
        Assert.Equal(24, HaulPlanner.PlannedPickupScu(result.Plan));
        var titanium = result.Plan.Stops
            .SelectMany(s => s.Actions)
            .Where(a => a.Commodity == "Titanium")
            .ToList();
        Assert.All(titanium, a => Assert.Equal(8, a.PlannedScu));
        Assert.Equal(16, hauling.Pickups.First(p => p.MissionId == MidB).Scu);
        Assert.Equal(16, hauling.Dropoffs.First(p => p.MissionId == MidB).Scu);
    }

    [Fact]
    public void ContainerLargerThanHull_IsHeld()
    {
        var hauling = State(
            new[] { Sum(MidA, cap: 32) },
            new[] { P("Everus Harbor", "Laranite", 32, MidA) },
            new[] { P("Area18", "Laranite", 32, MidA) });
        var result = HaulPlanner.Plan(hauling, HaulComplexity.Moderate, new HaulCapacity(64, 16));

        Assert.Empty(result.SelectedMissionIds);
        var hold = Assert.Single(result.Holds);
        Assert.Equal(HaulHoldReason.ContainerCap, hold.Reason);
        Assert.False(result.Plan.HasStops);
    }

    [Fact]
    public void HaulTracker_UsesPlannerNotEveryContractDump()
    {
        var state = new GameState();
        state.PublishActiveShip(new GameActiveShipState(
            "test-hull", "Test Hull", 2, GameActiveShipProvenance.Hangar));
        using var tracker = new HaulTracker(gameState: state);
        tracker.Ingest(new GameLogEntry { Raw = HaulLogParserFixtures.MarkerPickup, Category = LogCategory.Other });
        tracker.Ingest(new GameLogEntry { Raw = HaulLogParserFixtures.MarkerDropoff, Category = LogCategory.Other });
        tracker.Ingest(new GameLogEntry { Raw = HaulLogParserFixtures.DeliverLine, Category = LogCategory.Other });
        tracker.Ingest(new GameLogEntry { Raw = HaulLogParserFixtures.AcceptRouteLine, Category = LogCategory.Other });

        Assert.Single(state.Hauling.Hauls);
        Assert.Equal(158, Assert.Single(state.Hauling.Pickups).Scu);
        Assert.False(state.Route.HasStops);
        var result = HaulPlanner.Plan(state, HaulComplexity.Moderate);
        Assert.Empty(result.SelectedMissionIds);
        Assert.Equal(HaulHoldReason.Capacity, Assert.Single(result.Holds).Reason);
    }

    [Fact]
    public void AdvancedPartial_DoesNotCreateCargoLots()
    {
        var state = new GameState();
        using var tracker = new HaulTracker(gameState: state);
        tracker.Ingest(new GameLogEntry { Raw = HaulLogParserFixtures.MarkerDropoff, Category = LogCategory.Other });
        tracker.Ingest(new GameLogEntry { Raw = HaulLogParserFixtures.DeliverLine, Category = LogCategory.Other });

        var hauling = State(
            new[] { Sum(MidA) },
            new[] { P("Everus Harbor", "Carbon", 32, MidA) },
            new[] { P("Jackson's Swap", "Carbon", 32, MidA) });
        var result = HaulPlanner.Plan(hauling, HaulComplexity.Advanced, new HaulCapacity(16, 32));

        Assert.Equal(16, HaulPlanner.PlannedPickupScu(result.Plan));
        Assert.Equal(GameCargoState.Empty, state.Cargo);
        Assert.Empty(state.Cargo.Lots);
    }
}
