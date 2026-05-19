using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using EmberTrace.Internal.Buffering;
using EmberTrace.Internal.Time;
using EmberTrace.Metadata;
using EmberTrace.OpenTelemetry;
using EmberTrace.Sessions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EmberTrace.Tests.Export;

[TestClass]
public class OpenTelemetryExportTests
{
    private static readonly DateTimeOffset FixedBase =
        new(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void CreateSpans_NullSession_ThrowsArgumentNullException()
    {
        bool threw = false;
        try { OpenTelemetryExport.CreateSpans(null!); }
        catch (ArgumentNullException) { threw = true; }
        Assert.IsTrue(threw, "Expected ArgumentNullException for null session");
    }

    [TestMethod]
    public void CreateSpans_EmptySession_ReturnsEmptyList()
    {
        var session = BuildSession();
        var spans = OpenTelemetryExport.CreateSpans(session);

        Assert.AreEqual(0, spans.Count);
    }

    [TestMethod]
    public void CreateSpans_OnlyCounterAndInstantEvents_ReturnsEmpty()
    {
        long freq = Timestamp.Frequency;
        var session = BuildSession(
            new TraceEvent(1, 1, 0,    TraceEventKind.Instant, 0, 0),
            new TraceEvent(2, 1, freq, TraceEventKind.Counter, 0, 99));

        var spans = OpenTelemetryExport.CreateSpans(session, options: Opts());

        Assert.AreEqual(0, spans.Count);
    }

    [TestMethod]
    public void CreateSpans_SingleBeginEnd_ProducesOneSpan()
    {
        long freq = Timestamp.Frequency;
        var meta = Meta(1, "TestOp");

        var session = BuildSession(
            new TraceEvent(1, 1, 0,    TraceEventKind.Begin, 0, 0),
            new TraceEvent(1, 1, freq, TraceEventKind.End,   0, 0));

        var spans = OpenTelemetryExport.CreateSpans(session, meta, Opts());

        Assert.AreEqual(1, spans.Count);
        Assert.AreEqual("TestOp", spans[0].DisplayName);
    }

    [TestMethod]
    public void CreateSpans_UnknownId_UsesIdAsSpanName()
    {
        long freq = Timestamp.Frequency;
        const int id = 12345;

        var session = BuildSession(
            new TraceEvent(id, 1, 0,    TraceEventKind.Begin, 0, 0),
            new TraceEvent(id, 1, freq, TraceEventKind.End,   0, 0));

        var spans = OpenTelemetryExport.CreateSpans(session, options: Opts());

        Assert.AreEqual(id.ToString(), spans[0].DisplayName);
    }

    [TestMethod]
    public void CreateSpans_TimestampDelta_ConvertsToCorrectUtcOffset()
    {
        long freq = Timestamp.Frequency;
        var session = BuildSession(
            startTs: 0,
            endTs: freq * 2,
            new TraceEvent(1, 1, 0,    TraceEventKind.Begin, 0, 0),
            new TraceEvent(1, 1, freq, TraceEventKind.End,   0, 0));

        var opts = new OpenTelemetryExportOptions { BaseUtc = FixedBase };
        var spans = OpenTelemetryExport.CreateSpans(session, Meta(1, "Op"), opts);

        var span = spans[0];
        Assert.AreEqual(FixedBase.UtcDateTime, span.StartTimeUtc,
            "Span start at timestamp=startTs should equal BaseUtc");

        var expectedEnd = FixedBase.UtcDateTime + TimeSpan.FromSeconds(1);
        Assert.IsTrue(span.Duration.TotalSeconds > 0, "Span should have a positive duration");
        var actualEnd = span.StartTimeUtc + span.Duration;
        var diffMs = Math.Abs((actualEnd - expectedEnd).TotalMilliseconds);
        Assert.IsTrue(diffMs < 1.0, $"Span end time should be within 1 ms of expected, diff={diffMs:F3} ms");
    }

    [TestMethod]
    public void CreateSpans_BaseUtcOverride_IsRespected()
    {
        long freq = Timestamp.Frequency;
        var customBase = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var session = BuildSession(
            startTs: 0,
            endTs: freq,
            new TraceEvent(1, 1, 0,        TraceEventKind.Begin, 0, 0),
            new TraceEvent(1, 1, freq / 2, TraceEventKind.End,   0, 0));

        var opts = new OpenTelemetryExportOptions { BaseUtc = customBase };
        var spans = OpenTelemetryExport.CreateSpans(session, Meta(1, "Op"), opts);

        Assert.AreEqual(customBase.UtcDateTime, spans[0].StartTimeUtc);
    }

    [TestMethod]
    public void CreateSpans_NestedScopes_InnerSpanParentIsOuter()
    {
        long freq = Timestamp.Frequency;
        long t0 = 0;
        long t1 = freq / 10;
        long t2 = freq * 3/10;
        long t3 = freq * 4/10;

        var meta = new DictionaryTraceMetadataProvider();
        meta.Add(1, "outer");
        meta.Add(2, "inner");

        var session = BuildSession(
            new TraceEvent(1, 1, t0, TraceEventKind.Begin, 0, 0),
            new TraceEvent(2, 1, t1, TraceEventKind.Begin, 0, 0),
            new TraceEvent(2, 1, t2, TraceEventKind.End,   0, 0),
            new TraceEvent(1, 1, t3, TraceEventKind.End,   0, 0));

        var spans = OpenTelemetryExport.CreateSpans(session, meta, Opts());

        Assert.AreEqual(2, spans.Count);

        var inner = spans.Single(s => s.DisplayName == "inner");
        var outer = spans.Single(s => s.DisplayName == "outer");

        Assert.AreEqual(outer.SpanId, inner.ParentSpanId,
            "Nested span should have outer span as parent");
    }

    [TestMethod]
    public void CreateSpans_MultipleRoots_NoParent()
    {
        long freq = Timestamp.Frequency;
        var meta = new DictionaryTraceMetadataProvider();
        meta.Add(1, "A");
        meta.Add(2, "B");

        var session = BuildSession(
            new TraceEvent(1, 1, 0,    TraceEventKind.Begin, 0, 0),
            new TraceEvent(1, 1, freq, TraceEventKind.End,   0, 0),
            new TraceEvent(2, 2, 0,    TraceEventKind.Begin, 0, 0),
            new TraceEvent(2, 2, freq, TraceEventKind.End,   0, 0));

        var spans = OpenTelemetryExport.CreateSpans(session, meta, Opts());

        Assert.AreEqual(2, spans.Count);
        foreach (var span in spans)
            Assert.AreEqual(default(ActivitySpanId), span.ParentSpanId,
                $"Root span '{span.DisplayName}' should have no parent");
    }

    [TestMethod]
    public void CreateSpans_UnclosedSpan_AutoClosedAtSessionEnd()
    {
        long freq = Timestamp.Frequency;
        long endTs = freq * 2;

        var session = BuildSession(
            startTs: 0,
            endTs: endTs,
            new TraceEvent(1, 1, 0, TraceEventKind.Begin, 0, 0));

        var opts = new OpenTelemetryExportOptions { BaseUtc = FixedBase };
        var spans = OpenTelemetryExport.CreateSpans(session, Meta(1, "Leak"), opts);

        Assert.AreEqual(1, spans.Count, "Unclosed span should still be emitted");
        Assert.AreEqual("Leak", spans[0].DisplayName);

        var expectedEnd = FixedBase.UtcDateTime + TimeSpan.FromSeconds(endTs / (double)freq);
        var actualEnd = spans[0].StartTimeUtc + spans[0].Duration;
        var diffMs = Math.Abs((actualEnd - expectedEnd).TotalMilliseconds);
        Assert.IsTrue(diffMs < 1.0, $"Unclosed span end time should be within 1 ms of session end, diff={diffMs:F3} ms");
    }

    [TestMethod]
    public void CreateSpans_IncludeFlowsAsLinks_FlowEventsAddLinksToCurrentSpan()
    {
        long freq = Timestamp.Frequency;
        const long flowId = 42L;

        var session = BuildSession(
            new TraceEvent(1, 1, 0,        TraceEventKind.Begin,     0,      0),
            new TraceEvent(2, 1, freq / 2, TraceEventKind.FlowStart, flowId, 0),
            new TraceEvent(1, 1, freq,     TraceEventKind.End,       0,      0));

        var opts = new OpenTelemetryExportOptions { IncludeFlowsAsLinks = true };
        var spans = OpenTelemetryExport.CreateSpans(session, Meta(1, "Span"), opts);

        Assert.AreEqual(1, spans.Count);
        Assert.AreEqual(1, spans[0].Links.Count(),
            "FlowStart inside scope should add one ActivityLink");
    }

    [TestMethod]
    public void CreateSpans_IncludeFlowsAsLinksFalse_FlowEventsIgnored()
    {
        long freq = Timestamp.Frequency;
        const long flowId = 42L;

        var session = BuildSession(
            new TraceEvent(1, 1, 0,        TraceEventKind.Begin,     0,      0),
            new TraceEvent(2, 1, freq / 2, TraceEventKind.FlowStart, flowId, 0),
            new TraceEvent(1, 1, freq,     TraceEventKind.End,       0,      0));

        var opts = new OpenTelemetryExportOptions { IncludeFlowsAsLinks = false };
        var spans = OpenTelemetryExport.CreateSpans(session, Meta(1, "Span"), opts);

        Assert.AreEqual(0, spans[0].Links.Count(),
            "Flow events should be ignored when IncludeFlowsAsLinks=false");
    }

    [TestMethod]
    public void CreateSpans_ThreadIdTagPresent_ByDefault()
    {
        long freq = Timestamp.Frequency;
        const int threadId = 7;

        var session = BuildSession(
            new TraceEvent(1, threadId, 0,    TraceEventKind.Begin, 0, 0),
            new TraceEvent(1, threadId, freq, TraceEventKind.End,   0, 0));

        var spans = OpenTelemetryExport.CreateSpans(session, Meta(1, "Op"),
            new OpenTelemetryExportOptions { IncludeThreadIdTag = true });

        var threadTag = spans[0].TagObjects.FirstOrDefault(t => t.Key == "thread.id");
        Assert.IsNotNull(threadTag.Key, "thread.id tag should be present when IncludeThreadIdTag=true");
        Assert.AreEqual(threadId, threadTag.Value);
    }

    [TestMethod]
    public void CreateSpans_IncludeThreadIdTagFalse_TagAbsent()
    {
        long freq = Timestamp.Frequency;

        var session = BuildSession(
            new TraceEvent(1, 1, 0,    TraceEventKind.Begin, 0, 0),
            new TraceEvent(1, 1, freq, TraceEventKind.End,   0, 0));

        var spans = OpenTelemetryExport.CreateSpans(session, Meta(1, "Op"),
            new OpenTelemetryExportOptions { IncludeThreadIdTag = false });

        var threadTag = spans[0].TagObjects.FirstOrDefault(t => t.Key == "thread.id");
        Assert.IsNull(threadTag.Key, "thread.id tag should be absent when IncludeThreadIdTag=false");
    }

    [TestMethod]
    public void CreateSpans_CategoryPresentInMeta_AddsCategoryTag()
    {
        long freq = Timestamp.Frequency;
        var meta = new DictionaryTraceMetadataProvider();
        meta.Add(1, "Fetch", "Network");

        var session = BuildSession(
            new TraceEvent(1, 1, 0,    TraceEventKind.Begin, 0, 0),
            new TraceEvent(1, 1, freq, TraceEventKind.End,   0, 0));

        var spans = OpenTelemetryExport.CreateSpans(session, meta, Opts());

        var catTag = spans[0].Tags.FirstOrDefault(t => t.Key == "embertrace.category");
        Assert.AreEqual("Network", catTag.Value);
    }

    [TestMethod]
    public void Export_NullCallback_ThrowsArgumentNullException()
    {
        var session = BuildSession();
        bool threw = false;
        try { OpenTelemetryExport.Export(session, null!); }
        catch (ArgumentNullException) { threw = true; }
        Assert.IsTrue(threw, "Expected ArgumentNullException for null onSpan callback");
    }

    [TestMethod]
    public void Export_CallsCallbackForEachSpan()
    {
        long freq = Timestamp.Frequency;
        var session = BuildSession(
            new TraceEvent(1, 1, 0,    TraceEventKind.Begin, 0, 0),
            new TraceEvent(1, 1, freq, TraceEventKind.End,   0, 0),
            new TraceEvent(2, 1, 0,    TraceEventKind.Begin, 0, 0),
            new TraceEvent(2, 1, freq, TraceEventKind.End,   0, 0));

        var received = new List<Activity>();
        OpenTelemetryExport.Export(session, a => received.Add(a), Meta(1, "A", 2, "B"), Opts());

        Assert.AreEqual(2, received.Count);
    }

    private static TraceSession BuildSession(params TraceEvent[] events) =>
        BuildSession(startTs: 0, endTs: Timestamp.Frequency, events);

    private static TraceSession BuildSession(long startTs, long endTs, params TraceEvent[] events)
    {
        Chunk? chunk = null;
        if (events.Length > 0)
        {
            chunk = new Chunk(events.Length);
            foreach (var e in events)
                chunk.TryWrite(e);
        }

        IReadOnlyList<Chunk> chunks = chunk is null
            ? Array.Empty<Chunk>()
            : new[] { chunk };

        return new TraceSession(
            chunks,
            startTimestamp: startTs,
            endTimestamp: endTs,
            options: new SessionOptions(),
            threadNames: new Dictionary<int, string>(),
            droppedEvents: 0,
            droppedChunks: 0,
            sampledOutEvents: 0,
            wasOverflow: false);
    }

    private static OpenTelemetryExportOptions Opts() =>
        new() { BaseUtc = FixedBase };

    private static DictionaryTraceMetadataProvider Meta(int id, string name)
    {
        var p = new DictionaryTraceMetadataProvider();
        p.Add(id, name);
        return p;
    }

    private static DictionaryTraceMetadataProvider Meta(int id1, string name1, int id2, string name2)
    {
        var p = new DictionaryTraceMetadataProvider();
        p.Add(id1, name1);
        p.Add(id2, name2);
        return p;
    }
}
