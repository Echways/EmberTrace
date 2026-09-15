using EmberTrace.Extensions.Hosting.Recording;

namespace EmberTrace.Extensions.Hosting.Tests.Recording;

[TestClass]
public sealed class DumpFileNameTests
{
    [TestMethod]
    [DataRow("embertrace", "slow", ".ember", "embertrace-slow-20260915-100405-678.ember")]
    [DataRow("app", null, ".json", "app-20260915-100405-678.json")]
    public void Create_StampsTheUtcMomentWithMilliseconds(string prefix, string? kind, string extension, string expected)
    {
        var moment = new DateTimeOffset(2026, 9, 15, 13, 4, 5, 678, TimeSpan.FromHours(3));

        Assert.AreEqual(expected, DumpFileName.Create(prefix, kind, moment, extension));
    }
}
