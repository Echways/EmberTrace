using EmberTrace.Sessions;

namespace EmberTrace.Tests.ReportText;

[TestClass]
public class CollapsedStackTests
{
    [TestMethod]
    public void NestedScopes_ProduceOneLinePerPathWithExclusiveMicroseconds()
    {
        var script = new TraceScript().Begin(1, 0).Span(2, 1_000, 5_000).End(1, 10_000);

        Assert.AreEqual("Outer 6000\nOuter;Inner 4000\n", Collapse(script, (1, "Outer"), (2, "Inner")));
    }

    [TestMethod]
    public void SamePathOnDifferentThreads_IsMerged()
    {
        var script = new TraceScript().Span(1, 0, 1_000).Span(1, 0, 3_000, 2);

        Assert.AreEqual("Work 4000\n", Collapse(script, (1, "Work")));
    }

    [TestMethod]
    public void FrameNames_CannotBreakTheFormat()
    {
        var script = new TraceScript().Span(1, 0, 1_000);

        Assert.AreEqual("a:b  c 1000\n", Collapse(script, (1, "a;b\r\nc")));
    }

    [TestMethod]
    public void FullyCoveredParent_IsOmitted()
    {
        var script = new TraceScript().Begin(1, 0).Span(2, 0, 2_000).End(1, 2_000);

        Assert.AreEqual("Outer;Inner 2000\n", Collapse(script, (1, "Outer"), (2, "Inner")));
    }

    [TestMethod]
    public void WithoutMetadata_FramesAreNamedByTheirId()
    {
        using var writer = new StringWriter();

        TraceText.WriteCollapsedStacks(new TraceScript().Span(42, 0, 500).ToSession().Process(), writer);

        Assert.AreEqual("42 500\n", writer.ToString());
    }

    [TestMethod]
    public void NullArguments_Throw()
    {
        var trace = new TraceScript().ToSession().Process();

        Assert.ThrowsExactly<ArgumentNullException>(() => TraceText.WriteCollapsedStacks(null!, TextWriter.Null));
        Assert.ThrowsExactly<ArgumentNullException>(() => TraceText.WriteCollapsedStacks(trace, null!));
    }

    [TestMethod]
    public void OpenScopesInASnapshot_AreWeighedUpToTheCut()
    {
        var session = new TraceScript().Begin(1, 0).Span(2, 1_000, 2_000).ToSession(end: 5_000, isSnapshot: true);

        Assert.AreEqual("Outer 4000\nOuter;Inner 1000\n", Collapse(session, (1, "Outer"), (2, "Inner")));
    }

    [TestMethod]
    public void OpenAsyncScopesInASnapshot_CloseInnermostFirst()
    {
        var session = new TraceScript()
            .AsyncBegin(1, 0, 10)
            .AsyncBegin(2, 1_000, 11, 10)
            .ToSession(end: 5_000, isSnapshot: true);

        Assert.AreEqual("Request 1000\nRequest;Db 4000\n", Collapse(session, (1, "Request"), (2, "Db")));
    }

    [TestMethod]
    public void OpenScopesInAStoppedSession_AreLeftOut()
    {
        var session = new TraceScript().Begin(1, 0).Span(2, 1_000, 2_000).ToSession(end: 5_000);

        Assert.AreEqual("Outer;Inner 1000\n", Collapse(session, (1, "Outer"), (2, "Inner")));
    }

    [TestMethod]
    public void ConcurrentAsyncChildren_AreEachWeighedByTheirOwnTime()
    {
        var script = new TraceScript()
            .AsyncBegin(1, 0, 10)
            .AsyncBegin(2, 0, 11, 10).AsyncEnd(2, 4_000, 11, 10)
            .AsyncBegin(3, 0, 12, 10).AsyncEnd(3, 3_000, 12, 10)
            .AsyncEnd(1, 4_000, 10);

        Assert.AreEqual("Fan;A 4000\nFan;B 3000\n", Collapse(script, (1, "Fan"), (2, "A"), (3, "B")));
    }

    private static string Collapse(TraceScript script, params (int Id, string Name)[] names)
    {
        return Collapse(script.ToSession(), names);
    }

    private static string Collapse(TraceSession session, params (int Id, string Name)[] names)
    {
        using var writer = new StringWriter();
        TraceText.WriteCollapsedStacks(session.Process(), writer,
            Meta.Of(names.Select(n => (n.Id, n.Name, (string?)null)).ToArray()));
        return writer.ToString();
    }
}
