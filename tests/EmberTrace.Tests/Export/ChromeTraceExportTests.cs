using System.Text.Json;
using EmberTrace.Sessions;

namespace EmberTrace.Tests.Export;

[TestClass]
public class ChromeTraceExportTests
{
    private const int Outer = 1;
    private const int Inner = 2;
    private const int Tick = 3;
    private const int Queue = 4;
    private const int Job = 5;
    private const long FlowId = 77;

    [TestMethod]
    public void WriteChromeComplete_EmitsNestedSpansWithDepthParentAndCategory()
    {
        var spans = Scopes(Complete(Session()));

        Assert.HasCount(2, spans);
        AssertEvent(spans[0], "Outer", "App", 1_000, 1);
        Assert.AreEqual(8_000.0, spans[0].GetProperty("dur").GetDouble());
        AssertArgs(spans[0], Outer, 0, null);

        AssertEvent(spans[1], "Inner", "", 2_000, 1);
        Assert.AreEqual(2_000.0, spans[1].GetProperty("dur").GetDouble());
        AssertArgs(spans[1], Inner, 1, Outer);
    }

    [TestMethod]
    public void WriteChromeComplete_EmitsInstantsCountersAndFlows()
    {
        var events = Complete(Session());

        var instant = events.Single(e => Phase(e) == "i");
        AssertEvent(instant, "Tick", "Marks", 2_500, 1);
        Assert.AreEqual("t", instant.GetProperty("s").GetString());

        var counter = events.Single(e => Phase(e) == "C");
        AssertEvent(counter, "Queue", "", 2_600, 1);
        Assert.AreEqual(42L, counter.GetProperty("args").GetProperty("value").GetInt64());

        var start = events.Single(e => Phase(e) == "s");
        AssertEvent(start, "Job", "", 3_000, 2);
        Assert.AreEqual(FlowId, start.GetProperty("id").GetInt64());
        Assert.AreEqual(Job, start.GetProperty("args").GetProperty("id").GetInt32());

        var end = events.Single(e => Phase(e) == "f");
        AssertEvent(end, "Job", "", 5_000, 1);
        Assert.AreEqual(FlowId, end.GetProperty("id").GetInt64());
    }

    [TestMethod]
    public void AsyncScope_IsScopedToTheProcess()
    {
        var session = new TraceScript().AsyncBegin(Outer, 1_000, 9).AsyncEnd(Outer, 5_000, 9, thread: 2).ToSession();

        foreach (var events in new[] { Complete(session), BeginEnd(session) })
        {
            var phases = events.Where(e => Phase(e) is "b" or "e").ToArray();

            Assert.HasCount(2, phases);
            Assert.IsTrue(phases.All(e => e.GetProperty("id2").GetProperty("local").GetInt64() == 9));
            Assert.IsFalse(phases.Any(e => e.TryGetProperty("id", out _)));
        }
    }

    [TestMethod]
    public void WriteChromeComplete_FlowOutsideAnySlice_IsAnchoredByAZeroDurationSliceWrittenRightBeforeIt()
    {
        var events = Complete(Session());
        var start = Array.FindIndex(events, e => Phase(e) == "s");
        var anchor = events[start - 1];

        Assert.AreEqual("X", Phase(anchor));
        AssertEvent(anchor, "Job", "", 3_000, 2);
        Assert.AreEqual(0.0, anchor.GetProperty("dur").GetDouble());
        Assert.AreEqual(Job, anchor.GetProperty("args").GetProperty("id").GetInt32());
        Assert.AreEqual(FlowId, anchor.GetProperty("args").GetProperty("flow").GetInt64());
    }

    [TestMethod]
    public void WriteChromeComplete_AnchorsAFlowUnlessTheLatestSliceOnItsTrackIsStillRunning()
    {
        var events = Complete(FlowSession());

        CollectionAssert.AreEqual(new[] { 5_000.0, 6_000.0, 8_000.0, 9_500.0 }, AnchorTimestamps(events));
        Assert.IsLessThan(
            Array.FindIndex(events, e => Phase(e) is "s" or "t" or "f"),
            Array.FindLastIndex(events, e => Phase(e) == "X" && !IsAnchor(e)));
    }

    [TestMethod]
    public void WriteChromeBeginEnd_AnchorsAFlowUnlessAnOpenSliceIsOnTopOfItsTrack()
    {
        CollectionAssert.AreEqual(new[] { 8_000.0, 9_500.0 }, AnchorTimestamps(BeginEnd(FlowSession())));
    }

    [TestMethod]
    public void WriteChromeComplete_Unsorted_AnchorsTheSameFlows()
    {
        var events = Parse(stream =>
            TraceExport.WriteChromeComplete(FlowSession(), stream, sortByStartTimestamp: false));

        CollectionAssert.AreEqual(new[] { 5_000.0, 6_000.0, 8_000.0, 9_500.0 }, AnchorTimestamps(events));
    }

    [TestMethod]
    public void FlowInsideAnUnclosedScope_IsAnchoredOnlyWhereTheScopeIsNotExported()
    {
        var session = new TraceScript()
            .Begin(Outer, 1_000)
            .Flow(TraceEventKind.FlowStart, Job, 2_000, FlowId)
            .ToSession(end: 5_000);

        CollectionAssert.AreEqual(new[] { 2_000.0 }, AnchorTimestamps(Complete(session)));
        Assert.IsEmpty(AnchorTimestamps(BeginEnd(session)));
    }

    [TestMethod]
    public void FlowEnd_BindsToTheEnclosingSlice()
    {
        foreach (var events in new[] { Complete(FlowSession()), BeginEnd(FlowSession()) })
        {
            Assert.AreEqual("e", events.Single(e => Phase(e) == "f").GetProperty("bp").GetString());
            Assert.IsFalse(events.Where(e => Phase(e) is "s" or "t").Any(e => e.TryGetProperty("bp", out _)));
        }
    }

    [TestMethod]
    public void WriteChromeComplete_NamesTheProcessAndEveryTrackIncludingTheSyntheticOne()
    {
        var events = Complete(Session(), pid: 42, processName: "svc");

        Assert.IsTrue(events.All(e => e.GetProperty("pid").GetInt32() == 42));
        Assert.AreEqual("process_name", events[0].GetProperty("name").GetString());
        Assert.AreEqual("svc", events[0].GetProperty("args").GetProperty("name").GetString());
        CollectionAssert.AreEqual(
            new[] { (0, "Thread 0"), (1, "main"), (2, "Thread 2") },
            ThreadNames(events));
    }

    [TestMethod]
    public void WriteChromeBeginEnd_EmitsEveryEventInTimestampOrder()
    {
        var events = BeginEnd(Session());

        CollectionAssert.AreEqual(new[] { (1, "main"), (2, "Thread 2") }, ThreadNames(events));
        CollectionAssert.AreEqual(
            new[]
            {
                ("B", "Outer", 1_000.0), ("B", "Inner", 2_000.0), ("i", "Tick", 2_500.0), ("C", "Queue", 2_600.0),
                ("X", "Job", 3_000.0), ("s", "Job", 3_000.0), ("E", "Inner", 4_000.0), ("f", "Job", 5_000.0),
                ("E", "Outer", 9_000.0)
            },
            events.Where(e => Phase(e) != "M")
                .Select(e => (Phase(e), e.GetProperty("name").GetString(), e.GetProperty("ts").GetDouble()))
                .ToArray());
    }

    [TestMethod]
    public void EventsBeforeTheSessionStart_AreSkippedAndTimestampsAreRelativeToIt()
    {
        var session = Session(start: 1_500);

        AssertEvent(Scopes(Complete(session)).Single(), "Inner", "", 500, 1);

        var scopes = BeginEnd(session).Where(e => Phase(e) is "B" or "E")
            .Select(e => (Phase(e), e.GetProperty("name").GetString()))
            .ToArray();
        CollectionAssert.AreEqual(new[] { ("B", "Inner"), ("E", "Inner"), ("E", "Outer") }, scopes);
    }

    [TestMethod]
    public void WithoutMetadata_EventsAreNamedByTheirId()
    {
        var session = new TraceScript().Span(9101, 1_000, 2_000).Instant(9102, 1_500).ToSession();

        var names = Complete(session).Where(e => Phase(e) != "M").Select(e => e.GetProperty("name").GetString());

        CollectionAssert.AreEquivalent(new[] { "9101", "9102" }, names.ToArray());
    }

    [TestMethod]
    public void ZeroDurationScopes_AreEmitted()
    {
        var script = new TraceScript();
        for (var i = 1; i <= 5; i++)
            script.Span(4242, i, i);

        var spans = Complete(script.ToSession()).Where(e => Phase(e) == "X").ToArray();

        Assert.HasCount(5, spans);
        Assert.IsTrue(spans.All(e => e.GetProperty("dur").GetDouble() == 0.0));
    }

    [TestMethod]
    public void UnclosedScope_IsLeftOutOfTheCompleteExport()
    {
        var session = new TraceScript().Span(Inner, 1_000, 2_000).Begin(Outer, 3_000).ToSession(end: 5_000);

        var spans = Complete(session).Where(e => Phase(e) == "X").ToArray();

        Assert.AreEqual(Inner, spans.Single().GetProperty("args").GetProperty("id").GetInt32());
    }

    private static TraceSession Session(long start = 0)
    {
        return new TraceScript()
            .Begin(Outer, 1_000)
            .Begin(Inner, 2_000)
            .Instant(Tick, 2_500)
            .Counter(Queue, 2_600, 42)
            .Flow(TraceEventKind.FlowStart, Job, 3_000, FlowId, 2)
            .End(Inner, 4_000)
            .Flow(TraceEventKind.FlowEnd, Job, 5_000, FlowId)
            .End(Outer, 9_000)
            .ToSession(start, 10_000,
                Meta.Of((Outer, "Outer", "App"), (Inner, "Inner", null), (Tick, "Tick", "Marks"), (Queue, "Queue", null),
                    (Job, "Job", null)),
                new Dictionary<int, string> { [1] = "main" });
    }

    private static TraceSession FlowSession()
    {
        return new TraceScript()
            .Begin(Outer, 1_000)
            .Flow(TraceEventKind.FlowStart, Job, 1_000, FlowId)
            .Begin(Inner, 2_000)
            .Flow(TraceEventKind.FlowStep, Job, 3_000, FlowId)
            .End(Inner, 4_000)
            .Flow(TraceEventKind.FlowStep, Job, 5_000, FlowId)
            .Flow(TraceEventKind.FlowStep, Job, 6_000, FlowId)
            .Instant(Tick, 7_000)
            .Flow(TraceEventKind.FlowStep, Job, 8_000, FlowId)
            .End(Outer, 9_000)
            .Flow(TraceEventKind.FlowEnd, Job, 9_500, FlowId)
            .ToSession();
    }

    private static JsonElement[] Scopes(JsonElement[] events)
    {
        return events.Where(e => Phase(e) == "X" && !IsAnchor(e)).ToArray();
    }

    private static double[] AnchorTimestamps(JsonElement[] events)
    {
        return events.Where(e => Phase(e) == "X" && IsAnchor(e))
            .Select(e => e.GetProperty("ts").GetDouble())
            .ToArray();
    }

    private static bool IsAnchor(JsonElement e)
    {
        return e.GetProperty("args").TryGetProperty("flow", out _);
    }

    private static JsonElement[] Complete(TraceSession session, int pid = 1, string processName = "EmberTrace")
    {
        return Parse(stream => TraceExport.WriteChromeComplete(session, stream, pid: pid, processName: processName));
    }

    private static JsonElement[] BeginEnd(TraceSession session)
    {
        return Parse(stream => TraceExport.WriteChromeBeginEnd(session, stream));
    }

    private static JsonElement[] Parse(Action<Stream> write)
    {
        using var stream = new MemoryStream();
        write(stream);
        stream.Position = 0;

        using var document = JsonDocument.Parse(stream);
        Assert.AreEqual("ms", document.RootElement.GetProperty("displayTimeUnit").GetString());

        return document.RootElement.GetProperty("traceEvents").EnumerateArray().Select(e => e.Clone()).ToArray();
    }

    private static string? Phase(JsonElement e)
    {
        return e.GetProperty("ph").GetString();
    }

    private static (int, string?)[] ThreadNames(JsonElement[] events)
    {
        return events
            .Where(e => e.GetProperty("name").GetString() == "thread_name")
            .Select(e => (e.GetProperty("tid").GetInt32(), e.GetProperty("args").GetProperty("name").GetString()))
            .ToArray();
    }

    private static void AssertEvent(JsonElement e, string name, string category, double ts, int tid)
    {
        Assert.AreEqual(name, e.GetProperty("name").GetString());
        Assert.AreEqual(category, e.GetProperty("cat").GetString());
        Assert.AreEqual(ts, e.GetProperty("ts").GetDouble());
        Assert.AreEqual(tid, e.GetProperty("tid").GetInt32());
    }

    private static void AssertArgs(JsonElement e, int id, int depth, int? parent)
    {
        var args = e.GetProperty("args");

        Assert.AreEqual(id, args.GetProperty("id").GetInt32());
        Assert.AreEqual(depth, args.GetProperty("depth").GetInt32());
        Assert.AreEqual(parent, args.TryGetProperty("parent", out var value) ? value.GetInt32() : null);
    }
}
