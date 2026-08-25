using System.Diagnostics;
using System.Text;
using EmberTrace.Metadata;
using EmberTrace.Sessions;

namespace EmberTrace.Tests.Runtime;

[TestClass]
[DoNotParallelize]
public class RuntimeCounterSessionTests
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
    public void RuntimeCounterMetadata_NamesEveryReservedId()
    {
        foreach (var id in ReservedIds)
        {
            Assert.IsTrue(RuntimeCounterMetadata.Instance.TryGet(id, out var meta), $"id {id} has no metadata");
            Assert.IsFalse(string.IsNullOrWhiteSpace(meta.Name));
            Assert.AreEqual(RuntimeCounterIds.Category, meta.Category);
        }
    }

    [TestMethod]
    public void RuntimeCounterMetadata_Enumerates_SoCompositesCanFlattenIt()
    {
        Assert.HasCount(ReservedIds.Length, RuntimeCounterMetadata.Instance.ToList());
    }

    [TestMethod]
    public void Session_WithoutRuntimeCounters_EmitsNoReservedIds()
    {
        Tracer.Start(new SessionOptions { ChunkCapacity = 4096 });

        using (Tracer.Scope(1000))
        {
        }

        var session = Tracer.Stop();

        Assert.IsFalse(
            Events(session).Any(e => RuntimeCounterIds.IsReserved(e.Id)),
            "runtime counters must be opt-in");
    }

    [TestMethod]
    public void Session_WithRuntimeCounters_EmitsCountersOnTheirOwnTrack()
    {
        Tracer.Start(new SessionOptions
        {
            ChunkCapacity = 4096,
            RuntimeCounters = RuntimeCounters.Gc | RuntimeCounters.Memory | RuntimeCounters.ThreadPool,
            RuntimeCounterInterval = TimeSpan.FromMilliseconds(5)
        });

        using (Tracer.Scope(1000))
        {
            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (DateTime.UtcNow < deadline)
                Thread.Sleep(20);
        }

        var session = Tracer.Stop();
        var events = Events(session);
        var counters = events.Where(e => RuntimeCounterIds.IsReserved(e.Id)).ToList();

        Assert.IsNotEmpty(counters);
        Assert.IsTrue(counters.All(e => e.Kind == TraceEventKind.Counter));
        Assert.IsTrue(counters.Any(e => e.Id == RuntimeCounterIds.HeapBytes));
        Assert.IsTrue(counters.Any(e => e.Id == RuntimeCounterIds.ThreadPoolThreads));
        Assert.IsTrue(
            counters.Where(e => e.Id == RuntimeCounterIds.HeapBytes).All(e => e.Value > 0),
            "heap size is a gauge and must be positive");

        var scopeTrack = events.First(e => e.Id == 1000).TrackId;
        var counterTracks = counters.Select(e => e.TrackId).Distinct().ToList();

        Assert.HasCount(1, counterTracks, "all counters come from the single sampler thread");
        Assert.AreNotEqual(scopeTrack, counterTracks[0], "the sampler must occupy its own track");
        Assert.IsTrue(session.ThreadNames.Values.Any(n => n == "EmberTrace.Runtime"));
    }

    [TestMethod]
    public void Session_WithRuntimeCounters_StopIsCleanAndRepeatable()
    {
        for (var i = 0; i < 3; i++)
        {
            Tracer.Start(new SessionOptions
            {
                ChunkCapacity = 1024,
                RuntimeCounters = RuntimeCounters.Gc,
                RuntimeCounterInterval = TimeSpan.FromMilliseconds(5)
            });

            Thread.Sleep(30);
            var session = Tracer.Stop();

            Assert.IsFalse(Tracer.IsRunning);
            Assert.IsTrue(session.EventCount >= 0);
        }
    }

    [TestMethod]
    public void Session_WithRuntimeCounters_DoesNotLeakSamplerThreads()
    {
        RunShortCounterSession();
        var baseline = Process.GetCurrentProcess().Threads.Count;

        for (var i = 0; i < 10; i++)
            RunShortCounterSession();

        var after = Process.GetCurrentProcess().Threads.Count;

        Assert.IsTrue(after <= baseline + 5, $"thread count grew from {baseline} to {after}");
    }

    [TestMethod]
    public void Session_WithRuntimeCounters_MetadataResolvesCounterNames()
    {
        Tracer.Start(new SessionOptions
        {
            ChunkCapacity = 1024,
            RuntimeCounters = RuntimeCounters.Memory,
            RuntimeCounterInterval = TimeSpan.FromMilliseconds(5)
        });

        Thread.Sleep(50);
        var session = Tracer.Stop();

        Assert.IsTrue(session.Metadata.TryGet(RuntimeCounterIds.HeapBytes, out var meta));
        Assert.AreEqual("Heap bytes", meta.Name);
    }

    [TestMethod]
    public void Session_WithRuntimeCounters_IgnoresCategoryAllowlist()
    {
        Tracer.Start(new SessionOptions
        {
            ChunkCapacity = 1024,
            EnabledCategoryIds = new[] { Tracer.CategoryId("Nothing") },
            RuntimeCounters = RuntimeCounters.Memory,
            RuntimeCounterInterval = TimeSpan.FromMilliseconds(5)
        });

        Thread.Sleep(50);
        var session = Tracer.Stop();

        Assert.IsTrue(
            Events(session).Any(e => e.Id == RuntimeCounterIds.HeapBytes),
            "a category allowlist must not discard counters the caller asked for");
    }

    [TestMethod]
    public void GcPauses_AppearAsMatchedScopePairs()
    {
        Tracer.Start(new SessionOptions
        {
            ChunkCapacity = 8192,
            RuntimeCounters = RuntimeCounters.GcPauses,
            RuntimeCounterInterval = TimeSpan.FromMilliseconds(5)
        });

        for (var i = 0; i < 5; i++)
        {
            var garbage = new byte[1 << 20];
            GC.KeepAlive(garbage);
            GC.Collect(2, GCCollectionMode.Forced, true);
            Thread.Sleep(30);
        }

        var session = Tracer.Stop();

        var pauses = SortedEvents(session).Where(e => e.Id == RuntimeCounterIds.GcPause).ToList();

        Assert.IsTrue(pauses.Count >= 2, $"expected at least one begin/end pair, saw {pauses.Count} events");
        Assert.AreEqual(0, pauses.Count % 2, "pause spans must be balanced");

        for (var i = 0; i < pauses.Count; i += 2)
        {
            Assert.AreEqual(TraceEventKind.Begin, pauses[i].Kind);
            Assert.AreEqual(TraceEventKind.End, pauses[i + 1].Kind);
            Assert.IsTrue(pauses[i + 1].Timestamp > pauses[i].Timestamp, "a pause must have positive duration");
        }

        var pauseStats = session.Analyze().ByTotalTimeDesc.SingleOrDefault(r => r.Id == RuntimeCounterIds.GcPause);

        Assert.IsNotNull(pauseStats);
        Assert.IsTrue(pauseStats.TotalMs > 0);
    }

    [TestMethod]
    public void GcPauses_ExportToChromeWithoutError()
    {
        Tracer.Start(new SessionOptions
        {
            ChunkCapacity = 8192,
            RuntimeCounters = RuntimeCounters.All,
            RuntimeCounterInterval = TimeSpan.FromMilliseconds(5)
        });

        using (Tracer.Scope(1000))
        {
            GC.Collect(2, GCCollectionMode.Forced, true);
            Thread.Sleep(60);
        }

        var session = Tracer.Stop();

        using var ms = new MemoryStream();
        TraceExport.WriteChromeComplete(session, ms, session.Metadata);

        var json = Encoding.UTF8.GetString(ms.ToArray());

        StringAssert.Contains(json, "Heap bytes");
    }

    private static void RunShortCounterSession()
    {
        Tracer.Start(new SessionOptions
        {
            ChunkCapacity = 1024,
            RuntimeCounters = RuntimeCounters.All,
            RuntimeCounterInterval = TimeSpan.FromMilliseconds(5)
        });

        Thread.Sleep(20);
        Tracer.Stop();
    }

    private static List<TraceEventRecord> Events(TraceSession session)
    {
        var events = new List<TraceEventRecord>();
        foreach (var e in session.EnumerateEvents())
            events.Add(e);

        return events;
    }

    private static List<TraceEventRecord> SortedEvents(TraceSession session)
    {
        var events = new List<TraceEventRecord>();
        foreach (var e in session.EnumerateEventsSorted())
            events.Add(e);

        return events;
    }
}
