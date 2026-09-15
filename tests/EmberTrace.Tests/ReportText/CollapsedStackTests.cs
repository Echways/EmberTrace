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

    private static string Collapse(TraceScript script, params (int Id, string Name)[] names)
    {
        using var writer = new StringWriter();
        TraceText.WriteCollapsedStacks(script.ToSession().Process(), writer,
            Meta.Of(names.Select(n => (n.Id, n.Name, (string?)null)).ToArray()));
        return writer.ToString();
    }
}
