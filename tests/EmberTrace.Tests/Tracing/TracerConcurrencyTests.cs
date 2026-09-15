using EmberTrace.Sessions;

namespace EmberTrace.Tests.Tracing;

[TestClass]
public class TracerConcurrencyTests
{
    [TestMethod]
    public void Stop_WhileWritersAreHot_QuiescesAndKeepsEventsIntact()
    {
        for (var round = 0; round < 20; round++)
        {
            using var tracing = new TracingSession();
            tracing.Start(new SessionOptions { ChunkCapacity = 512 });

            using var stop = new CancellationTokenSource();
            var writers = Enumerable.Range(0, 8)
                .Select(_ => Task.Factory.StartNew(() =>
                {
                    var id = 0;
                    while (!stop.IsCancellationRequested)
                    {
                        id = (id + 1) & 0x3F;
                        tracing.Counter(id, id * 31L);
                    }
                }, TaskCreationOptions.LongRunning))
                .ToArray();

            Thread.Sleep(5);
            var session = tracing.Stop();
            stop.Cancel();
            Assert.IsTrue(Task.WaitAll(writers, TimeSpan.FromSeconds(10)));

            Assert.IsGreaterThan(0, session.EventCount);

            foreach (var e in session.EnumerateEventsSorted())
            {
                Assert.AreEqual(e.Id * 31L, e.Value);
                Assert.AreEqual(TraceEventKind.Counter, e.Kind);
                Assert.IsGreaterThan(0, e.Sequence);
            }
        }
    }

    [TestMethod]
    public void OnOverflow_BlockingHandler_DoesNotStallTheWritingThread()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var tracing = new TracingSession();

        tracing.Start(new SessionOptions
        {
            ChunkCapacity = 512,
            MaxTotalEvents = 1,
            OnOverflow = _ =>
            {
                entered.Set();
                release.Wait();
            }
        });

        var writer = Task.Run(() =>
        {
            tracing.Instant(1);
            tracing.Instant(2);
        });

        Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(10)));
        Assert.IsTrue(writer.Wait(TimeSpan.FromSeconds(10)));

        release.Set();
        Assert.AreEqual(1L, tracing.Stop().EventCount);
    }

    [TestMethod]
    public void Stop_FromTheOverflowHandler_DoesNotDeadlock()
    {
        using var fired = new ManualResetEventSlim();
        using var tracing = new TracingSession();
        TraceSession? stopped = null;

        tracing.Start(new SessionOptions
        {
            ChunkCapacity = 512,
            MaxTotalEvents = 1,
            OnOverflow = _ =>
            {
                stopped = tracing.Stop();
                fired.Set();
            }
        });

        var writer = Task.Run(() =>
        {
            for (var i = 0; i < 16; i++)
                tracing.Instant(7);
        });

        Assert.IsTrue(writer.Wait(TimeSpan.FromSeconds(10)));
        Assert.IsTrue(fired.Wait(TimeSpan.FromSeconds(10)));
        Assert.AreEqual(1L, stopped!.EventCount);
        Assert.IsFalse(tracing.IsRunning);
    }
}
