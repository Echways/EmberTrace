using EmberTrace.Export;

namespace EmberTrace.Tests.Export;

#pragma warning disable CS0618

[TestClass]
public class TraceTimeTests
{
    [TestMethod]
    public void ToUs_SubMicrosecondTicks_KeepFraction()
    {
        Assert.AreEqual(0.1, TraceTime.ToUs(1, 10_000_000), 1e-12);
        Assert.AreEqual(0.001, TraceTime.ToUs(1, 1_000_000_000), 1e-12);
    }

    [TestMethod]
    public void ToUs_WholeMicroseconds_AreExact()
    {
        Assert.AreEqual(1.0, TraceTime.ToUs(10, 10_000_000), 1e-12);
        Assert.AreEqual(1_500.0, TraceTime.ToUs(15_000, 10_000_000), 1e-12);
    }
}

#pragma warning restore CS0618
