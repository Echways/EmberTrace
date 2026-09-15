using EmberTrace.Metadata;
using EmberTrace.Sessions;

namespace EmberTrace.Tests.Runtime;

[TestClass]
public class RuntimeCounterIdsTests
{
    private static readonly int[] ReservedIds =
    [
        RuntimeCounterIds.GcGen0, RuntimeCounterIds.GcGen1, RuntimeCounterIds.GcGen2,
        RuntimeCounterIds.HeapBytes, RuntimeCounterIds.AllocatedBytes,
        RuntimeCounterIds.ThreadPoolThreads, RuntimeCounterIds.ThreadPoolQueue,
        RuntimeCounterIds.ThreadPoolCompleted, RuntimeCounterIds.Exceptions,
        RuntimeCounterIds.GcPause
    ];

    [TestMethod]
    [DataRow(-1, true)]
    [DataRow(-10, true)]
    [DataRow(0, false)]
    [DataRow(1, false)]
    [DataRow(-11, false)]
    [DataRow(int.MinValue, false)]
    public void IsReserved_CoversExactlyTheCounterRange(int id, bool expected)
    {
        Assert.AreEqual(expected, RuntimeCounterIds.IsReserved(id));
    }

    [TestMethod]
    public void ReservedIds_AreDistinctAndInsideTheReservedRange()
    {
        Assert.HasCount(ReservedIds.Length, ReservedIds.Distinct());
        Assert.IsTrue(ReservedIds.All(RuntimeCounterIds.IsReserved));
    }

    [TestMethod]
    [DataRow("App")]
    [DataRow("")]
    [DataRow("поток")]
    [DataRow("a-very-long-name-that-still-hashes-to-a-positive-id")]
    public void TracerIds_NeverFallIntoTheReservedRange(string name)
    {
        var id = Tracer.Id(name);

        Assert.IsGreaterThan(0, id);
        Assert.IsFalse(RuntimeCounterIds.IsReserved(id));
    }

    [TestMethod]
    public void Metadata_NamesEveryReservedIdUnderTheRuntimeCategory_AndEnumeratesThemAll()
    {
        foreach (var id in ReservedIds)
        {
            Assert.IsTrue(RuntimeCounterMetadata.Instance.TryGet(id, out var meta));
            Assert.AreEqual(id, meta.Id);
            Assert.IsFalse(string.IsNullOrWhiteSpace(meta.Name));
            Assert.AreEqual(RuntimeCounterIds.Category, meta.Category);
        }

        CollectionAssert.AreEquivalent(ReservedIds, RuntimeCounterMetadata.Instance.Select(m => m.Id).ToArray());
    }

    [TestMethod]
    public void SessionOptions_DefaultToCountersOff()
    {
        var options = new SessionOptions();

        Assert.AreEqual(RuntimeCounters.None, options.RuntimeCounters);
        Assert.AreEqual(TimeSpan.FromMilliseconds(50), options.RuntimeCounterInterval);
    }
}
