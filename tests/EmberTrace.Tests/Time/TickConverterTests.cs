using EmberTrace.Internal.Time;
using EmberTrace.Sessions;

namespace EmberTrace.Tests.Time;

[TestClass]
public class TickConverterTests
{
    [TestMethod]
    [DataRow(4_000L, 2_000L, 500.0, 500_000.0)]
    [DataRow(1_000_000_000L, 3L, 0.000003, 0.003)]
    [DataRow(4_000L, -2_000L, -500.0, -500_000.0)]
    public void ToMsAndToUs_ScaleByTheFrequency(long frequency, long ticks, double expectedMs, double expectedUs)
    {
        var converter = new TickConverter(frequency);

        Assert.AreEqual(expectedMs, converter.ToMs(ticks), 1e-9);
        Assert.AreEqual(expectedUs, converter.ToUs(ticks), 1e-9);
    }

    [TestMethod]
    public void ToUtc_AddsTheElapsedTimeToTheBase()
    {
        var baseUtc = new DateTimeOffset(2024, 6, 1, 15, 0, 0, TimeSpan.FromHours(3));

        var result = new TickConverter(4_000).ToUtc(baseUtc, 2_000);

        Assert.AreEqual(DateTimeKind.Utc, result.Kind);
        Assert.AreEqual(new DateTime(2024, 6, 1, 12, 0, 0, 500, DateTimeKind.Utc), result);
    }

    [TestMethod]
    public void FromSession_UsesTheSessionFrequency()
    {
        var session = TraceSession.FromEvents([], 0, 0, 4_000);

        Assert.AreEqual(500.0, TickConverter.FromSession(session).ToMs(2_000), 1e-9);
    }
}
