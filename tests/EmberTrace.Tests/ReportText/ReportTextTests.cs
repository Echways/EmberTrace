using System.Globalization;
using EmberTrace.Analysis.Model;
using EmberTrace.Metadata;
using EmberTrace.Sessions;

namespace EmberTrace.Tests.ReportText;

[TestClass]
public class ReportTextTests
{
    private const int Disk = 1;
    private const int Cpu = 2;
    private const int Nested = 3;

    private static readonly string Nl = Environment.NewLine;

    [TestMethod]
    public void Write_SummarisesEverySessionCounter()
    {
        var session = TraceSession.FromEvents(
            new TraceScript().Span(Disk, 0, 1_000).Instant(Cpu, 1_500).ToSession().SortedEvents(),
            0, 2_000, 1_000_000,
            droppedEvents: 4, droppedChunks: 1, sampledOutEvents: 6, wasOverflow: true);

        var report = TraceText.Write(session.Process());

        StringAssert.StartsWith(report, "Summary" + Nl);
        foreach (var line in new[]
                 {
                     "Duration: 2.000 ms", "Events: 3", "ScopeEvents: 2", "Threads: 1", "DroppedEvents: 4",
                     "DroppedChunks: 1", "SampledOut: 6", "UnmatchedBegin: 0", "UnmatchedEnd: 0", "MismatchedEnd: 0"
                 })
            Assert.Contains(Nl + line + Nl, report);

        Assert.DoesNotContain("Started:", report);
    }

    [TestMethod]
    public void Write_PrintsTheSessionStartWhenKnown()
    {
        using var tracing = new TracingSession();
        tracing.Start(new SessionOptions { ChunkCapacity = 1024 });
        var session = tracing.Stop();

        var report = TraceText.Write(session.Process());

        Assert.Contains(
            "Started: " + session.StartedAtUtc!.Value.ToString("O", CultureInfo.InvariantCulture) + Nl, report);
    }

    [TestMethod]
    public void Write_GroupsCategoriesAndFiltersEverySectionByCategory()
    {
        var trace = Trace();

        var report = TraceText.Write(trace, Metadata());
        var filtered = TraceText.Write(trace, Metadata(), categoryFilter: "io");

        Assert.IsGreaterThan(0, Row(report, "IO"));
        Assert.IsLessThan(Row(report, "CPU"), Row(report, "IO"));
        Assert.Contains("DiskRead", filtered);
        Assert.DoesNotContain("Compute", filtered);
        Assert.DoesNotContain("CPU", filtered);
    }

    [TestMethod]
    public void Write_WithoutMetadataCategories_OmitsTheCategorySection()
    {
        var report = TraceText.Write(Trace());

        Assert.DoesNotContain("Categories (by inclusive)", report);
        Assert.Contains("Hotspots (by inclusive)", report);
        Assert.Contains("Call trees", report);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Write_PercentileColumns_AreOptIn(bool includePercentiles)
    {
        var report = TraceText.Write(Trace(), includePercentiles: includePercentiles);

        Assert.AreEqual(includePercentiles, report.Contains("p50 ms"));
        Assert.AreEqual(includePercentiles, report.Contains("p95 ms"));
        Assert.AreEqual(includePercentiles, report.Contains("p99 ms"));
    }

    [TestMethod]
    public void Write_CategoryFilter_AppliesBeforeTheTopLimit()
    {
        var script = new TraceScript();
        for (var id = 1; id <= 11; id++)
            script.Span(id, id * 10_000L, id * 10_000L + (id == 11 ? 1 : 1_000));

        var meta = Meta.Of(Enumerable.Range(1, 11)
            .Select(id => (id, "scope-" + id, (string?)(id == 11 ? "IO" : "CPU"))).ToArray());

        var report = TraceText.Write(script.ToSession().Process(), meta, topHotspots: 10, categoryFilter: "IO");

        Assert.Contains("scope-11", Section(report, "Hotspots (by inclusive)", "Categories"));
    }

    [TestMethod]
    public void Write_TopHotspots_LimitsTheHotspotRows()
    {
        var report = TraceText.Write(Trace(), Metadata(), topHotspots: 1);
        var hotspots = Section(report, "Hotspots (by inclusive)", "Categories");

        Assert.Contains("DiskRead", hotspots);
        Assert.DoesNotContain("Compute", hotspots);
    }

    [TestMethod]
    public void Write_MaxDepthAndMinPercent_PruneTheCallTree()
    {
        var shallow = Section(TraceText.Write(Trace(), Metadata(), maxDepth: 1), "Call trees", null);
        var deep = Section(TraceText.Write(Trace(), Metadata(), maxDepth: 2), "Call trees", null);
        var significant = Section(TraceText.Write(Trace(), Metadata(), minPercent: 10), "Call trees", null);

        Assert.DoesNotContain("Nested", shallow);
        Assert.Contains("Nested", deep);
        Assert.Contains("DiskRead", significant);
        Assert.DoesNotContain("Nested", significant);
        Assert.DoesNotContain("Compute", significant);
    }

    [TestMethod]
    public void Write_NamesThreadsThatHaveAName()
    {
        var trace = new TraceScript().Span(1, 0, 1_000, 7).Span(1, 0, 1_000, 8)
            .ToSession(threadNames: new Dictionary<int, string> { [7] = "Worker-7" })
            .Process();

        var report = TraceText.Write(trace);

        Assert.Contains("Thread 7 (Worker-7)" + Nl, report);
        Assert.Contains("Thread 8" + Nl, report);
    }

    private static ProcessedTrace Trace()
    {
        return new TraceScript()
            .Begin(Disk, 0)
            .Span(Nested, 1_000, 1_500)
            .End(Disk, 8_000)
            .Span(Cpu, 8_000, 8_500)
            .ToSession(end: 10_000)
            .Process();
    }

    private static ITraceMetadataProvider Metadata()
    {
        return Meta.Of((Disk, "DiskRead", "IO"), (Cpu, "Compute", "CPU"), (Nested, "Nested", "IO"));
    }

    private static int Row(string report, string category)
    {
        return report.IndexOf(Nl + category + " ", report.IndexOf("Categories", StringComparison.Ordinal),
            StringComparison.Ordinal);
    }

    private static string Section(string report, string header, string? next)
    {
        var start = report.IndexOf(header, StringComparison.Ordinal);
        var end = next is null ? report.Length : report.IndexOf(next, start, StringComparison.Ordinal);
        return report[start..(end < 0 ? report.Length : end)];
    }
}
