using System;
using EmberTrace;
using EmberTrace.Internal.Buffering;
using EmberTrace.Internal.Time;
using EmberTrace.Sessions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EmberTrace.Tests.Analysis;

[TestClass]
public class TraceAnalysisTests
{
    [TestMethod]
    public void AnalyzeFlows_ComputesDurations()
    {
        var freq = Timestamp.Frequency;
        var start = 100L;
        var step = start + freq;
        var end = start + (freq * 3);

        var session = CreateSession(new[]
        {
            new TraceEvent(1, 1, start, TraceEventKind.FlowStart, 42, 0),
            new TraceEvent(1, 1, step, TraceEventKind.FlowStep, 42, 0),
            new TraceEvent(1, 1, end, TraceEventKind.FlowEnd, 42, 0)
        });

        var flows = session.AnalyzeFlows();

        Assert.HasCount(1, flows);
        var flow = flows[0];
        Assert.AreEqual(42, flow.FlowId);
        Assert.HasCount(2, flow.Steps);
        Assert.AreEqual(3000.0, flow.TotalDurationMs, 0.01);
        Assert.AreEqual(1000.0, flow.Steps[0].DurationMs, 0.01);
        Assert.AreEqual(2000.0, flow.Steps[1].DurationMs, 0.01);
    }

    [TestMethod]
    public void Analyze_StrictMode_TracksMismatches()
    {
        var session = CreateSession(new[]
        {
            new TraceEvent(1, 1, 10, TraceEventKind.Begin, 0, 0),
            new TraceEvent(2, 1, 20, TraceEventKind.Begin, 0, 0),
            new TraceEvent(1, 1, 30, TraceEventKind.End, 0, 0),
            new TraceEvent(2, 1, 40, TraceEventKind.End, 0, 0),
            new TraceEvent(3, 1, 50, TraceEventKind.End, 0, 0)
        });

        var nonStrict = session.Analyze(strict: false);
        Assert.AreEqual(1, nonStrict.MismatchedEndCount);
        Assert.AreEqual(1, nonStrict.UnmatchedBeginCount);
        Assert.AreEqual(2, nonStrict.UnmatchedEndCount);

        var strict = session.Analyze(strict: true);
        Assert.AreEqual(2, strict.MismatchedEndCount);
        Assert.AreEqual(1, strict.UnmatchedBeginCount);
        Assert.AreEqual(0, strict.UnmatchedEndCount);

        var processed = session.Process(strict: true);
        Assert.AreEqual(strict.MismatchedEndCount, processed.MismatchedEndCount);
        Assert.AreEqual(strict.UnmatchedBeginCount, processed.UnmatchedBeginCount);
        Assert.AreEqual(strict.UnmatchedEndCount, processed.UnmatchedEndCount);
    }

    private static TraceSession CreateSession(TraceEvent[] events)
    {
        var capacity = Math.Max(1, events.Length);
        var chunk = new Chunk(capacity);
        Array.Copy(events, chunk.Events, events.Length);
        chunk.Count = events.Length;

        var options = new SessionOptions { ChunkCapacity = capacity };
        var start = events.Length > 0 ? events[0].Timestamp : 0;
        var end = events.Length > 0 ? events[events.Length - 1].Timestamp : start;

        return new TraceSession(new[] { chunk }, start, end, options, 0, 0, 0, wasOverflow: false);
    }
}
