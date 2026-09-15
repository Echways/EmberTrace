using EmberTrace.Extensions.Hosting.Recording;

namespace EmberTrace.Extensions.Hosting.Tests.Recording;

[TestClass]
public sealed class DumpFileNameTests
{
    private static readonly DateTimeOffset Moment = new(2026, 9, 15, 13, 4, 5, 678, TimeSpan.FromHours(3));

    [TestMethod]
    public void WithKind_IncludesKindAndUtcStampWithMilliseconds()
    {
        Assert.AreEqual("embertrace-slow-20260915-100405-678.ember",
            DumpFileName.Create("embertrace", "slow", Moment, ".ember"));
    }

    [TestMethod]
    public void WithoutKind_OmitsTheKindSegment()
    {
        Assert.AreEqual("app-20260915-100405-678.json", DumpFileName.Create("app", null, Moment, ".json"));
    }
}
