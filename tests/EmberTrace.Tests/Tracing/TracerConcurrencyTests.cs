using System.Collections.Concurrent;
using EmberTrace.Sessions;

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
                    for (var i = 0; i < iterations; i++)
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
    public void Stop_WhileWritersAreHot_QuiescesAndKeepsEventsIntact()
    {
        for (var round = 0; round < 20; round++)
        {
            var ts = new TracingSession();
            ts.Start(new SessionOptions { ChunkCapacity = 512 });

            using var stop = new CancellationTokenSource();
            var writers = Enumerable.Range(0, 8)
                .Select(_ => Task.Factory.StartNew(() =>
                {
                    var id = 0;
                    while (!stop.IsCancellationRequested)
                    {
                        id = (id + 1) & 0x3F;
                        ts.Counter(id, id * 31L);
                    }
                }, TaskCreationOptions.LongRunning))
                .ToArray();

            Thread.Sleep(5);
            var session = ts.Stop();
            stop.Cancel();
            Assert.IsTrue(Task.WaitAll(writers, TimeSpan.FromSeconds(10)));

            foreach (var e in session.EnumerateEventsSorted())
            {
                Assert.AreEqual(e.Id * 31L, e.Value, "event fields came from different writes");
                Assert.AreEqual(TraceEventKind.Counter, e.Kind);
                Assert.IsGreaterThan(0, e.Sequence);
            }

            Assert.IsGreaterThan(0, session.EventCount);
        }
    }

    [TestMethod]
    public void OnOverflow_DoesNotRunOnTheWritingThread()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();

        var ts = new TracingSession();
        ts.Start(new SessionOptions
        {
            ChunkCapacity = 512,
            MaxTotalEvents = 1,
            OverflowPolicy = OverflowPolicy.DropNew,
            OnOverflow = _ =>
            {
                entered.Set();
                release.Wait();
            }
        });

        var writer = Task.Run(() =>
        {
            ts.Instant(1);
            ts.Instant(2);
        });

        Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(10)), "the overflow handler must still be invoked");
        Assert.IsTrue(writer.Wait(TimeSpan.FromSeconds(10)),
            "a blocking overflow handler must not stall the tracing thread");

        release.Set();
        Assert.AreEqual(1, ts.Stop().EventCount);
    }

    [TestMethod]
    public void Stop_FromOverflowHandler_DoesNotDeadlock()
    {
        var ts = new TracingSession();
        TraceSession? stopped = null;
        using var fired = new ManualResetEventSlim();

        ts.Start(new SessionOptions
        {
            ChunkCapacity = 512,
            MaxTotalEvents = 1,
            OverflowPolicy = OverflowPolicy.DropNew,
            OnOverflow = _ =>
            {
                stopped = ts.Stop();
                fired.Set();
            }
        });

        var writer = Task.Run(() =>
        {
            for (var i = 0; i < 16; i++)
                ts.Instant(7);
        });

        Assert.IsTrue(writer.Wait(TimeSpan.FromSeconds(10)),
            "Stop() called from the overflow handler must not deadlock");
        Assert.IsTrue(fired.Wait(TimeSpan.FromSeconds(10)));
        Assert.IsNotNull(stopped);
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
                for (var i = 0; i < perTask; i++)
                    ids.Add(ts.NewFlowId());
            }));

        await Task.WhenAll(runners);

        Assert.IsFalse(ids.Contains(0));
        Assert.AreEqual(tasks * perTask, ids.Distinct().Count());
    }
}