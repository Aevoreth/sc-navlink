using NexusApp.Models;
using NexusApp.Services;
using Xunit;

namespace NexusApp.Tests;

public class GameStateRefineryTests
{
    private static DateTime T => DateTime.Parse("2026-09-12T14:00:00.000Z").ToUniversalTime();

    private static WorkOrder Order(
        string id,
        string resources,
        WorkOrderStatus status,
        string refinery = "ARC-L1",
        string? notes = null,
        DateTime? timerEnd = null)
        => new()
        {
            Id = id,
            Label = resources,
            Resources = resources,
            Location = "Aberdeen",
            Refinery = refinery,
            Status = status,
            Notes = notes ?? "",
            CreatedAt = T,
            TimerStart = timerEnd is null ? null : T,
            TimerEnd = timerEnd,
        };

    [Fact]
    public void NewState_StartsWithHonestEmptyRefinery()
    {
        var state = new GameState();

        Assert.Equal(GameRefineryState.Empty, state.Refinery);
        Assert.False(state.Refinery.HasOpenJobs);
        Assert.Equal(0, state.Refinery.ReadyCount);
        Assert.Equal(0, state.Refinery.InProgressCount);
        Assert.Empty(state.Refinery.Jobs);
    }

    [Fact]
    public void PublishRefinery_ReplacesImmutableSnapshot_AndNotifiesOnce()
    {
        var state = new GameState();
        var snapshot = RefineryJobProjection.FromOrders(new[]
        {
            Order("wo-1", "Quantanium", WorkOrderStatus.Refining, timerEnd: T.AddHours(2)),
        });
        int changed = 0;
        int refineryChanged = 0;
        state.Changed += () => changed++;
        state.RefineryChanged += () => refineryChanged++;

        state.PublishRefinery(snapshot);
        state.PublishRefinery(RefineryJobProjection.FromOrders(new[]
        {
            Order("wo-1", "Quantanium", WorkOrderStatus.Refining, timerEnd: T.AddHours(2)),
        }));

        Assert.Equal(1, changed);
        Assert.Equal(1, refineryChanged);
        Assert.True(state.Refinery.HasOpenJobs);
        Assert.Equal(1, state.Refinery.InProgressCount);
        Assert.Equal("Quantanium", state.Refinery.Jobs[0].Resources);
    }

    [Fact]
    public void FromOrders_KeepsPersistedFacts_AndDropsEditorChrome()
    {
        var order = Order("wo-2", "Bexalite", WorkOrderStatus.ReadyToCollect, notes: "bay 3");
        order.Label = "Night haul";

        var snapshot = RefineryJobProjection.FromOrders(new[] { order });
        var job = Assert.Single(snapshot.Jobs);

        Assert.Equal("wo-2", job.Id);
        Assert.Equal("Night haul", job.Label);
        Assert.Equal("Bexalite", job.Resources);
        Assert.Equal("Aberdeen", job.Location);
        Assert.Equal("ARC-L1", job.Refinery);
        Assert.Equal(WorkOrderStatus.ReadyToCollect, job.Status);
        Assert.Equal("bay 3", job.Notes);
        Assert.True(job.IsReady);
        Assert.False(job.IsInProgress);
        Assert.Equal(1, snapshot.ReadyCount);
        Assert.Equal(0, snapshot.InProgressCount);
    }

    [Fact]
    public void FromOrders_IncludesCompleteJobs_AndPreservesOrder()
    {
        var orders = new[]
        {
            Order("ready", "Gold", WorkOrderStatus.ReadyToCollect),
            Order("done", "Laranite", WorkOrderStatus.Complete),
            Order("mine", "Quantanium", WorkOrderStatus.Mining),
        };

        var snapshot = RefineryJobProjection.FromOrders(orders);

        Assert.Equal(new[] { "ready", "done", "mine" }, snapshot.Jobs.Select(j => j.Id));
        Assert.True(snapshot.HasOpenJobs);
        Assert.Equal(1, snapshot.ReadyCount);
        Assert.Equal(1, snapshot.InProgressCount);
        Assert.False(snapshot.Jobs[1].IsOpen);
    }

    [Fact]
    public void FromOrders_EmptyListIsEmptySlice()
    {
        Assert.Equal(GameRefineryState.Empty, RefineryJobProjection.FromOrders([]));
    }

    [Fact]
    public void LogReset_DoesNotClearRefinery()
    {
        var state = new GameState();
        using var feed = new GameLogFeed(state);
        state.PublishRefinery(RefineryJobProjection.FromOrders(new[]
        {
            Order("wo-3", "Quantanium", WorkOrderStatus.Refining, timerEnd: T.AddHours(1)),
        }));
        int refineryChanged = 0;
        state.RefineryChanged += () => refineryChanged++;

        feed.HandleLogReset();

        Assert.True(state.Refinery.HasOpenJobs);
        Assert.Equal("Quantanium", state.Refinery.Jobs[0].Resources);
        Assert.Equal(1, state.Session.LogGeneration);
        Assert.Equal(0, refineryChanged);
    }
}
