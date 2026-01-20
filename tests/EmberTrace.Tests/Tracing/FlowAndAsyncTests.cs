using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EmberTrace;
using EmberTrace.Sessions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EmberTrace.Tests.Tracing;

[TestClass]
public class FlowAndAsyncTests
{
    [TestMethod]
    public void FlowHandle_WritesStartStepEnd()
    {
        const int traceId = 5001;
        const int steps = 3;

        Tracer.Start(new SessionOptions { ChunkCapacity = 128 });

        TraceSession session;
        try
        {
            var handle = Tracer.FlowStartNewHandle(traceId);
            for (int i = 0; i < steps; i++)
                handle.Step();
            handle.End();
        }
        finally
        {
            session = Tracer.Stop();
        }

        var events = new List<TraceEventRecord>();
        foreach (var e in session.EnumerateEvents())
            events.Add(e);

        var flowEvents = events.Where(e => e.Id == traceId).ToList();

        Assert.AreEqual(1, flowEvents.Count(e => e.Kind == TraceEventKind.FlowStart));
        Assert.AreEqual(steps, flowEvents.Count(e => e.Kind == TraceEventKind.FlowStep));
        Assert.AreEqual(1, flowEvents.Count(e => e.Kind == TraceEventKind.FlowEnd));

        var flowIds = new HashSet<long>(flowEvents.Select(e => e.FlowId));
        Assert.HasCount(1, flowIds);
        Assert.DoesNotContain(0, flowIds);
    }

    [TestMethod]
    public async Task ScopeAsync_WritesBeginEnd()
    {
        const int id = 6001;
        const int tasks = 5;
        const int iterations = 100;

        Tracer.Start(new SessionOptions { ChunkCapacity = 256 });

        try
        {
            var runners = Enumerable.Range(0, tasks)
                .Select(_ => Task.Run(async () =>
                {
                    for (int i = 0; i < iterations; i++)
                    {
                        await using (Tracer.ScopeAsync(id))
                            await Task.Yield();
                    }
                }));

            await Task.WhenAll(runners);
        }
        finally
        {
            var session = Tracer.Stop();
            Assert.AreEqual(tasks * iterations * 2, session.EventCount);
        }
    }
}
