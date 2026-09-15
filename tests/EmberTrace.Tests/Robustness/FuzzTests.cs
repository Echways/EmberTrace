using System.Text.Json;
using EmberTrace.Sessions;

namespace EmberTrace.Tests.Robustness;

[TestClass]
public class FuzzTests
{
    [TestMethod]
    [DataRow(1337, false)]
    [DataRow(1337, true)]
    [DataRow(4242, false)]
    [DataRow(4242, true)]
    [DataRow(90210, false)]
    public void RandomScopeStreams_KeepEveryEventAccountedFor(int seed, bool strict)
    {
        var session = RandomSession(seed, threads: 4, events: 600);
        var events = session.Events();
        var begins = events.Count(e => e.Kind == TraceEventKind.Begin);
        var ends = events.Count(e => e.Kind == TraceEventKind.End);

        var stats = session.Analyze(strict);
        var processed = session.Process(strict, groupByThread: false);
        var closed = stats.ByTotalTimeDesc.Sum(r => r.Count);

        Assert.AreEqual(begins, closed + stats.UnmatchedBeginCount);
        Assert.AreEqual(ends, closed + stats.UnmatchedEndCount + (strict ? stats.MismatchedEndCount : 0));
        Assert.AreEqual(closed, processed.HotspotsByInclusiveDesc.Sum(h => h.Count));
        Assert.AreEqual(stats.UnmatchedBeginCount, processed.UnmatchedBeginCount);
        Assert.AreEqual(stats.UnmatchedEndCount, processed.UnmatchedEndCount);
        Assert.AreEqual(stats.MismatchedEndCount, processed.MismatchedEndCount);
        Assert.IsTrue(stats.ByTotalTimeDesc.All(r => r.MinMs >= 0 && r.MinMs <= r.MaxMs));

        Assert.AreEqual(begins + ends, PhaseCount(stream => TraceExport.WriteChromeBeginEnd(session, stream), "B", "E"));
        Assert.AreEqual(session.Analyze().ByTotalTimeDesc.Sum(r => r.Count),
            PhaseCount(stream => TraceExport.WriteChromeComplete(session, stream), "X"));
    }

    private static TraceSession RandomSession(int seed, int threads, int events)
    {
        var rng = new Random(seed);
        var script = new TraceScript();
        var stacks = Enumerable.Range(0, threads).Select(_ => new Stack<int>()).ToArray();
        long timestamp = 0;

        for (var i = 0; i < events; i++)
        {
            var thread = rng.Next(threads);
            var stack = stacks[thread];
            timestamp += rng.Next(1, 5);

            if (stack.Count > 0 && rng.NextDouble() < 0.45)
            {
                var id = rng.NextDouble() < 0.2 ? rng.Next(50, 75) : stack.Pop();
                script.End(id, timestamp, thread + 1);
                continue;
            }

            var begin = rng.Next(1, 10);
            stack.Push(begin);
            script.Begin(begin, timestamp, thread + 1);
        }

        return script.ToSession();
    }

    private static long PhaseCount(Action<Stream> write, params string[] phases)
    {
        using var stream = new MemoryStream();
        write(stream);
        stream.Position = 0;

        using var document = JsonDocument.Parse(stream);
        return document.RootElement.GetProperty("traceEvents").EnumerateArray()
            .Count(e => phases.Contains(e.GetProperty("ph").GetString()));
    }
}
