using System.Globalization;
using EmberTrace.Analysis.Compare;

namespace EmberTrace.Tests.ReportText;

[TestClass]
public class ScopeTableTests
{
    private const string Header =
        "run,arm,round,id,name,count,total_ms,mean_ms,min_ms,p25_ms,p50_ms,p75_ms,p95_ms,p99_ms,max_ms,exact,bimodal,overflow,dropped";

    [TestMethod]
    public void WriteScopeTable_Header_IsExact()
    {
        var lines = Write([]).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.AreEqual(Header, lines.Single());
    }

    [TestMethod]
    public void WriteScopeTable_WritesOneRowPerRunAndScope()
    {
        var runs = new[] { Run("r1", "A", 1), Run("r2", "B", 2) };

        var lines = Write(runs).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.HasCount(1 + 2 * 2, lines);
        Assert.StartsWith("r1,A,1,", lines[1]);
        Assert.StartsWith("r2,B,2,", lines[3]);
    }

    [TestMethod]
    public void WriteScopeTable_RowCarriesTheStatistics()
    {
        var fields = Fields(Write([Run("r1", "A", 1)]), 1);

        Assert.AreEqual("1", fields[3]);
        Assert.AreEqual("4", fields[5]);
        Assert.AreEqual("6", fields[6]);
        Assert.AreEqual("1.5", fields[7]);
        Assert.AreEqual("1", fields[8]);
        Assert.AreEqual("2", fields[14]);
        Assert.AreEqual("true", fields[15]);
        Assert.AreEqual("false", fields[16]);
        Assert.AreEqual("false", fields[17]);
        Assert.AreEqual("0", fields[18]);
    }

    [TestMethod]
    public void NameWithCommaAndQuote_IsEscaped()
    {
        var meta = Meta.Of((1, "load,\"fast\"", null));

        var text = Write([Run("r1", "A", 1)], meta);

        StringAssert.Contains(text, ",\"load,\"\"fast\"\"\",");
    }

    [TestMethod]
    public void LabelWithNewline_IsQuoted()
    {
        var text = Write([Run("line1\nline2", "A", 1)]);

        StringAssert.Contains(text, "\"line1\nline2\",A,1,");
    }

    [TestMethod]
    public void Culture_RuRu_StillUsesDotDecimal()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("ru-RU");
        try
        {
            var fields = Fields(Write([Run("r1", "A", 1)]), 1);

            Assert.AreEqual("1.5", fields[7]);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [TestMethod]
    public void NonExactScope_WritesEmptyPercentileShapeColumns()
    {
        var session = new TraceScript().Span(1, 0, 1_000).Span(1, 2_000, 4_000).ToSession();
        var run = TraceRun.FromSession(session, "r1", "A", 1) with { Stats = session.Analyze(maxSamplesPerId: 0) };

        var fields = Fields(Write([run]), 1);

        Assert.AreEqual(string.Empty, fields[9]);
        Assert.AreEqual(string.Empty, fields[11]);
        Assert.AreEqual(string.Empty, fields[16]);
        Assert.AreNotEqual(string.Empty, fields[10]);
        Assert.AreEqual("false", fields[15]);
    }

    [TestMethod]
    public void OverflowAndDrops_AreWritten()
    {
        var run = Run("r1", "A", 1) with { WasOverflow = true, DroppedEvents = 12 };

        var fields = Fields(Write([run]), 1);

        Assert.AreEqual("true", fields[17]);
        Assert.AreEqual("12", fields[18]);
    }

    [TestMethod]
    public void RunMetadata_IsUsedWhenNoProviderIsPassed()
    {
        var session = new TraceScript().Span(1, 0, 1_000).ToSession(metadata: Meta.Of((1, "Named", null)));

        var text = Write([TraceRun.FromSession(session, "r1", "A", 1)]);

        StringAssert.Contains(text, ",1,Named,");
    }

    [TestMethod]
    public void WriteScopeTable_NullArguments_Throw()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => TraceText.WriteScopeTable(null!, new StringWriter()));
        Assert.ThrowsExactly<ArgumentNullException>(() => TraceText.WriteScopeTable([], null!));
    }

    private static string Write(IEnumerable<TraceRun> runs, EmberTrace.Metadata.ITraceMetadataProvider? meta = null)
    {
        var writer = new StringWriter();
        TraceText.WriteScopeTable(runs, writer, meta);
        return writer.ToString();
    }

    private static string[] Fields(string text, int row)
    {
        return text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries)[row].Split(',');
    }

    private static TraceRun Run(string label, string arm, int round)
    {
        var session = new TraceScript()
            .Span(1, 0, 1_000)
            .Span(1, 2_000, 4_000)
            .Span(1, 5_000, 6_000)
            .Span(1, 7_000, 9_000)
            .Span(2, 10_000, 10_500)
            .ToSession();

        return TraceRun.FromSession(session, label, arm, round);
    }
}