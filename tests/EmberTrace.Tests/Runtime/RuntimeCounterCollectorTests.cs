using EmberTrace.Internal.Runtime;
using EmberTrace.Sessions;

namespace EmberTrace.Tests.Runtime;

[TestClass]
public class RuntimeCounterCollectorTests
{
    private static readonly int[] ReservedIds =
    {
        RuntimeCounterIds.GcGen0, RuntimeCounterIds.GcGen1, RuntimeCounterIds.GcGen2,
        RuntimeCounterIds.HeapBytes, RuntimeCounterIds.AllocatedBytes,
        RuntimeCounterIds.ThreadPoolThreads, RuntimeCounterIds.ThreadPoolQueue,
        RuntimeCounterIds.ThreadPoolCompleted, RuntimeCounterIds.Exceptions,
        RuntimeCounterIds.GcPause
    };

    [TestMethod]
    public void ReservedIds_AreAllNegativeAndDistinct()
    {
        foreach (var id in ReservedIds)
        {
            Assert.IsTrue(id < 0, $"reserved id {id} must be negative");
            Assert.IsTrue(RuntimeCounterIds.IsReserved(id));
        }

        Assert.AreEqual(ReservedIds.Length, ReservedIds.Distinct().Count());
    }

    [TestMethod]
    public void ReservedRange_CannotCollideWithTracerIds()
    {
        foreach (var name in new[] { "App", "Worker", "", "a", "поток", new string('x', 500) })
        {
            var id = Tracer.Id(name);

            Assert.IsTrue(id > 0, $"Tracer.Id(\"{name}\") returned {id}");
            Assert.IsFalse(RuntimeCounterIds.IsReserved(id));
        }
    }

    [TestMethod]
    public void SessionOptions_DefaultToCountersOff()
    {
        var options = new SessionOptions();

        Assert.AreEqual(RuntimeCounters.None, options.RuntimeCounters);
        Assert.AreEqual(TimeSpan.FromMilliseconds(50), options.RuntimeCounterInterval);
    }

    [TestMethod]
    public void SystemRuntimeMetrics_ReportsPlausibleValues()
    {
        using var metrics = new SystemRuntimeMetrics(false);

        Assert.IsTrue(metrics.GcCollectionCount(0) >= 0);
        Assert.IsTrue(metrics.TotalMemoryBytes > 0);
        Assert.IsTrue(metrics.TotalAllocatedBytes > 0);
        Assert.IsTrue(metrics.ThreadPoolThreadCount > 0);
        Assert.IsTrue(metrics.ThreadPoolPendingWorkItemCount >= 0);
        Assert.IsTrue(metrics.ThreadPoolCompletedWorkItemCount >= 0);
    }

    [TestMethod]
    public void SystemRuntimeMetrics_CountsFirstChanceExceptionsOnlyWhenAsked()
    {
        using var counting = new SystemRuntimeMetrics(true);
        var before = counting.ExceptionCount;

        try
        {
            throw new InvalidOperationException("counted");
        }
        catch (InvalidOperationException)
        {
        }

        Assert.IsTrue(counting.ExceptionCount > before);
    }

    [TestMethod]
    public void SystemRuntimeMetrics_WhenNotCounting_StaysAtZero()
    {
        using var quiet = new SystemRuntimeMetrics(false);

        try
        {
            throw new InvalidOperationException("ignored");
        }
        catch (InvalidOperationException)
        {
        }

        Assert.AreEqual(0, quiet.ExceptionCount);
    }

    [TestMethod]
    public void FirstSample_EmitsZeroDeltasAndRealGauges()
    {
        var metrics = new FakeRuntimeMetrics
        {
            Gen0 = 10, Gen1 = 5, Gen2 = 1,
            TotalMemoryBytes = 4096,
            TotalAllocatedBytes = 100_000,
            ThreadPoolThreadCount = 8,
            ThreadPoolPendingWorkItemCount = 3,
            ThreadPoolCompletedWorkItemCount = 500,
            ExceptionCountValue = 7
        };

        var sink = new RecordingSink();
        new RuntimeCounterCollector(RuntimeCounters.All, metrics).Sample(1000, sink);

        Assert.AreEqual(0, sink.CounterValue(RuntimeCounterIds.GcGen0));
        Assert.AreEqual(0, sink.CounterValue(RuntimeCounterIds.AllocatedBytes));
        Assert.AreEqual(0, sink.CounterValue(RuntimeCounterIds.ThreadPoolCompleted));
        Assert.AreEqual(0, sink.CounterValue(RuntimeCounterIds.Exceptions));

        Assert.AreEqual(4096, sink.CounterValue(RuntimeCounterIds.HeapBytes));
        Assert.AreEqual(8, sink.CounterValue(RuntimeCounterIds.ThreadPoolThreads));
        Assert.AreEqual(3, sink.CounterValue(RuntimeCounterIds.ThreadPoolQueue));
    }

    [TestMethod]
    public void SecondSample_EmitsDeltasForCumulativeCounters()
    {
        var metrics = new FakeRuntimeMetrics
        {
            Gen0 = 10, Gen1 = 5, Gen2 = 1,
            TotalMemoryBytes = 4096,
            TotalAllocatedBytes = 100_000,
            ThreadPoolThreadCount = 8,
            ThreadPoolPendingWorkItemCount = 3,
            ThreadPoolCompletedWorkItemCount = 500,
            ExceptionCountValue = 7
        };

        var collector = new RuntimeCounterCollector(RuntimeCounters.All, metrics);
        collector.Sample(1000, new RecordingSink());

        metrics.Gen0 = 14;
        metrics.Gen2 = 2;
        metrics.TotalMemoryBytes = 8192;
        metrics.TotalAllocatedBytes = 250_000;
        metrics.ThreadPoolCompletedWorkItemCount = 900;
        metrics.ExceptionCountValue = 9;

        var sink = new RecordingSink();
        collector.Sample(2000, sink);

        Assert.AreEqual(4, sink.CounterValue(RuntimeCounterIds.GcGen0));
        Assert.AreEqual(0, sink.CounterValue(RuntimeCounterIds.GcGen1));
        Assert.AreEqual(1, sink.CounterValue(RuntimeCounterIds.GcGen2));
        Assert.AreEqual(150_000, sink.CounterValue(RuntimeCounterIds.AllocatedBytes));
        Assert.AreEqual(400, sink.CounterValue(RuntimeCounterIds.ThreadPoolCompleted));
        Assert.AreEqual(2, sink.CounterValue(RuntimeCounterIds.Exceptions));
        Assert.AreEqual(8192, sink.CounterValue(RuntimeCounterIds.HeapBytes));
    }

    [TestMethod]
    public void DisabledGroups_EmitNothing()
    {
        var metrics = new FakeRuntimeMetrics { Gen0 = 3, TotalMemoryBytes = 4096, ThreadPoolThreadCount = 8 };

        var sink = new RecordingSink();
        new RuntimeCounterCollector(RuntimeCounters.Gc, metrics).Sample(1000, sink);

        Assert.IsTrue(sink.HasCounter(RuntimeCounterIds.GcGen0));
        Assert.IsFalse(sink.HasCounter(RuntimeCounterIds.HeapBytes));
        Assert.IsFalse(sink.HasCounter(RuntimeCounterIds.ThreadPoolThreads));
        Assert.IsFalse(sink.HasCounter(RuntimeCounterIds.Exceptions));
    }

    [TestMethod]
    public void GcPause_EmittedOnceWhenTheGcIndexAdvances()
    {
        var metrics = new FakeRuntimeMetrics { GcIndex = 4, GcPauseTicks = 250 };
        var collector = new RuntimeCounterCollector(RuntimeCounters.GcPauses, metrics);

        var first = new RecordingSink();
        collector.Sample(10_000, first);

        Assert.HasCount(1, first.Spans);
        Assert.AreEqual(RuntimeCounterIds.GcPause, first.Spans[0].Id);
        Assert.AreEqual(9_750, first.Spans[0].Start);
        Assert.AreEqual(10_000, first.Spans[0].End);

        var second = new RecordingSink();
        collector.Sample(20_000, second);

        Assert.IsEmpty(second.Spans);

        metrics.GcIndex = 5;
        metrics.GcPauseTicks = 100;

        var third = new RecordingSink();
        collector.Sample(30_000, third);

        Assert.HasCount(1, third.Spans);
        Assert.AreEqual(29_900, third.Spans[0].Start);
    }

    [TestMethod]
    public void GcPause_WithZeroDuration_IsSkipped()
    {
        var metrics = new FakeRuntimeMetrics { GcIndex = 4, GcPauseTicks = 0 };

        var sink = new RecordingSink();
        new RuntimeCounterCollector(RuntimeCounters.GcPauses, metrics).Sample(10_000, sink);

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

        Assert.AreEqual(0, sink.CounterValue(RuntimeCounterIds.AllocatedBytes));
    }
}

internal sealed class FakeRuntimeMetrics : IRuntimeMetrics
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

internal sealed class RecordingSink : IRuntimeCounterSink
{
    public List<(int Id, long Value)> Counters { get; } = new();
    public List<(int Id, long Start, long End)> Spans { get; } = new();

    public void Counter(int id, long value)
    {
        Counters.Add((id, value));
    }

    public void Span(int id, long startTimestamp, long endTimestamp)
    {
        Spans.Add((id, startTimestamp, endTimestamp));
    }

    public bool HasCounter(int id)
    {
        return Counters.Any(c => c.Id == id);
    }

    public long CounterValue(int id)
    {
        var matches = Counters.Where(c => c.Id == id).ToList();
        Assert.HasCount(1, matches, $"expected exactly one sample for id {id}");
        return matches[0].Value;
    }
}
