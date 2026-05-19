using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EmberTrace;
using EmberTrace.Sessions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EmberTrace.Tests.Tracing;

[TestClass]
public class TracerConcurrencyTests
{
    [TestMethod]
    public async Task Scopes_FromMultipleThreads_ProduceExpectedEventCount()
    {
        const int threads = 8;
        const int iterations = 1000;
        const int id = 1234;

        var ts = new TracingSession();
        ts.Start(new SessionOptions { ChunkCapacity = 256 });

        try
        {
            var tasks = Enumerable.Range(0, threads)
                .Select(_ => Task.Run(() =>
                {
                    for (int i = 0; i < iterations; i++)
                    {
                        using var _ = ts.Scope(id);
                    }
                }));

            await Task.WhenAll(tasks);
        }
        finally
        {
            var session = ts.Stop();
            var expected = threads * iterations * 2;
            Assert.AreEqual(expected, session.EventCount);
        }
    }

    [TestMethod]
    public void Scope_InterleavedOnSameThread_EachEndsInCorrectProfiler()
    {
        const int id1 = 11;
        const int id2 = 22;

        var session = new TracingSession();
        session.Start(new SessionOptions { ChunkCapacity = 256 });
        Tracer.Start(new SessionOptions { ChunkCapacity = 256 });
        try
        {
            var scope1 = Tracer.Scope(id1);
            var scope2 = session.Scope(id2);
            scope2.Dispose();
            scope1.Dispose();
        }
        finally
        {
            var tracerSession = Tracer.Stop();
            var sessionResult = session.Stop();

            var tracerEvents = Flatten(tracerSession);
            var sessionEvents = Flatten(sessionResult);

            Assert.HasCount(2, tracerEvents, "Tracer.Default should have exactly 2 events (Begin+End)");
            Assert.HasCount(2, sessionEvents, "TracingSession should have exactly 2 events (Begin+End)");

            Assert.AreEqual(id1, tracerEvents[0].Id);
            Assert.AreEqual(TraceEventKind.Begin, tracerEvents[0].Kind);
            Assert.AreEqual(id1, tracerEvents[1].Id);
            Assert.AreEqual(TraceEventKind.End, tracerEvents[1].Kind);

            Assert.AreEqual(id2, sessionEvents[0].Id);
            Assert.AreEqual(TraceEventKind.Begin, sessionEvents[0].Kind);
            Assert.AreEqual(id2, sessionEvents[1].Id);
            Assert.AreEqual(TraceEventKind.End, sessionEvents[1].Kind);
        }
    }

    private static List<TraceEventRecord> Flatten(TraceSession session)
    {
        var list = new List<TraceEventRecord>();
        foreach (var e in session.EnumerateEventsSorted())
            list.Add(e);
        return list;
    }

    [TestMethod]
    public async Task NewFlowId_IsUnique_And_NonZero()
    {
        const int tasks = 6;
        const int perTask = 2000;

        var ts = new TracingSession();
        var ids = new ConcurrentBag<long>();

        var runners = Enumerable.Range(0, tasks)
            .Select(_ => Task.Run(() =>
            {
                for (int i = 0; i < perTask; i++)
                    ids.Add(ts.NewFlowId());
            }));

        await Task.WhenAll(runners);

        Assert.IsFalse(ids.Contains(0));
        Assert.AreEqual(tasks * perTask, ids.Distinct().Count());
    }
}
