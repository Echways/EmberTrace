using EmberTrace.Analysis.Stats;

namespace EmberTrace.Tests.Analysis;

[TestClass]
public class SampleBufferTests
{
    [TestMethod]
    public void Detach_ReturnsExactlyTheAddedSamplesInOrder()
    {
        var buffer = new SampleBuffer(100);
        for (var i = 0; i < 37; i++)
            buffer.Add(i);

        var samples = buffer.Detach();

        Assert.IsTrue(buffer.IsExact);
        CollectionAssert.AreEqual(Enumerable.Range(0, 37).Select(i => (long)i).ToArray(), samples.ToArray());
    }

    [TestMethod]
    public void Add_PastLimit_DropsSamplesAndStopsBeingExact()
    {
        var buffer = new SampleBuffer(3);
        for (var i = 0; i < 4; i++)
            buffer.Add(i);

        Assert.IsFalse(buffer.IsExact);
        Assert.IsTrue(buffer.Span.IsEmpty);
        Assert.IsTrue(buffer.Detach().IsEmpty);
    }

    [TestMethod]
    public void Add_ExactlyAtLimit_StaysExact()
    {
        var buffer = new SampleBuffer(3);
        for (var i = 0; i < 3; i++)
            buffer.Add(i);

        Assert.IsTrue(buffer.IsExact);
        Assert.AreEqual(3, buffer.Span.Length);
    }

    [TestMethod]
    public void ZeroLimit_IsNeverExact()
    {
        var buffer = new SampleBuffer(0);
        buffer.Add(1);

        Assert.IsFalse(buffer.IsExact);
    }

    [TestMethod]
    public void SortedSamples_SortsACopyAndLeavesTheSourceUntouched()
    {
        long[] source = [5, 1, 4, 2];

        using var sorted = SortedSamples.From(source);

        CollectionAssert.AreEqual(new long[] { 1, 2, 4, 5 }, sorted.Values.ToArray());
        CollectionAssert.AreEqual(new long[] { 5, 1, 4, 2 }, source);
    }
}