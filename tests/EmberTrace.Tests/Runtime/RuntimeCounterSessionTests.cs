using System.Diagnostics;
using System.Text;
using EmberTrace.Internal.Runtime;
using EmberTrace.Sessions;

namespace EmberTrace.Tests.Runtime;

[TestClass]
[DoNotParallelize]
public class RuntimeCounterSessionTests
{
    private const int ScopeId = 1000;

    private static readonly int[] GcIds = [RuntimeCounterIds.GcGen0, RuntimeCounterIds.GcGen1, RuntimeCounterIds.GcGen2];

    [TestMethod]
    public void Session_WithoutRuntimeCounters_EmitsNoReservedIds()
    {
        using var tracing = new TracingSession();
        tracing.Start(new SessionOptions { ChunkCapacity = 4096 });
        using (tracing.Scope(ScopeId))
        {
        }

        var session = tracing.Stop();

        Assert.IsFalse(session.Events().Any(e => RuntimeCounterIds.IsReserved(e.Id)));
        Assert.IsFalse(session.ThreadNames.Values.Contains(RuntimeCounterSampler.ThreadName));
    }

    [TestMethod]
    public void Session_WithRuntimeCounters_EmitsNamedCountersOnTheSamplerTrack()
    {
        var session = RecordUntil(
            Options(RuntimeCounters.Gc | RuntimeCounters.Memory | RuntimeCounters.ThreadPool),
            events => events.Count(e => e.Id == RuntimeCounterIds.HeapBytes) >= 2,
            tracing =>
            {
                using (tracing.Scope(ScopeId))
                {
                }
            });

        var events = session.Events();
        var counters = events.Where(e => RuntimeCounterIds.IsReserved(e.Id)).ToList();
        var expectedIds = new[]
        {
            RuntimeCounterIds.GcGen0, RuntimeCounterIds.GcGen1, RuntimeCounterIds.GcGen2,
            RuntimeCounterIds.HeapBytes, RuntimeCounterIds.AllocatedBytes,
            RuntimeCounterIds.ThreadPoolThreads, RuntimeCounterIds.ThreadPoolQueue,
            RuntimeCounterIds.ThreadPoolCompleted
        };

        CollectionAssert.AreEquivalent(expectedIds, counters.Select(e => e.Id).Distinct().ToArray());
        Assert.IsTrue(counters.All(e => e.Kind == TraceEventKind.Counter));
        Assert.IsTrue(counters.Where(e => e.Id == RuntimeCounterIds.HeapBytes).All(e => e.Value > 0));
        Assert.IsTrue(counters.All(e => e.Value >= 0));

        var counterTrack = counters.Select(e => e.TrackId).Distinct().Single();
        Assert.AreNotEqual(events.First(e => e.Id == ScopeId).TrackId, counterTrack);
        Assert.AreEqual(RuntimeCounterSampler.ThreadName, session.ThreadNames[counters[0].ThreadId]);

        Assert.IsTrue(session.Metadata.TryGet(RuntimeCounterIds.HeapBytes, out var meta));
        Assert.AreEqual("Heap bytes", meta.Name);
        Assert.AreEqual(RuntimeCounterIds.Category, meta.Category);
    }

    [TestMethod]
    public void Session_WithRuntimeCounters_StopsWithWholeSamplesAndCanRestart()
    {
        using var tracing = new TracingSession();

        for (var i = 0; i < 3; i++)
        {
            tracing.Start(Options(RuntimeCounters.Gc));
            WaitFor(tracing, events => events.Count > 0);

            var session = tracing.Stop();
            var ids = session.Events().Select(e => e.Id).ToList();

            Assert.IsFalse(tracing.IsRunning);
            Assert.IsNotEmpty(ids);
            Assert.AreEqual(0, ids.Count % GcIds.Length);
            Assert.IsTrue(ids.All(GcIds.Contains));
        }
    }

    [TestMethod]
    public void Session_WithRuntimeCounters_DoesNotLeakSamplerThreads()
    {
        RunShortCounterSession();
        var baseline = Process.GetCurrentProcess().Threads.Count;

        for (var i = 0; i < 10; i++)
            RunShortCounterSession();

        Assert.IsLessThanOrEqualTo(baseline + 5, Process.GetCurrentProcess().Threads.Count);
    }

    [TestMethod]
    public void Session_WithRuntimeCounters_IgnoresTheCategoryAllowlist()
    {
        var session = RecordUntil(
            Options(RuntimeCounters.Memory, [Tracer.CategoryId("Nothing")]),
            events => events.Any(e => e.Id == RuntimeCounterIds.HeapBytes),
            tracing => tracing.Instant(ScopeId));

        Assert.IsFalse(session.Events().Any(e => e.Id == ScopeId));
    }

    [TestMethod]
    public void GcPauses_AppearAsBalancedScopesWithPositiveDuration()
    {
        var session = RecordUntil(
            Options(RuntimeCounters.GcPauses),
            events =>
            {
                GC.Collect(2, GCCollectionMode.Forced, true);
                return events.Count(e => e.Id == RuntimeCounterIds.GcPause) >= 2;
            });

        var pauses = session.SortedEvents().Where(e => e.Id == RuntimeCounterIds.GcPause).ToList();

        Assert.AreEqual(0, pauses.Count % 2);
        for (var i = 0; i < pauses.Count; i += 2)
        {
            Assert.AreEqual(TraceEventKind.Begin, pauses[i].Kind);
            Assert.AreEqual(TraceEventKind.End, pauses[i + 1].Kind);
            Assert.IsGreaterThan(pauses[i].Timestamp, pauses[i + 1].Timestamp);
        }

        var stats = session.Analyze();
        Assert.AreEqual(0, stats.UnmatchedBeginCount + stats.UnmatchedEndCount);
        Assert.IsGreaterThan(0.0, stats.ByTotalTimeDesc.Single(r => r.Id == RuntimeCounterIds.GcPause).TotalMs);
    }

    [TestMethod]
    public void RuntimeCounters_ExportToChromeWithResolvedNames()
    {
        var session = RecordUntil(
            Options(RuntimeCounters.Memory),
            events => events.Any(e => e.Id == RuntimeCounterIds.HeapBytes));

        using var stream = new MemoryStream();
        TraceExport.WriteChromeComplete(session, stream);
        var json = Encoding.UTF8.GetString(stream.ToArray());

        StringAssert.Contains(json, "\"name\":\"Heap bytes\",\"cat\":\"Runtime\",\"ph\":\"C\"");
    }

    private static SessionOptions Options(RuntimeCounters counters, int[]? enabledCategoryIds = null)
    {
        return new SessionOptions
        {
            ChunkCapacity = 4096,
            RuntimeCounters = counters,
            RuntimeCounterInterval = TimeSpan.FromMilliseconds(5),
            EnabledCategoryIds = enabledCategoryIds
        };
    }

    private static TraceSession RecordUntil(
        SessionOptions options,
        Func<List<TraceEventRecord>, bool> done,
        Action<TracingSession>? body = null)
    {
        using var tracing = new TracingSession();
        tracing.Start(options);

        body?.Invoke(tracing);
        WaitFor(tracing, done);
        return tracing.Stop();
    }

    private static void WaitFor(TracingSession tracing, Func<List<TraceEventRecord>, bool> done)
    {
        var elapsed = Stopwatch.StartNew();

        while (!done(tracing.Snapshot().Events()))
        {
            Assert.IsLessThan(TimeSpan.FromSeconds(10), elapsed.Elapsed);
            Thread.Sleep(5);
        }
    }

    private static void RunShortCounterSession()
    {
        using var tracing = new TracingSession();
        tracing.Start(Options(RuntimeCounters.All));
        Thread.Sleep(20);
        tracing.Stop();
    }
}
