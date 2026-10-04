using EmberTrace.Internal.Runtime;
using EmberTrace.Sessions;

namespace EmberTrace.Tests.Runtime;

[TestClass]
public class RuntimeCounterCollectorTests
{
    [TestMethod]
    public void SystemRuntimeMetrics_ReportsPlausibleValues()
    {
        using var metrics = new SystemRuntimeMetrics(false);

        Assert.IsGreaterThanOrEqualTo(0, metrics.GcCollectionCount(0));
        Assert.IsGreaterThan(0L, metrics.TotalMemoryBytes);
        Assert.IsGreaterThan(0L, metrics.TotalAllocatedBytes);
        Assert.IsGreaterThan(0, metrics.ThreadPoolThreadCount);
        Assert.IsGreaterThanOrEqualTo(0L, metrics.ThreadPoolPendingWorkItemCount);
        Assert.IsGreaterThanOrEqualTo(0L, metrics.ThreadPoolCompletedWorkItemCount);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void SystemRuntimeMetrics_CountsFirstChanceExceptionsOnlyWhenAsked(bool count)
    {
        using var metrics = new SystemRuntimeMetrics(count);

        try
        {
            throw new InvalidOperationException();
        }
        catch (InvalidOperationException)
        {
        }

        Assert.AreEqual(count, metrics.ExceptionCount > 0);
    }

    [TestMethod]
    public void SystemRuntimeMetrics_StopsCountingAfterDispose()
    {
        var metrics = new SystemRuntimeMetrics(true);
        metrics.Dispose();

        try
        {
            throw new InvalidOperationException();
        }
        catch (InvalidOperationException)
        {
        }

        Assert.AreEqual(0L, metrics.ExceptionCount);
    }

    [TestMethod]
    public void FirstSample_EmitsZeroDeltasAndRealGauges()
    {
        var sink = new RecordingSink();

        new RuntimeCounterCollector(RuntimeCounters.All, BusyMetrics()).Sample(1000, sink);

        Assert.AreEqual(0, sink.Value(RuntimeCounterIds.GcGen0));
        Assert.AreEqual(0, sink.Value(RuntimeCounterIds.GcGen1));
        Assert.AreEqual(0, sink.Value(RuntimeCounterIds.GcGen2));
        Assert.AreEqual(0, sink.Value(RuntimeCounterIds.AllocatedBytes));
        Assert.AreEqual(0, sink.Value(RuntimeCounterIds.ThreadPoolCompleted));
        Assert.AreEqual(0, sink.Value(RuntimeCounterIds.Exceptions));
        Assert.AreEqual(4096, sink.Value(RuntimeCounterIds.HeapBytes));
        Assert.AreEqual(8, sink.Value(RuntimeCounterIds.ThreadPoolThreads));
        Assert.AreEqual(3, sink.Value(RuntimeCounterIds.ThreadPoolQueue));
        Assert.HasCount(9, sink.Counters);
    }

    [TestMethod]
    public void NextSample_EmitsDeltasForCumulativeCountersAndCurrentGauges()
    {
        var metrics = BusyMetrics();
        var collector = new RuntimeCounterCollector(RuntimeCounters.All, metrics);
        collector.Sample(1000, new RecordingSink());

        metrics.Gen0 = 14;
        metrics.Gen2 = 2;
        metrics.TotalMemoryBytes = 8192;
        metrics.TotalAllocatedBytes = 250_000;
        metrics.ThreadPoolThreadCount = 2;
        metrics.ThreadPoolCompletedWorkItemCount = 900;
        metrics.ExceptionCountValue = 9;

        var sink = new RecordingSink();
        collector.Sample(2000, sink);

        Assert.AreEqual(4, sink.Value(RuntimeCounterIds.GcGen0));
        Assert.AreEqual(0, sink.Value(RuntimeCounterIds.GcGen1));
        Assert.AreEqual(1, sink.Value(RuntimeCounterIds.GcGen2));
        Assert.AreEqual(150_000, sink.Value(RuntimeCounterIds.AllocatedBytes));
        Assert.AreEqual(400, sink.Value(RuntimeCounterIds.ThreadPoolCompleted));
        Assert.AreEqual(2, sink.Value(RuntimeCounterIds.Exceptions));
        Assert.AreEqual(8192, sink.Value(RuntimeCounterIds.HeapBytes));
        Assert.AreEqual(2, sink.Value(RuntimeCounterIds.ThreadPoolThreads));
    }

    [TestMethod]
    [DataRow(RuntimeCounters.Gc, new[] { RuntimeCounterIds.GcGen0, RuntimeCounterIds.GcGen1, RuntimeCounterIds.GcGen2 })]
    [DataRow(RuntimeCounters.Memory, new[] { RuntimeCounterIds.HeapBytes, RuntimeCounterIds.AllocatedBytes })]
    [DataRow(RuntimeCounters.ThreadPool,
        new[] { RuntimeCounterIds.ThreadPoolThreads, RuntimeCounterIds.ThreadPoolQueue, RuntimeCounterIds.ThreadPoolCompleted })]
    [DataRow(RuntimeCounters.Exceptions, new[] { RuntimeCounterIds.Exceptions })]
    [DataRow(RuntimeCounters.None, new int[0])]
    public void EnabledGroups_EmitOnlyTheirOwnCounters(RuntimeCounters enabled, int[] expectedIds)
    {
        var sink = new RecordingSink();

        new RuntimeCounterCollector(enabled, BusyMetrics()).Sample(1000, sink);

        CollectionAssert.AreEqual(expectedIds, sink.Counters.Select(c => c.Id).ToArray());
        Assert.IsEmpty(sink.Spans);
    }

    [TestMethod]
    public void GcPause_IsEmittedOncePerGcIndexAndEndsAtTheSampleTimestamp()
    {
        var metrics = new FakeRuntimeMetrics { GcIndex = 4, GcPauseTicks = 250 };
        var collector = new RuntimeCounterCollector(RuntimeCounters.GcPauses, metrics);

        var baseline = new RecordingSink();
        collector.Sample(10_000, baseline);

        metrics.GcIndex = 5;
        var first = new RecordingSink();
        collector.Sample(20_000, first);

        var repeated = new RecordingSink();
        collector.Sample(30_000, repeated);

        metrics.GcIndex = 6;
        metrics.GcPauseTicks = 100;
        var advanced = new RecordingSink();
        collector.Sample(40_000, advanced);

        Assert.IsEmpty(baseline.Spans);
        CollectionAssert.AreEqual(new[] { (RuntimeCounterIds.GcPause, 19_750L, 20_000L) }, first.Spans);
        Assert.IsEmpty(repeated.Spans);
        CollectionAssert.AreEqual(new[] { (RuntimeCounterIds.GcPause, 39_900L, 40_000L) }, advanced.Spans);
        Assert.IsEmpty(first.Counters);
    }

    [TestMethod]
    public void GcPause_LongerThanTheSamplingInterval_StartsAtThePreviousSample()
    {
        var metrics = new FakeRuntimeMetrics { GcIndex = 1, GcPauseTicks = 500 };
        var collector = new RuntimeCounterCollector(RuntimeCounters.GcPauses, metrics);
        collector.Sample(1_000, new RecordingSink());

        metrics.GcIndex = 2;
        var sink = new RecordingSink();
        collector.Sample(1_200, sink);

        CollectionAssert.AreEqual(new[] { (RuntimeCounterIds.GcPause, 1_000L, 1_200L) }, sink.Spans);
    }

    [TestMethod]
    public void EverySample_IsStampedInNonDecreasingOrderWithThePauseFirst()
    {
        var metrics = BusyMetrics();
        metrics.GcIndex = 1;
        metrics.GcPauseTicks = 5_000;
        var collector = new RuntimeCounterCollector(RuntimeCounters.All, metrics);
        var sink = new RecordingSink();

        for (var sample = 1; sample <= 4; sample++)
        {
            metrics.GcIndex++;
            collector.Sample(sample * 1_000, sink);
        }

        CollectionAssert.AreEqual(sink.Timestamps.Order().ToArray(), sink.Timestamps);
        CollectionAssert.AreEqual(
            new[]
            {
                (RuntimeCounterIds.GcPause, 1_000L, 2_000L), (RuntimeCounterIds.GcPause, 2_000L, 3_000L),
                (RuntimeCounterIds.GcPause, 3_000L, 4_000L)
            },
            sink.Spans);
    }

    [TestMethod]
    [DataRow(0L, 4L)]
    [DataRow(-5L, 4L)]
    [DataRow(250L, 3L)]
    [DataRow(250L, 0L)]
    public void GcPause_WithoutAPauseOrANewGc_EmitsNothing(long pauseTicks, long gcIndex)
    {
        var metrics = new FakeRuntimeMetrics { GcIndex = 3, GcPauseTicks = 250 };
        var collector = new RuntimeCounterCollector(RuntimeCounters.GcPauses, metrics);
        collector.Sample(10_000, new RecordingSink());

        metrics.GcIndex = gcIndex;
        metrics.GcPauseTicks = pauseTicks;
        var sink = new RecordingSink();
        collector.Sample(20_000, sink);

        Assert.IsEmpty(sink.Spans);
    }

    [TestMethod]
    public void CumulativeCounterGoingBackwards_ClampsToZero()
    {
        var metrics = new FakeRuntimeMetrics { TotalAllocatedBytes = 1000 };
        var collector = new RuntimeCounterCollector(RuntimeCounters.Memory, metrics);
        collector.Sample(1000, new RecordingSink());

        metrics.TotalAllocatedBytes = 400;
        var sink = new RecordingSink();
        collector.Sample(2000, sink);

        Assert.AreEqual(0, sink.Value(RuntimeCounterIds.AllocatedBytes));
    }

    private static FakeRuntimeMetrics BusyMetrics()
    {
        return new FakeRuntimeMetrics
        {
            Gen0 = 10,
            Gen1 = 5,
            Gen2 = 1,
            TotalMemoryBytes = 4096,
            TotalAllocatedBytes = 100_000,
            ThreadPoolThreadCount = 8,
            ThreadPoolPendingWorkItemCount = 3,
            ThreadPoolCompletedWorkItemCount = 500,
            ExceptionCountValue = 7
        };
    }

    private sealed class FakeRuntimeMetrics : IRuntimeMetrics
    {
        public int Gen0 { get; set; }
        public int Gen1 { get; set; }
        public int Gen2 { get; set; }
        public long ExceptionCountValue { get; set; }
        public long GcIndex { get; set; }
        public long GcPauseTicks { get; set; }
        public long TotalMemoryBytes { get; set; }
        public long TotalAllocatedBytes { get; set; }
        public int ThreadPoolThreadCount { get; set; }
        public long ThreadPoolPendingWorkItemCount { get; set; }
        public long ThreadPoolCompletedWorkItemCount { get; set; }

        public long ExceptionCount => ExceptionCountValue;

        public int GcCollectionCount(int generation)
        {
            return generation switch { 0 => Gen0, 1 => Gen1, _ => Gen2 };
        }

        public bool TryGetLatestGcPause(out long index, out long pauseTicks)
        {
            index = GcIndex;
            pauseTicks = GcPauseTicks;
            return GcIndex > 0;
        }
    }

    private sealed class RecordingSink : IRuntimeCounterSink
    {
        public List<(int Id, long Value)> Counters { get; } = [];
        public List<(int Id, long Start, long End)> Spans { get; } = [];
        public List<long> Timestamps { get; } = [];

        public void Counter(int id, long value, long timestamp)
        {
            Counters.Add((id, value));
            Timestamps.Add(timestamp);
        }

        public void Span(int id, long startTimestamp, long endTimestamp)
        {
            Spans.Add((id, startTimestamp, endTimestamp));
            Timestamps.Add(startTimestamp);
            Timestamps.Add(endTimestamp);
        }

        public long Value(int id)
        {
            return Counters.Single(c => c.Id == id).Value;
        }
    }
}
