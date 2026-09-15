using EmberTrace.Sessions;

namespace EmberTrace.Tests.Tracing;

[TestClass]
public class FlowTests
{
    private const int Id = 7101;

    [TestMethod]
    public void FlowScope_WritesStartStepEndUnderOneFlowId()
    {
        long flowId = 0;
        var events = Record(tracing =>
        {
            using var flow = tracing.Flow(Id);
            flowId = flow.FlowId;
            flow.Step();
        });

        AssertFlow(events, flowId, steps: 1);
    }

    [TestMethod]
    public async Task FlowScope_WorksAcrossAwait()
    {
        using var tracing = new TracingSession();
        tracing.Start(new SessionOptions { ChunkCapacity = 128 });

        long flowId;
        using (var flow = tracing.Flow(Id))
        {
            flowId = flow.FlowId;
            await Task.Yield();
            flow.Step();
        }

        AssertFlow(tracing.Stop().SortedEvents(), flowId, steps: 1);
    }

    [TestMethod]
    public void FlowScope_ToHandle_TransfersTheEndToTheHandle()
    {
        long flowId = 0;
        var events = Record(tracing =>
        {
            using var flow = tracing.Flow(Id);
            flowId = flow.FlowId;
            var handle = flow.ToHandle();

            flow.Step();
            Assert.IsFalse(flow.ToHandle().IsValid);

            handle.Step();
            Assert.IsTrue(handle.TryEnd());
            Assert.IsFalse(handle.TryEnd());
        });

        AssertFlow(events, flowId, steps: 1);
    }

    [TestMethod]
    public void FlowHandle_EndsOnceAndIgnoresStepsAfterTheEnd()
    {
        const int steps = 3;

        long flowId = 0;
        var events = Record(tracing =>
        {
            var handle = tracing.FlowStartNewHandle(Id);
            flowId = handle.FlowId;

            for (var i = 0; i < steps; i++)
                tracing.FlowStep(handle);

            tracing.FlowEnd(handle);
            handle.End();
            handle.Step();
        });

        AssertFlow(events, flowId, steps);
    }

    [TestMethod]
    public async Task NewFlowId_IsUniqueAndNonZeroAcrossThreads()
    {
        const int tasks = 6;
        const int perTask = 2000;

        using var tracing = new TracingSession();

        var ids = await Task.WhenAll(Enumerable.Range(0, tasks).Select(_ => Task.Run(() =>
            Enumerable.Range(0, perTask).Select(_ => tracing.NewFlowId()).ToArray())));

        var all = ids.SelectMany(batch => batch).ToArray();

        Assert.DoesNotContain(0L, all);
        Assert.HasCount(tasks * perTask, all.Distinct());
    }

    private static void AssertFlow(List<TraceEventRecord> events, long flowId, int steps)
    {
        var expected = new List<TraceEventKind> { TraceEventKind.FlowStart };
        expected.AddRange(Enumerable.Repeat(TraceEventKind.FlowStep, steps));
        expected.Add(TraceEventKind.FlowEnd);

        Assert.AreNotEqual(0L, flowId);
        CollectionAssert.AreEqual(expected, events.Select(e => e.Kind).ToList());
        Assert.IsTrue(events.All(e => e.Id == Id && e.FlowId == flowId));
    }

    private static List<TraceEventRecord> Record(Action<TracingSession> body)
    {
        using var tracing = new TracingSession();
        tracing.Start(new SessionOptions { ChunkCapacity = 128 });
        body(tracing);
        return tracing.Stop().SortedEvents();
    }
}
