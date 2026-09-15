using EmberTrace.Sessions;

namespace EmberTrace.Tests.Tracing;

[TestClass]
public class StressTests
{
    [TestMethod]
    public async Task MixedScopesAndFlows_KeepEveryEventAndStayBalanced()
    {
        const int flowEventId = 7100;
        const int tasks = 8;
        const int iterations = 3000;
        const int flowEvery = 50;

        using var tracing = new TracingSession();
        tracing.Start(new SessionOptions { ChunkCapacity = 256 });

        await Task.WhenAll(Enumerable.Range(0, tasks).Select(task => Task.Run(() =>
        {
            for (var i = 0; i < iterations; i++)
                using (tracing.Scope(7000 + task % 3))
                {
                    if (i % flowEvery != 0)
                        continue;

                    var flowId = tracing.NewFlowId();
                    tracing.FlowStart(flowEventId, flowId);
                    tracing.FlowStep(flowEventId, flowId);
                    tracing.FlowEnd(flowEventId, flowId);
                }
        })));

        var session = tracing.Stop();
        var stats = session.Analyze();

        var flowsPerTask = (iterations + flowEvery - 1) / flowEvery;
        Assert.AreEqual(tasks * iterations * 2 + tasks * flowsPerTask * 3, session.EventCount);
        Assert.AreEqual(tasks * iterations, stats.ByTotalTimeDesc.Sum(row => row.Count));
        Assert.AreEqual(0, stats.UnmatchedBeginCount + stats.UnmatchedEndCount + stats.MismatchedEndCount);
        Assert.HasCount(tasks * flowsPerTask, session.AnalyzeFlows(int.MaxValue));
    }

    [TestMethod]
    public async Task ConcurrentAsyncScopeChains_KeepEveryEventAndStayBalanced()
    {
        const int id = 7200;
        const int tasks = 6;
        const int iterations = 400;

        using var tracing = new TracingSession();
        tracing.Start(new SessionOptions { ChunkCapacity = 256 });

        await Task.WhenAll(Enumerable.Range(0, tasks).Select(_ => Task.Run(async () =>
        {
            for (var i = 0; i < iterations; i++)
                await using (tracing.ScopeAsync(id))
                {
                    await Task.Yield();
                }
        })));

        var session = tracing.Stop();
        var stats = session.Analyze();

        Assert.AreEqual(tasks * iterations * 2, session.EventCount);
        Assert.AreEqual(tasks * iterations, stats.ByTotalTimeDesc.Single().Count);
        Assert.AreEqual(0, stats.UnmatchedBeginCount + stats.UnmatchedEndCount + stats.MismatchedEndCount);
    }
}
