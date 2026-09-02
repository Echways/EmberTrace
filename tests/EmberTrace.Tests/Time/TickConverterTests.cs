using EmberTrace.Internal.Time;
using EmberTrace.Sessions;

namespace EmberTrace.Tests.Time;

[TestClass]
public class TickConverterTests
{
    [TestMethod]
    public void ToMs_ConvertsUsingFrequency()
    {
        var converter = new TickConverter(1000);

        Assert.AreEqual(2000.0, converter.ToMs(2000), 1e-9);
    }

    [TestMethod]
    public void ToUs_ConvertsUsingFrequency()
    {
        var converter = new TickConverter(1_000_000);

        Assert.AreEqual(1000.0, converter.ToUs(1000), 1e-9);
    }

    [TestMethod]
    public void ToUtc_AddsElapsedToBase()
    {
        var converter = new TickConverter(1000);
        var baseUtc = new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);

        var result = converter.ToUtc(baseUtc, 500);

        Assert.AreEqual(baseUtc.UtcDateTime.AddMilliseconds(500), result);
    }

    [TestMethod]
    public void FromSession_UsesSessionFrequency()
    {
        var session = TraceSession.FromEvents(Array.Empty<TraceEventRecord>(), 0, 0, 1_000_000);

        var converter = TickConverter.FromSession(session);

        Assert.AreEqual(1000.0, converter.ToUs(1000), 1e-9);
    }
}
