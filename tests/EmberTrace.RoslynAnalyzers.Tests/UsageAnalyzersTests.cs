namespace EmberTrace.RoslynAnalyzers.Tests;

[TestClass]
public class UsageAnalyzersTests
{
    [TestMethod]
    [DataRow("var scope = Tracer.Scope(1);", 1)]
    [DataRow("Tracer.Scope(1);", 1)]
    [DataRow("var a = Tracer.Scope(1); var b = Tracer.Scope(2);", 2)]
    [DataRow("using (var f = new System.IO.MemoryStream()) { var a = Tracer.Scope(1); var b = Tracer.Scope(2); }", 2)]
    [DataRow("var ts = new TracingSession(); var scope = ts.Scope(1);", 1)]
    [DataRow("using var scope = Tracer.Scope(1);", 0)]
    [DataRow("using (Tracer.Scope(1)) { }", 0)]
    [DataRow("using (var scope = Tracer.Scope(1)) { }", 0)]
    [DataRow("var ts = new TracingSession(); using var scope = ts.Scope(1);", 0)]
    public async Task ETA001_ReportsScopesThatAreNotDisposedByUsing(string body, int expected)
    {
        await AssertDiagnostics(UsageAnalyzers.ScopeNotDisposedId, expected, "void M() { " + body + " }");
    }

    [TestMethod]
    [DataRow("var scope = Tracer.ScopeAsync(1);", 1)]
    [DataRow("using var scope = Tracer.ScopeAsync(1);", 1)]
    [DataRow("using (Tracer.ScopeAsync(1)) { }", 1)]
    [DataRow("await using (var f = new System.IO.MemoryStream()) { var scope = Tracer.ScopeAsync(1); }", 1)]
    [DataRow("var ts = new TracingSession(); var scope = ts.ScopeAsync(1);", 1)]
    [DataRow("await using var scope = Tracer.ScopeAsync(1);", 0)]
    [DataRow("await using (Tracer.ScopeAsync(1)) { }", 0)]
    [DataRow("var ts = new TracingSession(); await using var scope = ts.ScopeAsync(1);", 0)]
    public async Task ETA002_ReportsAsyncScopesThatAreNotAwaitUsing(string body, int expected)
    {
        await AssertDiagnostics(UsageAnalyzers.AsyncScopeNotAwaitedId, expected,
            "async System.Threading.Tasks.Task M() { " + body + " }");
    }

    [TestMethod]
    [DataRow("var handle = Tracer.FlowStartNewHandle(1);", 1)]
    [DataRow("FlowHandle handle; handle = Tracer.FlowStartNewHandle(1);", 1)]
    [DataRow("var ts = new TracingSession(); var handle = ts.FlowStartNewHandle(1);", 1)]
    [DataRow("var h1 = Tracer.FlowStartNewHandle(1); var h2 = Tracer.FlowStartNewHandle(2); h1.End();", 1)]
    [DataRow("var handle = Tracer.FlowStartNewHandle(1); handle.End();", 0)]
    [DataRow("var handle = Tracer.FlowStartNewHandle(1); handle.TryEnd();", 0)]
    [DataRow("var ts = new TracingSession(); FlowHandle handle = ts.FlowStartNewHandle(1); handle.End();", 0)]
    public async Task ETA003_ReportsFlowHandlesThatAreNeverEnded(string body, int expected)
    {
        await AssertDiagnostics(UsageAnalyzers.FlowHandleNotEndedId, expected, "void M() { " + body + " }");
    }

    [TestMethod]
    public async Task ETA001_IsReportedAtTheScopeInvocation()
    {
        var diagnostic = (await AnalyzerTestHost.DiagnosticsAsync(new UsageAnalyzers(),
            Source("void M() { var scope = Tracer.Scope(1); }"))).Single();

        Assert.AreEqual("Tracer.Scope(1)", diagnostic.Location.SourceTree!.GetText()
            .ToString(diagnostic.Location.SourceSpan));
    }

    [TestMethod]
    [DataRow("class C { }")]
    [DataRow("class Tracer { public static System.IDisposable Scope(int id) => null!; void M() { var s = Scope(1); } }")]
    public async Task UnrelatedCode_ProducesNoDiagnostics(string code)
    {
        Assert.IsEmpty(await AnalyzerTestHost.DiagnosticsAsync(new UsageAnalyzers(), "namespace Other; " + code));
    }

    private static async Task AssertDiagnostics(string id, int expected, string method)
    {
        var diagnostics = await AnalyzerTestHost.DiagnosticsAsync(new UsageAnalyzers(), Source(method));

        Assert.HasCount(expected, diagnostics.Where(d => d.Id == id));
        Assert.IsTrue(diagnostics.All(d => d.Id == id));
    }

    private static string Source(string method)
    {
        return "using EmberTrace;\nusing EmberTrace.Flow;\nclass C\n{\n" + method + "\n}";
    }
}
