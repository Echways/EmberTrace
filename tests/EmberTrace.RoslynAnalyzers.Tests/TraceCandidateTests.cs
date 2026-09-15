using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace EmberTrace.RoslynAnalyzers.Tests;

[TestClass]
public class TraceCandidateTests
{
    [TestMethod]
    [DataRow("public int M(int a) { using var scope = Tracer.Scope(1); return a; }")]
    [DataRow("public async Task<int> M(int a) { await using var scope = Tracer.ScopeAsync(1); await Task.Yield(); return a; }")]
    [DataRow("public static void M() { using var scope = Tracer.Scope(1); }")]
    public async Task MethodOpeningWithAManualScope_ReportsETA004OnItsName(string method)
    {
        var diagnostic = (await Diagnostics(method)).Single();

        Assert.AreEqual(TraceCandidateAnalyzer.TraceCandidateId, diagnostic.Id);
        Assert.AreEqual("M", diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan));
        Assert.Contains("'M'", diagnostic.GetMessage());
    }

    [TestMethod]
    [DataRow("public int M(int a) => a;")]
    [DataRow("public void M() { }")]
    [DataRow("public int M(int a) { var x = a; using var scope = Tracer.Scope(1); return x; }")]
    [DataRow("public void M() { using (Tracer.Scope(1)) { } }")]
    [DataRow("public void M() { using var stream = new System.IO.MemoryStream(); }")]
    [DataRow("public void M() { var ts = new TracingSession(); using var scope = ts.Scope(1); }")]
    [DataRow("public unsafe int M(int* a) { using var scope = Tracer.Scope(1); return *a; }")]
    [DataRow("public int M(int a) { using var scope = Tracer.Scope(1); return MCore(a); } private int MCore(int a) => a;")]
    [DataRow("[Trace] public partial int M(int a); private int MCore(int a) { using var scope = Tracer.Scope(1); return a; }")]
    public async Task MethodsThatCannotOrNeedNotBecomePartial_ReportNothing(string members)
    {
        Assert.IsEmpty(await Diagnostics(members));
    }

    [TestMethod]
    public async Task ExplicitInterfaceImplementation_ReportsNothing()
    {
        Assert.IsEmpty(await AnalyzerTestHost.DiagnosticsAsync(new TraceCandidateAnalyzer(), """
            using System;
            using EmberTrace;

            public class C : IDisposable
            {
                void IDisposable.Dispose()
                {
                    using var scope = Tracer.Scope(1);
                }
            }
            """));
    }

    [TestMethod]
    [DataRow("public int M(int a) { using var scope = Tracer.Scope(1); return a; }",
        "public partial int M(int a);", "private int MCore(int a)")]
    [DataRow("int M(int a) { using var scope = Tracer.Scope(1); return a; }",
        "private partial int M(int a);", "private int MCore(int a)")]
    [DataRow("public static int M(int a) { using var scope = Tracer.Scope(1); return a; }",
        "public static partial int M(int a);", "private static int MCore(int a)")]
    [DataRow("internal async Task<int> M(int a) { await using var scope = Tracer.ScopeAsync(1); await Task.Yield(); return a; }",
        "internal partial Task<int> M(int a);", "private async Task<int> MCore(int a)")]
    public async Task Fix_SplitsTheMethodIntoATracedDeclarationAndACoreThatCompiles(
        string method, string expectedDeclaration, string expectedCore)
    {
        var fixedSource = await AnalyzerTestHost.ApplyFixAsync(
            new TraceCandidateAnalyzer(), new TraceCandidateCodeFixProvider(), Source(method));

        StringAssert.Contains(fixedSource, "[Trace]");
        StringAssert.Contains(fixedSource, expectedDeclaration);
        StringAssert.Contains(fixedSource, expectedCore);
        Assert.DoesNotContain("Tracer.Scope", fixedSource);
        AnalyzerTestHost.AssertCompilesWithTheGenerator(fixedSource);
    }

    private static Task<ImmutableArray<Diagnostic>> Diagnostics(string members)
    {
        return AnalyzerTestHost.DiagnosticsAsync(new TraceCandidateAnalyzer(), Source(members));
    }

    private static string Source(string members)
    {
        return "using System.Threading.Tasks;\nusing EmberTrace;\nusing EmberTrace.Abstractions.Attributes;\n"
               + "public partial class C\n{\n" + members + "\n}";
    }
}
