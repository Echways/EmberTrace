using System.Diagnostics;
using EmberTrace.OpenTelemetry;
using EmberTrace.Sessions;

namespace EmberTrace.Tests.Export;

[TestClass]
public class OpenTelemetryExportTests
{
    private const long Second = 1_000_000;

    private static readonly DateTimeOffset BaseUtc = new(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void NullArguments_Throw()
    {
        var session = new TraceScript().ToSession();

        Assert.ThrowsExactly<ArgumentNullException>(() => OpenTelemetryExport.CreateSpans(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => OpenTelemetryExport.Export(session, null!));
    }

    [TestMethod]
    public void SessionWithoutScopes_ProducesNoSpans()
    {
        var session = new TraceScript()
            .Instant(1, 0)
            .Counter(2, Second, 99)
            .Flow(TraceEventKind.FlowStart, 3, Second, 42)
            .ToSession();

        Assert.IsEmpty(OpenTelemetryExport.CreateSpans(session, options: Options()));
    }

    [TestMethod]
    public void Span_CarriesNameTimingAndIdentityTags()
    {
        var session = new TraceScript().Span(1, Second / 2, Second * 2, 7).ToSession(end: Second * 3);

        var span = OpenTelemetryExport.CreateSpans(session, Meta.Of((1, "Fetch", "Network")), Options()).Single();

        Assert.AreEqual("Fetch", span.DisplayName);
        Assert.AreEqual(BaseUtc.UtcDateTime.AddMilliseconds(500), span.StartTimeUtc);
        Assert.AreEqual(TimeSpan.FromSeconds(1.5), span.Duration);
        Assert.AreEqual(ActivityIdFormat.W3C, span.IdFormat);
        Assert.AreNotEqual(default, span.SpanId);
        Assert.AreEqual(default, span.ParentSpanId);
        Assert.AreEqual(1, span.GetTagItem("embertrace.id"));
        Assert.AreEqual("Network", span.GetTagItem("embertrace.category"));
        Assert.AreEqual(7, span.GetTagItem("thread.id"));
        Assert.IsNull(span.GetTagItem("embertrace.async_scope_id"));
    }

    [TestMethod]
    public void UnknownIdWithoutCategory_IsNamedByTheIdAndCarriesNoCategoryTag()
    {
        var session = new TraceScript().Span(12345, 0, Second).ToSession();

        var span = OpenTelemetryExport.CreateSpans(session, options: Options()).Single();

        Assert.AreEqual("12345", span.DisplayName);
        Assert.IsNull(span.GetTagItem("embertrace.category"));
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void IncludeThreadIdTag_ControlsTheThreadTag(bool include)
    {
        var session = new TraceScript().Span(1, 0, Second, 7).ToSession();

        var span = OpenTelemetryExport.CreateSpans(session,
            options: new OpenTelemetryExportOptions { BaseUtc = BaseUtc, IncludeThreadIdTag = include }).Single();

        Assert.AreEqual(include ? 7 : null, span.GetTagItem("thread.id"));
    }

    [TestMethod]
    public void NestedScopes_ShareTheTraceAndPointToTheirParent()
    {
        var session = new TraceScript()
            .Begin(1, 0)
            .Span(2, Second / 10, Second * 3 / 10)
            .End(1, Second * 4 / 10)
            .Span(3, 0, Second, 2)
            .ToSession();

        var spans = OpenTelemetryExport.CreateSpans(session, Meta.Of((1, "outer", null), (2, "inner", null),
            (3, "other", null)), Options());

        var outer = spans.Single(s => s.DisplayName == "outer");
        var inner = spans.Single(s => s.DisplayName == "inner");
        var other = spans.Single(s => s.DisplayName == "other");

        Assert.HasCount(3, spans);
        Assert.AreEqual(outer.SpanId, inner.ParentSpanId);
        Assert.AreEqual(outer.TraceId, inner.TraceId);
        Assert.AreEqual(default, outer.ParentSpanId);
        Assert.AreEqual(default, other.ParentSpanId);
        Assert.AreNotEqual(outer.TraceId, other.TraceId);
    }

    [TestMethod]
    public void UnclosedScope_EndsAtTheSessionEnd()
    {
        var session = new TraceScript().Begin(1, Second).ToSession(end: Second * 3);

        var span = OpenTelemetryExport.CreateSpans(session, options: Options()).Single();

        Assert.AreEqual(BaseUtc.UtcDateTime.AddSeconds(1), span.StartTimeUtc);
        Assert.AreEqual(TimeSpan.FromSeconds(2), span.Duration);
    }

    [TestMethod]
    [DataRow(true, 2)]
    [DataRow(false, 0)]
    public void FlowEvents_BecomeLinksOnTheEnclosingSpanOfTheirTrack(bool include, int expectedLinks)
    {
        var session = new TraceScript()
            .Begin(1, 0)
            .Flow(TraceEventKind.FlowStart, 9, Second / 4, 42)
            .Flow(TraceEventKind.FlowStep, 9, Second / 2, 42)
            .End(1, Second)
            .Span(2, 0, Second, 2)
            .Flow(TraceEventKind.FlowEnd, 9, Second * 2, 42)
            .ToSession();

        var spans = OpenTelemetryExport.CreateSpans(session,
            options: new OpenTelemetryExportOptions { BaseUtc = BaseUtc, IncludeFlowsAsLinks = include });

        var links = spans.Single(s => s.DisplayName == "1").Links.ToArray();

        Assert.HasCount(expectedLinks, links);
        Assert.IsEmpty(spans.Single(s => s.DisplayName == "2").Links);
        Assert.HasCount(expectedLinks == 0 ? 0 : 1, links.Select(l => l.Context.TraceId).Distinct());
    }

    [TestMethod]
    public void WithoutBaseUtc_AnchorsOnTheRecordedStart()
    {
        var startedAt = new DateTimeOffset(2025, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var session = TraceSession.FromEvents(
            new TraceScript().Span(1, Second, Second * 2).ToSession().SortedEvents(),
            0, Second * 2, Second, null, null, 0, 0, 0, false, null, false, startedAt);

        var span = OpenTelemetryExport.CreateSpans(session).Single();

        Assert.AreEqual(startedAt.UtcDateTime.AddSeconds(1), span.StartTimeUtc);
    }

    [TestMethod]
    public void CreateSpans_LeavesTheAmbientActivityUntouchedAndOutOfTheExportedTrace()
    {
        var session = new TraceScript().Begin(1, 0).Span(2, Second / 4, Second / 2).End(1, Second).ToSession();

        Activity.Current = null;
        using var ambient = new Activity("ambient");
        ambient.SetIdFormat(ActivityIdFormat.W3C);
        ambient.Start();

        var spans = OpenTelemetryExport.CreateSpans(session, options: Options());

        Assert.AreSame(ambient, Activity.Current);
        Assert.IsTrue(spans.All(span => span.TraceId != ambient.TraceId));
    }

    [TestMethod]
    public void Export_InvokesTheCallbackForEverySpanInOrder()
    {
        var session = new TraceScript().Span(1, 0, Second).Span(2, Second, Second * 2).ToSession();

        var received = new List<string>();
        OpenTelemetryExport.Export(session, span => received.Add(span.DisplayName), options: Options());

        CollectionAssert.AreEqual(new[] { "1", "2" }, received);
    }

    private static OpenTelemetryExportOptions Options()
    {
        return new OpenTelemetryExportOptions { BaseUtc = BaseUtc };
    }
}
