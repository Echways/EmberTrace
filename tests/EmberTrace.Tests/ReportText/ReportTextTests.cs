using System.Globalization;
using EmberTrace.Analysis.Model;
using EmberTrace.Metadata;
using EmberTrace.Sessions;

namespace EmberTrace.Tests.ReportText;

[TestClass]
public class ReportTextTests
{
    [TestMethod]
    public void Report_IncludesCountersAndCategories()
    {
        const int ioId = 9001;
        const int cpuId = 9002;

        var ts = new TracingSession();
        ts.Start(new SessionOptions { ChunkCapacity = 64 });
        TraceSession session;
        try
        {
            using (ts.Scope(ioId))
            {
            }

            using (ts.Scope(cpuId))
            {
            }
        }
        finally
        {
            session = ts.Stop();
        }

        var trace = session.Process();
        var meta = new TestMetaProvider();

        var report = TraceText.Write(trace, meta);

        Assert.Contains("Events: " + trace.TotalEventCount, report);
        Assert.Contains("ScopeEvents: " + trace.ScopeEventCount, report);
        Assert.Contains("DroppedEvents:", report);
        Assert.Contains("DroppedChunks:", report);
        Assert.Contains("SampledOut:", report);
        Assert.Contains("UnmatchedBegin:", report);
        Assert.Contains("UnmatchedEnd:", report);
        Assert.Contains("MismatchedEnd:", report);
        Assert.Contains("Categories (by inclusive)", report);
        Assert.Contains("IO", report);
        Assert.Contains("CPU", report);

        var filtered = TraceText.Write(trace, meta, categoryFilter: "IO");
        Assert.Contains("IO", filtered);
        Assert.DoesNotContain("CPU", filtered);
    }

    [TestMethod]
    public void Write_WithoutPercentiles_OmitsTheColumns()
    {
        var trace = BuildUniformTrace();

        var report = TraceText.Write(trace);

        Assert.DoesNotContain("p95 ms", report);
    }

    [TestMethod]
    public void Write_WithPercentiles_AddsTheColumns()
    {
        var trace = BuildUniformTrace();

        var report = TraceText.Write(trace, includePercentiles: true);

        Assert.Contains("p50 ms", report);
        Assert.Contains("p95 ms", report);
        Assert.Contains("p99 ms", report);
    }

    private static ProcessedTrace BuildUniformTrace()
    {
        var events = new List<TraceEventRecord>();
        long ts = 0;
        long sequence = 0;

        for (var i = 0; i < 20; i++)
        {
            events.Add(new TraceEventRecord(1, 1, ts, TraceEventKind.Begin, 0, 0, ++sequence, 1));
            ts += 5_000;
            events.Add(new TraceEventRecord(1, 1, ts, TraceEventKind.End, 0, 0, ++sequence, 1));
            ts += 1_000;
        }

        return TraceSession.FromEvents(events, 0, ts, 1_000_000).Process();
    }

    private sealed class TestMetaProvider : ITraceMetadataProvider
    {
        public bool TryGet(int id, out TraceMeta metadata)
        {
            if (id == 9001)
            {
                metadata = new TraceMeta(id, "Disk", "IO");
                return true;
            }

            if (id == 9002)
            {
                metadata = new TraceMeta(id, "Cpu", "CPU");
                return true;
            }

            metadata = default;
            return false;
        }
    }

    [TestMethod]
    public void Write_CategoryFilter_AppliesBeforeTheTopLimit()
    {
        var events = new List<TraceEventRecord>();
        long ts = 0;
        long sequence = 0;

        for (var id = 1; id <= 11; id++)
        {
            events.Add(new TraceEventRecord(id, 1, ts, TraceEventKind.Begin, 0, 0, ++sequence, 1));
            ts += id == 11 ? 1 : 1_000;
            events.Add(new TraceEventRecord(id, 1, ts, TraceEventKind.End, 0, 0, ++sequence, 1));
        }

        var trace = TraceSession.FromEvents(events, 0, ts, 1_000_000).Process();
        var meta = TraceMetadata.FromEntries(Enumerable.Range(1, 11)
            .Select(id => new TraceMeta(id, "scope-" + id, id == 11 ? "IO" : "CPU")));

        var report = TraceText.Write(trace, meta, topHotspots: 10, categoryFilter: "IO");
        var hotspots = report[..report.IndexOf("Call trees", StringComparison.Ordinal)];

        Assert.Contains("scope-11", hotspots);
    }

    [TestMethod]
    public void Write_NamesThreadsThatHaveAName()
    {
        var events = new List<TraceEventRecord>
        {
            new(1, 7, 0, TraceEventKind.Begin, 0, 0, 1),
            new(1, 7, 1_000, TraceEventKind.End, 0, 0, 2),
            new(1, 8, 0, TraceEventKind.Begin, 0, 0, 1),
            new(1, 8, 1_000, TraceEventKind.End, 0, 0, 2)
        };

        var trace = TraceSession.FromEvents(events, 0, 1_000, 1_000_000,
            threadNames: new Dictionary<int, string> { [7] = "Worker-7" }).Process();

        var report = TraceText.Write(trace);

        Assert.AreEqual("Worker-7", trace.Threads.Single(t => t.ThreadId == 7).Name);
        Assert.IsNull(trace.Threads.Single(t => t.ThreadId == 8).Name);
        Assert.Contains("Thread 7 (Worker-7)", report);
        Assert.Contains("Thread 8" + Environment.NewLine, report);
    }

    [TestMethod]
    public void Write_PrintsTheSessionStartWhenKnown()
    {
        using var tracing = new TracingSession();
        tracing.Start(new SessionOptions { ChunkCapacity = 1024 });
        using (tracing.Scope(1))
        {
        }

        var session = tracing.Stop();
        var report = TraceText.Write(session.Process());

        Assert.Contains("Started: " + session.StartedAtUtc!.Value.ToString("O", CultureInfo.InvariantCulture), report);
        Assert.DoesNotContain("Started:", TraceText.Write(TraceSession.FromEvents([], 0, 0, 1_000_000).Process()));
    }
}
