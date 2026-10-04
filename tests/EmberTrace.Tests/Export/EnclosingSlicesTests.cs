using EmberTrace.Export;

namespace EmberTrace.Tests.Export;

[TestClass]
public class EnclosingSlicesTests
{
    private const int Track = 1;

    [TestMethod]
    public void Complete_EnclosesItsOwnTrackUntilItEnds()
    {
        var slices = new EnclosingSlices();
        slices.Complete(Track, 20);

        Assert.IsTrue(slices.Encloses(Track, 20));
        Assert.IsFalse(slices.Encloses(Track, 21));
        Assert.IsFalse(slices.Encloses(2, 10));
    }

    [TestMethod]
    public void Complete_TheLatestSliceHidesTheOneUnderIt()
    {
        var slices = new EnclosingSlices();
        slices.Complete(Track, 100);
        slices.Complete(Track, 20);

        Assert.IsTrue(slices.Encloses(Track, 15));
        Assert.IsFalse(slices.Encloses(Track, 50));
    }

    [TestMethod]
    public void Point_HidesTheSliceUnderIt()
    {
        var slices = new EnclosingSlices();
        slices.Complete(Track, 100);
        slices.Point(Track);

        Assert.IsFalse(slices.Encloses(Track, 50));
    }

    [TestMethod]
    public void BeginEnd_EnclosesWhileASliceIsOpenAndForgetsPointsOnEveryEnd()
    {
        var slices = new EnclosingSlices();

        slices.Begin(Track);
        Assert.IsTrue(slices.Encloses(Track, long.MaxValue));

        slices.Point(Track);
        Assert.IsFalse(slices.Encloses(Track, 10));

        slices.Begin(Track);
        slices.Point(Track);
        slices.End(Track);
        Assert.IsTrue(slices.Encloses(Track, 10));

        slices.End(Track);
        Assert.IsFalse(slices.Encloses(Track, 10));
    }

    [TestMethod]
    public void End_WithoutABegin_DoesNotUnbalanceTheTrack()
    {
        var slices = new EnclosingSlices();

        slices.End(Track);
        slices.Begin(Track);
        Assert.IsTrue(slices.Encloses(Track, 10));

        slices.End(Track);
        Assert.IsFalse(slices.Encloses(Track, 10));
    }
}
