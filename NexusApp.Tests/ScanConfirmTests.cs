using NexusApp.Services;
using Xunit;

namespace NexusApp.Tests;

// ScannerService's auto-scan debounce: an RS pin value only counts as "read" after landing the
// same on two consecutive ticks, and once confirmed the same value never re-emits ValueDetected
// again until it changes or the pin is lost. Mirrors GameProcessPresenceTests' focus on the pure
// decision core, independent of the timer/dispatcher plumbing around it.
public class ScanConfirmTests
{
    [Fact]
    public void SameValueTwice_ConfirmsAndEmitsExactlyOnce()
    {
        var c = new ScanConfirm();
        int? first = c.Update(1234, true);
        int? second = c.Update(1234, true);
        int? third = c.Update(1234, true);

        Assert.Null(first);            // first sighting - not yet confirmed
        Assert.Equal(1234, second);    // second sighting of the same value - confirms and emits
        Assert.Null(third);            // still the same value - no second emit
    }

    [Fact]
    public void DifferingValue_ResetsTheCounter()
    {
        var c = new ScanConfirm();
        c.Update(1234, true);
        Assert.Equal(1, c.PendingCount);

        int? onDiffer = c.Update(5678, true);   // a different reading starts the count over
        Assert.Null(onDiffer);
        Assert.Equal(1, c.PendingCount);

        int? confirmed = c.Update(5678, true);  // now confirms on its own second sighting
        Assert.Equal(5678, confirmed);
    }

    [Fact]
    public void PinFoundFalse_ClearsAllState()
    {
        var c = new ScanConfirm();
        c.Update(1234, true);
        Assert.Equal(1234, c.Update(1234, true));   // confirmed once already

        int? onReset = c.Update((int?)null, false);       // pin lost - clears pending/pendingCount/lastEmitted
        Assert.Null(onReset);
        Assert.Equal(0, c.PendingCount);

        // The same value has to re-earn confirmation from scratch - lastEmitted's memory is gone too.
        Assert.Null(c.Update(1234, true));
        Assert.Equal(1234, c.Update(1234, true));
    }

    [Fact]
    public void AlreadyEmittedValue_DoesNotReEmitOnALaterTick()
    {
        var c = new ScanConfirm();
        c.Update(1234, true);
        Assert.Equal(1234, c.Update(1234, true));   // confirmed, emitted once

        int? repeat1 = c.Update(1234, true);        // same value keeps reading - no second emit
        int? repeat2 = c.Update(1234, true);
        Assert.Null(repeat1);
        Assert.Null(repeat2);
    }

    [Fact]
    public void TwoSignatures_ConfirmIndependently()
    {
        var c = new ScanConfirm();
        Assert.Empty(c.Update([1234, 5678], true));
        var newly = c.Update([1234, 5678], true);

        Assert.Equal(new[] { 1234, 5678 }, newly);
        Assert.Equal(new[] { 1234, 5678 }, c.ConfirmedVisible);
    }

    [Fact]
    public void UnstableSecondSignature_DoesNotResetConfirmedFirst()
    {
        var c = new ScanConfirm();
        c.Update([1234], true);
        Assert.Equal(new[] { 1234 }, c.Update([1234], true));

        Assert.Empty(c.Update([1234, 5678], true));
        Assert.Equal(new[] { 1234 }, c.ConfirmedVisible);
        Assert.Equal(1, c.PendingCount);

        var newly = c.Update([1234, 9999], true);
        Assert.Empty(newly);
        Assert.Equal(new[] { 1234 }, c.ConfirmedVisible);
        Assert.Equal(1, c.PendingCount);

        newly = c.Update([1234, 9999], true);
        Assert.Equal(new[] { 9999 }, newly);
        Assert.Equal(new[] { 1234, 9999 }, c.ConfirmedVisible);
    }

    [Fact]
    public void ThreeSignatures_ConfirmWithoutCollapsing()
    {
        var c = new ScanConfirm();
        var first = new[] { 2200, 4800, 17300 };
        Assert.Empty(c.Update(first, true));
        Assert.Equal(first, c.Update(first, true));
        Assert.Equal(first, c.ConfirmedVisible);
    }

    [Fact]
    public void EmptyReadingWithPin_DoesNotResetPending()
    {
        var c = new ScanConfirm();
        c.Update([2200, 4800, 17300], true);
        Assert.Equal(1, c.PendingCount);

        Assert.Empty(c.Update(Array.Empty<int>(), true));
        Assert.Equal(1, c.PendingCount);

        Assert.Equal(new[] { 2200, 4800, 17300 }, c.Update([2200, 4800, 17300], true));
    }
}
