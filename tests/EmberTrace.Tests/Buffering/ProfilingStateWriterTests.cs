using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EmberTrace.Internal;
using EmberTrace.Internal.Buffering;
using EmberTrace.Sessions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EmberTrace.Tests.Buffering;

[TestClass]
public class ProfilingStateWriterTests
{
    private static ProfilingState CreateState()
    {
        var options = new SessionOptions { ChunkCapacity = 1024 };
        var collector = new SessionCollector(options, new ChunkPool(options.ChunkCapacity), options.ChunkCapacity);
        return new ProfilingState(options, collector, null, default, 0);
    }

    [TestMethod]
    public void GetWriter_SameThread_ReturnsSameInstance()
    {
        var state = CreateState();

        var first = state.GetWriter();

        for (int i = 0; i < 100; i++)
            Assert.AreSame(first, state.GetWriter());

        Assert.HasCount(1, state.Writers.ToArray());
    }

    [TestMethod]
    public void GetWriter_DistinctStates_DoNotShareWriters()
    {
        var first = CreateState();
        var second = CreateState();

        Assert.AreNotSame(first.GetWriter(), second.GetWriter());
        Assert.AreNotEqual(first.Id, second.Id);
    }

    [TestMethod]
    public async Task GetWriter_OneWriterPerThread_UnderConcurrency()
    {
        var state = CreateState();

        const int tasks = 8;
        const int callsPerTask = 500;

        var observed = await Task.WhenAll(Enumerable.Range(0, tasks).Select(_idx => Task.Run(() =>
        {
            var writer = state.GetWriter();
            for (int i = 0; i < callsPerTask; i++)
                Assert.AreSame(writer, state.GetWriter());

            return (Thread: Environment.CurrentManagedThreadId, Writer: writer);
        })));

        var threads = new HashSet<int>(observed.Select(o => o.Thread));
        var writers = new HashSet<ThreadWriter>(observed.Select(o => o.Writer));

        Assert.HasCount(threads.Count, writers);
        Assert.HasCount(threads.Count, state.Writers.ToArray());
    }
}
