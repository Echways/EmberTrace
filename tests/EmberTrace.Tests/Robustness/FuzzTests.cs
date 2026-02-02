using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using EmberTrace.Export;
using EmberTrace.Internal.Buffering;
using EmberTrace.Sessions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EmberTrace.Tests.Robustness;

[TestClass]
public class FuzzTests
{
    [TestMethod]
    public void Randomized_BeginEnd_DoesNotThrow_And_ExportsValidJson()
    {
        var events = GenerateRandomEvents(seed: 1337, threads: 4, totalEvents: 600);
        var session = CreateSession(events);

        var stats = session.Analyze(strict: false);
        var processed = session.Process(strict: false, groupByThread: false);

        Assert.IsGreaterThanOrEqualTo(0, stats.UnmatchedBeginCount);
        Assert.IsGreaterThanOrEqualTo(0, stats.UnmatchedEndCount);
        Assert.IsGreaterThanOrEqualTo(0, stats.MismatchedEndCount);
        Assert.IsGreaterThanOrEqualTo(0, processed.UnmatchedBeginCount);
        Assert.IsGreaterThanOrEqualTo(0, processed.UnmatchedEndCount);
        Assert.IsGreaterThanOrEqualTo(0, processed.MismatchedEndCount);

        using var ms = new MemoryStream();
        TraceExport.WriteChromeBeginEnd(session, ms, sortByTimestamp: true);
        ms.Position = 0;
        using var doc = JsonDocument.Parse(ms);
        Assert.AreEqual(JsonValueKind.Object, doc.RootElement.ValueKind);

        ms.SetLength(0);
        TraceExport.WriteChromeComplete(session, ms, sortByStartTimestamp: true);
        ms.Position = 0;
        using var doc2 = JsonDocument.Parse(ms);
        Assert.AreEqual(JsonValueKind.Object, doc2.RootElement.ValueKind);
    }

    private static List<TraceEvent> GenerateRandomEvents(int seed, int threads, int totalEvents)
    {
        var rng = new Random(seed);
        var list = new List<TraceEvent>(totalEvents);
        var stacks = new List<int>[threads];
        var sequences = new long[threads];

        for (int i = 0; i < threads; i++)
            stacks[i] = new List<int>(capacity: 64);

        long timestamp = 0;

        for (int i = 0; i < totalEvents; i++)
        {
            var threadIndex = rng.Next(threads);
            var threadId = threadIndex + 1;
            timestamp += rng.Next(1, 5);

            var stack = stacks[threadIndex];
            var doEnd = stack.Count > 0 && rng.NextDouble() < 0.45;
            int id;
            TraceEventKind kind;

            if (doEnd)
            {
                kind = TraceEventKind.End;
                if (rng.NextDouble() < 0.2)
                {
                    id = rng.Next(50, 75);
                }
                else
                {
                    id = stack[^1];
                    stack.RemoveAt(stack.Count - 1);
                }
            }
            else
            {
                kind = TraceEventKind.Begin;
                id = rng.Next(1, 10);
                stack.Add(id);
            }

            var sequence = ++sequences[threadIndex];
            list.Add(new TraceEvent(id, threadId, timestamp, kind, 0, 0, sequence));
        }

        return list;
    }

    private static TraceSession CreateSession(List<TraceEvent> events)
    {
        var capacity = Math.Max(1, events.Count);
        var chunk = new Chunk(capacity);
        for (int i = 0; i < events.Count; i++)
            chunk.Events[i] = events[i];
        chunk.Count = events.Count;

        var options = new SessionOptions { ChunkCapacity = capacity };
        var start = events.Count > 0 ? events[0].Timestamp : 0;
        var end = events.Count > 0 ? events[^1].Timestamp : start;

        return new TraceSession(new[] { chunk }, start, end, options, new Dictionary<int, string>(), 0, 0, 0, wasOverflow: false);
    }
}
