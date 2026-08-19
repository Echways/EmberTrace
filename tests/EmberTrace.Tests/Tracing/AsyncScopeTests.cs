using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EmberTrace.Analysis.Model;
using EmberTrace.Sessions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EmberTrace.Tests.Tracing;

[TestClass]
public class AsyncScopeTests
{
    [TestMethod]
    public async Task ScopeAsync_SequentialAwaits_MatchesEveryScope()
    {
        const int id = 7001;
        const int iterations = 20;

        var ts = new TracingSession();
        ts.Start(new SessionOptions { ChunkCapacity = 256 });

        TraceSession session;
        try
        {
            for (int i = 0; i < iterations; i++)
            {
                await using (ts.ScopeAsync(id))
                    await Task.Delay(5);
            }
        }
        finally
        {
            session = ts.Stop();
        }

        var stats = session.Analyze();

        Assert.AreEqual(0, stats.UnmatchedBeginCount);
        Assert.AreEqual(0, stats.UnmatchedEndCount);
        Assert.AreEqual(0, stats.MismatchedEndCount);

        var row = stats.ByTotalTimeDesc.Single(x => x.Id == id);
        Assert.AreEqual(iterations, row.Count);
        Assert.IsGreaterThanOrEqualTo(3.0, row.MinMs);
    }

    [TestMethod]
    public async Task ScopeAsync_ConcurrentChains_DoNotStealEachOthersEnds()
    {
        const int id = 7002;
        const int chains = 8;
        const int iterations = 10;

        var ts = new TracingSession();
        ts.Start(new SessionOptions { ChunkCapacity = 256 });

        TraceSession session;
        try
        {
            await Task.WhenAll(Enumerable.Range(0, chains).Select(_ => Task.Run(async () =>
            {
                for (int i = 0; i < iterations; i++)
                {
                    await using (ts.ScopeAsync(id))
                        await Task.Delay(5);
                }
            })));
        }
        finally
        {
            session = ts.Stop();
        }

        var stats = session.Analyze();

        Assert.AreEqual(0, stats.UnmatchedBeginCount);
        Assert.AreEqual(0, stats.UnmatchedEndCount);
        Assert.AreEqual(0, stats.MismatchedEndCount);

        var row = stats.ByTotalTimeDesc.Single(x => x.Id == id);
        Assert.AreEqual(chains * iterations, row.Count);
        Assert.IsGreaterThanOrEqualTo(3.0, row.MinMs);
    }

    [TestMethod]
    public async Task ScopeAsync_SyncScopeAfterAwait_StaysNestedUnderAsyncScope()
    {
        const int outer = 7003;
        const int inner = 7004;

        var ts = new TracingSession();
        ts.Start(new SessionOptions { ChunkCapacity = 256 });

        TraceSession session;
        try
        {
            await using (ts.ScopeAsync(outer))
            {
                await Task.Delay(5);
                using (ts.Scope(inner))
                    Thread.Sleep(1);
            }
        }
        finally
        {
            session = ts.Stop();
        }

        var processed = session.Process(groupByThread: false);

        var outerNode = Child(processed.GlobalRoot, outer);
        Assert.AreEqual(1, outerNode.Count);

        var innerNode = Child(outerNode, inner);
        Assert.AreEqual(1, innerNode.Count);
    }

    [TestMethod]
    public async Task ScopeAsync_NestedAsyncScopes_KeepTheirNesting()
    {
        const int outer = 7005;
        const int inner = 7006;

        var ts = new TracingSession();
        ts.Start(new SessionOptions { ChunkCapacity = 256 });

        TraceSession session;
        try
        {
            await using (ts.ScopeAsync(outer))
            {
                await Task.Delay(5);
                await using (ts.ScopeAsync(inner))
                    await Task.Delay(5);
            }
        }
        finally
        {
            session = ts.Stop();
        }

        var stats = session.Analyze();
        Assert.AreEqual(0, stats.UnmatchedBeginCount);
        Assert.AreEqual(0, stats.UnmatchedEndCount);

        var processed = session.Process(groupByThread: false);
        var outerNode = Child(processed.GlobalRoot, outer);
        var innerNode = Child(outerNode, inner);

        Assert.AreEqual(1, innerNode.Count);
        Assert.IsGreaterThanOrEqualTo(innerNode.InclusiveMs, outerNode.InclusiveMs);
    }

    [TestMethod]
    public async Task ScopeAsync_ParallelChildren_AttachToTheSameAsyncParent()
    {
        const int outer = 7007;
        const int child = 7008;
        const int workers = 4;

        var ts = new TracingSession();
        ts.Start(new SessionOptions { ChunkCapacity = 256 });

        TraceSession session;
        try
        {
            await using (ts.ScopeAsync(outer))
            {
                await Task.WhenAll(Enumerable.Range(0, workers).Select(_ => Task.Run(() =>
                {
                    using (ts.Scope(child))
                        Thread.Sleep(5);
                })));
            }
        }
        finally
        {
            session = ts.Stop();
        }

        var stats = session.Analyze();
        Assert.AreEqual(0, stats.UnmatchedBeginCount);
        Assert.AreEqual(0, stats.UnmatchedEndCount);
        Assert.AreEqual(0, stats.MismatchedEndCount);

        var processed = session.Process(groupByThread: false);
        var outerNode = Child(processed.GlobalRoot, outer);
        var childNode = Child(outerNode, child);

        Assert.AreEqual(workers, childNode.Count);
    }

    [TestMethod]
    public async Task ScopeAsync_EventsCarryScopeIdentity()
    {
        const int asyncId = 7009;
        const int syncId = 7010;

        var ts = new TracingSession();
        ts.Start(new SessionOptions { ChunkCapacity = 256 });

        TraceSession session;
        try
        {
            await using (ts.ScopeAsync(asyncId))
            {
                await Task.Delay(5);
                using (ts.Scope(syncId))
                    Thread.Sleep(1);
            }
        }
        finally
        {
            session = ts.Stop();
        }

        var events = new List<TraceEventRecord>();
        foreach (var e in session.EnumerateEventsSorted())
            events.Add(e);

        var asyncEvents = events.Where(e => e.Id == asyncId).ToArray();
        Assert.HasCount(2, asyncEvents);
        Assert.AreNotEqual(0L, asyncEvents[0].AsyncScopeId);
        Assert.AreEqual(asyncEvents[0].AsyncScopeId, asyncEvents[1].AsyncScopeId);
        Assert.AreEqual(0L, asyncEvents[0].AsyncContextId);

        var syncEvents = events.Where(e => e.Id == syncId).ToArray();
        Assert.HasCount(2, syncEvents);
        Assert.IsTrue(syncEvents.All(e => e.AsyncScopeId == 0));
        Assert.IsTrue(syncEvents.All(e => e.AsyncContextId == asyncEvents[0].AsyncScopeId));
    }

    [TestMethod]
    public async Task ScopeAsync_AfterSessionStopped_DoesNotThrow()
    {
        const int id = 7011;

        var ts = new TracingSession();
        ts.Start(new SessionOptions { ChunkCapacity = 256 });

        await using (ts.ScopeAsync(id))
        {
            await Task.Delay(1);
            ts.Stop();
        }

        Assert.IsFalse(ts.IsRunning);
    }

    private static CallTreeNode Child(CallTreeNode node, int id)
    {
        foreach (var c in node.Children)
        {
            if (c.Id == id)
                return c;
        }

        Assert.Fail($"node {id} is not a child of {node.Id}");
        return default!;
    }
}
