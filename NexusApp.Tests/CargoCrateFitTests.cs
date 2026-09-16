using NexusApp.Models.Cargo;
using NexusApp.Services;
using NexusApp.Services.Cargo;
using Xunit;

namespace NexusApp.Tests;

public class CargoCrateFitTests
{
    private static readonly CargoShipCatalog Catalog = CargoShipCatalog.LoadEmbedded();

    [Fact]
    public void CutlassBlack_FourScuFitsEightOnGrid_NotEleven()
    {
        var ship = Catalog.ById("drak-cutlass-black")!;
        Assert.True(CargoCrateFit.SizeFits(ship, null, 4));
        Assert.Equal(8, CargoCrateFit.MaxCount(ship, null, 4, offGrid: false));
        Assert.Equal(CargoCrateFit.OffGridCountCap, CargoCrateFit.MaxCount(ship, null, 4, offGrid: true));
        Assert.True(CargoCrateFit.MaxCount(ship, null, 4, offGrid: true) >= 11);
    }

    [Fact]
    public void CutlassBlack_ThirtyTwoDoesNotLieFlat()
    {
        var ship = Catalog.ById("drak-cutlass-black")!;
        Assert.False(CargoCrateFit.SizeFits(ship, null, 32));
        Assert.Equal(0, CargoCrateFit.MaxCount(ship, null, 32, offGrid: false));
        Assert.True(CargoCrateFit.MaxCount(ship, null, 32, offGrid: true) > 0);
        Assert.False(CargoCrateFit.SizeFits(ship, null, 24));
    }

    [Fact]
    public void Raft_ThirtyTwoLiesFlat()
    {
        var ship = Catalog.ById("argo-raft")!;
        Assert.True(CargoCrateFit.SizeFits(ship, null, 32));
        Assert.True(CargoCrateFit.MaxCount(ship, null, 32, offGrid: false) >= 1);
    }

    [Fact]
    public void TwoScu_IsTwoByOneByOne_DoesNotStandOnEnd()
    {
        var two = BoxType.Of(2);
        Assert.Equal(2, two.Size.W);
        Assert.Equal(1, two.Size.D);
        Assert.Equal(1, two.Size.H);
        Assert.All(two.Orientations, ori =>
        {
            Assert.Equal(1, ori.H);
            Assert.True(ori.W == 2 && ori.D == 1 || ori.W == 1 && ori.D == 2);
        });

        var four = BoxType.Of(4);
        Assert.All(four.Orientations, ori => Assert.Equal(1, ori.H));
        Assert.Equal(2, four.Size.W);
        Assert.Equal(2, four.Size.D);

        var thirtyTwo = BoxType.Of(32);
        Assert.All(thirtyTwo.Orientations, ori => Assert.Equal(2, ori.H));

        var shaft = new ShipCargoDef
        {
            Id = "shaft",
            Grids =
            [
                new GridDef { Id = 0, W = 1, D = 1, H = 2, AcceptedCaps = [1, 2] },
            ],
        };
        Assert.False(CargoCrateFit.SizeFits(shaft, null, 2));
        Assert.Equal(0, CargoCrateFit.MaxCount(shaft, null, 2, offGrid: false));
        Assert.True(CargoCrateFit.SizeFits(shaft, null, 1));
    }

    [Fact]
    public void SixteenTwentyFourThirtyTwo_StayTwoHigh_CutlassTakesSixteenNotTwentyFour()
    {
        foreach (var scu in new[] { 16, 24, 32 })
        {
            var box = BoxType.Of(scu);
            Assert.All(box.Orientations, ori => Assert.Equal(2, ori.H));
        }
        var ship = Catalog.ById("drak-cutlass-black")!;
        Assert.True(CargoCrateFit.SizeFits(ship, null, 16));
        Assert.Equal(2, CargoCrateFit.MaxCount(ship, null, 16, offGrid: false));
        Assert.False(CargoCrateFit.SizeFits(ship, null, 24));
        Assert.False(CargoCrateFit.SizeFits(ship, null, 32));
    }

    [Fact]
    public void CutlassBlack_EightFoursLeaveNoRoomForAnotherFour()
    {
        var ship = Catalog.ById("drak-cutlass-black")!;
        var packed = CargoCrateFit.EmptyCounts();
        packed[4] = 8;
        Assert.Equal(0, CargoCrateFit.MaxCount(ship, null, 4, offGrid: false, packed));
        Assert.True(CargoCrateFit.MaxCount(ship, null, 1, offGrid: false, packed) > 0);
        Assert.Equal(CargoCrateFit.OffGridCountCap, CargoCrateFit.MaxCount(ship, null, 4, offGrid: true, packed, volumeUsed: 32));
        Assert.Equal(CargoCrateFit.OffGridCountCap, CargoCrateFit.MaxCount(ship, null, 32, offGrid: true, packed, volumeUsed: 32));
    }

    [Fact]
    public void CutlassBlack_SixteenAndTwoEightsLeaveSixTwosNotSeven()
    {
        var ship = Catalog.ById("drak-cutlass-black")!;
        var rear = Assert.Single(ship.Grids, g => g.Id == 1);
        Assert.Equal(1, rear.W);
        Assert.Equal(3, rear.D);
        Assert.Equal(2, rear.H);

        var packed = CargoCrateFit.EmptyCounts();
        packed[16] = 1;
        packed[8] = 2;
        Assert.Equal(6, CargoCrateFit.MaxCount(ship, null, 2, offGrid: false, packed));
        Assert.Equal(0, CargoCrateFit.MaxCount(ship, null, 4, offGrid: false, packed));
    }

    [Fact]
    public void Raft_OneThirtyTwoReducesRemainingThirtyTwos()
    {
        var ship = Catalog.ById("argo-raft")!;
        var empty = CargoCrateFit.MaxCount(ship, null, 32, offGrid: false);
        var packed = CargoCrateFit.EmptyCounts();
        packed[32] = 1;
        var left = CargoCrateFit.MaxCount(ship, null, 32, offGrid: false, packed);
        Assert.True(empty >= 1);
        Assert.Equal(empty - 1, left);
    }

    [Fact]
    public void OffGrid_IgnoresGridOccupancyAndVolume()
    {
        var ship = Catalog.ById("drak-cutlass-black")!;
        var packed = CargoCrateFit.EmptyCounts();
        packed[4] = 8;
        Assert.Equal(
            CargoCrateFit.OffGridCountCap,
            CargoCrateFit.MaxCount(ship, null, 4, offGrid: true, packed, volumeUsed: ship.TotalScu));
        Assert.Equal(
            CargoCrateFit.OffGridCountCap,
            CargoCrateFit.MaxCount(ship, null, 32, offGrid: true, packed, volumeUsed: ship.TotalScu));
    }
}
