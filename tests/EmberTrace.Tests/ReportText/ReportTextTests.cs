using EmberTrace;
using EmberTrace.Metadata;
using EmberTrace.ReportText;
using EmberTrace.Sessions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EmberTrace.Tests.ReportText;

[TestClass]
public class ReportTextTests
{
    [TestMethod]
    public void Report_IncludesCountersAndCategories()
    {
        const int ioId = 9001;
        const int cpuId = 9002;

        Tracer.Start(new SessionOptions { ChunkCapacity = 64 });
        TraceSession session;
        try
        {
            using (Tracer.Scope(ioId)) { }
            using (Tracer.Scope(cpuId)) { }
        }
        finally
        {
            session = Tracer.Stop();
        }

        var trace = session.Process();
        var meta = new TestMetaProvider();

        var report = TraceText.Write(trace, meta);

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
}
