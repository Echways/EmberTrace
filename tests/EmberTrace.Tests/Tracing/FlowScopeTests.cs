using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EmberTrace;
using EmberTrace.Sessions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EmberTrace.Tests.Tracing;

[TestClass]
public class FlowScopeTests
{
    [TestMethod]
    public void FlowScope_WritesStartStepEnd()
    {
        const int id = 7101;

        Tracer.Start(new SessionOptions { ChunkCapacity = 128 });

        TraceSession session;
        try
        {
            using var flow = Tracer.Flow(id);
            flow.Step();
        }
        finally
        {
            session = Tracer.Stop();
        }

        var events = new List<TraceEventRecord>();
        foreach (var e in session.EnumerateEvents())
            events.Add(e);

        var flowEvents = events.Where(e => e.Id == id).ToList();

        Assert.AreEqual(1, flowEvents.Count(e => e.Kind == TraceEventKind.FlowStart));
        Assert.AreEqual(1, flowEvents.Count(e => e.Kind == TraceEventKind.FlowStep));
        Assert.AreEqual(1, flowEvents.Count(e => e.Kind == TraceEventKind.FlowEnd));
    }

    [TestMethod]
    public async Task FlowScope_WorksAcrossAwait()
    {
        const int id = 7102;

        Tracer.Start(new SessionOptions { ChunkCapacity = 128 });

        TraceSession session;
        try
        {
            using var flow = Tracer.Flow(id);
            await Task.Yield();
            flow.Step();
        }
        finally
        {
            session = Tracer.Stop();
        }

        var flowEvents = new List<TraceEventRecord>();
        foreach (var e in session.EnumerateEvents())
        {
            if (e.Id == id)
                flowEvents.Add(e);
        }

        Assert.AreEqual(1, flowEvents.Count(e => e.Kind == TraceEventKind.FlowStart));
        Assert.AreEqual(1, flowEvents.Count(e => e.Kind == TraceEventKind.FlowStep));
        Assert.AreEqual(1, flowEvents.Count(e => e.Kind == TraceEventKind.FlowEnd));
        Assert.AreNotEqual(0, flowEvents[0].FlowId);
    }

    [TestMethod]
    public void FlowScope_ToHandle_Detaches()
    {
        const int id = 7103;

        Tracer.Start(new SessionOptions { ChunkCapacity = 128 });

        TraceSession session;
        try
        {
            using var flow = Tracer.Flow(id);
            var handle = flow.ToHandle();
            handle.Step();
            handle.End();
            handle.End();
        }
        finally
        {
            session = Tracer.Stop();
        }

        var flowEvents = new List<TraceEventRecord>();
        foreach (var e in session.EnumerateEvents())
        {
            if (e.Id == id)
                flowEvents.Add(e);
        }

        Assert.AreEqual(1, flowEvents.Count(e => e.Kind == TraceEventKind.FlowStart));
        Assert.AreEqual(1, flowEvents.Count(e => e.Kind == TraceEventKind.FlowStep));
        Assert.AreEqual(1, flowEvents.Count(e => e.Kind == TraceEventKind.FlowEnd));
    }
}
