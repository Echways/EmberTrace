using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
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

        Tracer.Start(new SessionOptions { ChunkCapacity = 256 });

        try
        {
            var tasks = Enumerable.Range(0, threads)
                .Select(_ => Task.Run(() =>
                {
                    for (int i = 0; i < iterations; i++)
                    {
                        using var _ = Tracer.Scope(id);
                    }
                }));

            await Task.WhenAll(tasks);
        }
        finally
        {
            var session = Tracer.Stop();
            var expected = threads * iterations * 2;
            Assert.AreEqual(expected, session.EventCount);
        }
    }

    [TestMethod]
    public async Task NewFlowId_IsUnique_And_NonZero()
    {
        const int tasks = 6;
        const int perTask = 2000;

        var ids = new ConcurrentBag<long>();

        var runners = Enumerable.Range(0, tasks)
            .Select(_ => Task.Run(() =>
            {
                for (int i = 0; i < perTask; i++)
                    ids.Add(Tracer.NewFlowId());
            }));

        await Task.WhenAll(runners);

        Assert.IsFalse(ids.Contains(0));
        Assert.AreEqual(tasks * perTask, ids.Distinct().Count());
    }
}
