namespace NexusApp.Services;

using NexusApp.Models;

/// <summary>
/// Pure snapshot builder for the refinery GameState slice. DataService remains the
/// persistence owner; this maps durable work-order facts into immutable records.
/// Editor chrome, flyout filters, and live timer-tick display stay out.
/// </summary>
public static class RefineryJobProjection
{
    public static GameRefineryState FromOrders(IEnumerable<WorkOrder> orders)
        => new(orders.Select(ToJob).ToArray());

    public static GameRefineryJob ToJob(WorkOrder wo) => new(
        wo.Id,
        wo.Label,
        wo.Resources,
        wo.Location,
        wo.Refinery,
        wo.Status,
        wo.Notes,
        wo.CreatedAt,
        wo.TimerStart,
        wo.TimerEnd);
}
