using System;
using System.Linq;
using System.Threading.Tasks;
using EmberTrace;
using EmberTrace.Sessions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EmberTrace.Tests.Tracing;

[TestClass]
public class StressTests
{
    [TestMethod]
    public async Task MixedScopesAndFlows_AreStableUnderLoad()
    {
        const int scopeIdBase = 7000;
        const int flowId = 7100;
        const int tasks = 8;
        const int iterations = 3000;
        const int flowEvery = 50;

        var ts = new TracingSession();
        ts.Start(new SessionOptions { ChunkCapacity = 256 });

        try
        {
            var runners = Enumerable.Range(0, tasks)
                .Select(taskIndex => Task.Run(() =>
                {
                    for (int i = 0; i < iterations; i++)
                    {
                        var scopeId = scopeIdBase + (taskIndex % 3);
                        using (ts.Scope(scopeId))
                        {
                            if (i % flowEvery == 0)
                            {
                                var id = ts.NewFlowId();
                                ts.FlowStart(flowId, id);
                                ts.FlowStep(flowId, id);
                                ts.FlowEnd(flowId, id);
                            }
                        }
                    }
                }));

            await Task.WhenAll(runners);
        }
        finally
        {
            var session = ts.Stop();

            var flowPerTask = (iterations + flowEvery - 1) / flowEvery;
            var expectedScopeEvents = tasks * iterations * 2;
            var expectedFlowEvents = tasks * flowPerTask * 3;

            Assert.AreEqual(expectedScopeEvents + expectedFlowEvents, session.EventCount);
        }
    }

    [TestMethod]
    public async Task MixedAsyncScopes_DoNotLeakEvents()
    {
        const int id = 7200;
        const int tasks = 6;
        const int iterations = 400;

        var ts = new TracingSession();
        ts.Start(new SessionOptions { ChunkCapacity = 256 });

        try
        {
            var runners = Enumerable.Range(0, tasks)
                .Select(_ => Task.Run(async () =>
                {
                    for (int i = 0; i < iterations; i++)
                    {
                        await using (ts.ScopeAsync(id))
                            await Task.Delay(1);
                    }
                }));

            await Task.WhenAll(runners);
        }
        finally
        {
            var session = ts.Stop();
            Assert.AreEqual(tasks * iterations * 2, session.EventCount);
        }
    }
}
