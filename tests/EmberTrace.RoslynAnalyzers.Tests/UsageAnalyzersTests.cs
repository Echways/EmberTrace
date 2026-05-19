using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using EmberTrace.RoslynAnalyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EmberTrace.RoslynAnalyzers.Tests;

[TestClass]
public class UsageAnalyzersTests
{
    [TestMethod]
    public async Task ETA001_ScopeAssignedWithoutUsing_ReportsWarning()
    {
        const string code = """
            using EmberTrace;
            class C
            {
                void M()
                {
                    var scope = Tracer.Scope(1);
                }
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(code);

        AssertDiagnostic(diagnostics, UsageAnalyzers.ScopeNotDisposedId, count: 1);
    }

    [TestMethod]
    public async Task ETA001_ScopeWithUsingDeclaration_NoDiagnostic()
    {
        const string code = """
            using EmberTrace;
            class C
            {
                void M()
                {
                    using var scope = Tracer.Scope(1);
                }
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(code);

        AssertNoDiagnostic(diagnostics, UsageAnalyzers.ScopeNotDisposedId);
    }

    [TestMethod]
    public async Task ETA001_ScopeWithUsingStatement_NoDiagnostic()
    {
        const string code = """
            using EmberTrace;
            class C
            {
                void M()
                {
                    using (Tracer.Scope(1)) { }
                }
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(code);

        AssertNoDiagnostic(diagnostics, UsageAnalyzers.ScopeNotDisposedId);
    }

    [TestMethod]
    public async Task ETA001_MultipleScopesWithoutUsing_ReportsOneWarningEach()
    {
        const string code = """
            using EmberTrace;
            class C
            {
                void M()
                {
                    var a = Tracer.Scope(1);
                    var b = Tracer.Scope(2);
                }
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(code);

        AssertDiagnostic(diagnostics, UsageAnalyzers.ScopeNotDisposedId, count: 2);
    }

    [TestMethod]
    public async Task ETA002_ScopeAsyncWithoutAwaitUsing_ReportsWarning()
    {
        const string code = """
            using EmberTrace;
            using System.Threading.Tasks;
            class C
            {
                async Task M()
                {
                    var scope = Tracer.ScopeAsync(1);
                }
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(code);

        AssertDiagnostic(diagnostics, UsageAnalyzers.AsyncScopeNotAwaitedId, count: 1);
    }

    [TestMethod]
    public async Task ETA002_ScopeAsyncWithUsingButNotAwait_ReportsWarning()
    {
        const string code = """
            using EmberTrace;
            using System.Threading.Tasks;
            class C
            {
                void M()
                {
                    using var scope = Tracer.ScopeAsync(1);
                }
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(code);

        AssertDiagnostic(diagnostics, UsageAnalyzers.AsyncScopeNotAwaitedId, count: 1);
    }

    [TestMethod]
    public async Task ETA002_ScopeAsyncWithAwaitUsing_NoDiagnostic()
    {
        const string code = """
            using EmberTrace;
            using System.Threading.Tasks;
            class C
            {
                async Task M()
                {
                    await using var scope = Tracer.ScopeAsync(1);
                }
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(code);

        AssertNoDiagnostic(diagnostics, UsageAnalyzers.AsyncScopeNotAwaitedId);
    }

    [TestMethod]
    public async Task ETA002_ScopeAsyncWithAwaitUsingStatement_NoDiagnostic()
    {
        const string code = """
            using EmberTrace;
            using System.Threading.Tasks;
            class C
            {
                async Task M()
                {
                    await using (Tracer.ScopeAsync(1)) { }
                }
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(code);

        AssertNoDiagnostic(diagnostics, UsageAnalyzers.AsyncScopeNotAwaitedId);
    }

    [TestMethod]
    public async Task ETA003_FlowHandleWithoutEndOrTryEnd_ReportsWarning()
    {
        const string code = """
            using EmberTrace;
            class C
            {
                void M()
                {
                    var handle = Tracer.FlowStartNewHandle(1);
                }
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(code);

        AssertDiagnostic(diagnostics, UsageAnalyzers.FlowHandleNotEndedId, count: 1);
    }

    [TestMethod]
    public async Task ETA003_FlowHandleWithEnd_NoDiagnostic()
    {
        const string code = """
            using EmberTrace;
            using EmberTrace.Flow;
            class C
            {
                void M()
                {
                    FlowHandle handle = Tracer.FlowStartNewHandle(1);
                    handle.End();
                }
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(code);

        AssertNoDiagnostic(diagnostics, UsageAnalyzers.FlowHandleNotEndedId);
    }

    [TestMethod]
    public async Task ETA003_FlowHandleWithTryEnd_NoDiagnostic()
    {
        const string code = """
            using EmberTrace;
            using EmberTrace.Flow;
            class C
            {
                void M()
                {
                    FlowHandle handle = Tracer.FlowStartNewHandle(1);
                    handle.TryEnd();
                }
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(code);

        AssertNoDiagnostic(diagnostics, UsageAnalyzers.FlowHandleNotEndedId);
    }

    [TestMethod]
    public async Task ETA003_MultipleHandles_OnlyUnendedOnesReported()
    {
        const string code = """
            using EmberTrace;
            using EmberTrace.Flow;
            class C
            {
                void M()
                {
                    FlowHandle h1 = Tracer.FlowStartNewHandle(1);
                    FlowHandle h2 = Tracer.FlowStartNewHandle(2);
                    h1.End();
                }
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(code);

        var eta003 = diagnostics.Where(d => d.Id == UsageAnalyzers.FlowHandleNotEndedId).ToArray();
        Assert.HasCount(1, eta003, "Only the un-ended handle should be reported");
    }

    [TestMethod]
    public async Task ETA003_FlowHandleAssignedViaAssignment_Detected()
    {
        const string code = """
            using EmberTrace;
            using EmberTrace.Flow;
            class C
            {
                void M()
                {
                    FlowHandle handle;
                    handle = Tracer.FlowStartNewHandle(1);
                }
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(code);

        AssertDiagnostic(diagnostics, UsageAnalyzers.FlowHandleNotEndedId, count: 1);
    }

    [TestMethod]
    public async Task ETA001_TracingSession_ScopeWithoutUsing_ReportsWarning()
    {
        const string code = """
            using EmberTrace;
            class C
            {
                void M()
                {
                    var ts = new TracingSession();
                    var scope = ts.Scope(1);
                }
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(code);

        AssertDiagnostic(diagnostics, UsageAnalyzers.ScopeNotDisposedId, count: 1);
    }

    [TestMethod]
    public async Task ETA001_TracingSession_ScopeWithUsingDeclaration_NoDiagnostic()
    {
        const string code = """
            using EmberTrace;
            class C
            {
                void M()
                {
                    var ts = new TracingSession();
                    using var scope = ts.Scope(1);
                }
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(code);

        AssertNoDiagnostic(diagnostics, UsageAnalyzers.ScopeNotDisposedId);
    }

    [TestMethod]
    public async Task ETA002_TracingSession_ScopeAsyncWithoutAwaitUsing_ReportsWarning()
    {
        const string code = """
            using EmberTrace;
            using System.Threading.Tasks;
            class C
            {
                async Task M()
                {
                    var ts = new TracingSession();
                    var scope = ts.ScopeAsync(1);
                }
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(code);

        AssertDiagnostic(diagnostics, UsageAnalyzers.AsyncScopeNotAwaitedId, count: 1);
    }

    [TestMethod]
    public async Task ETA002_TracingSession_ScopeAsyncWithAwaitUsing_NoDiagnostic()
    {
        const string code = """
            using EmberTrace;
            using System.Threading.Tasks;
            class C
            {
                async Task M()
                {
                    var ts = new TracingSession();
                    await using var scope = ts.ScopeAsync(1);
                }
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(code);

        AssertNoDiagnostic(diagnostics, UsageAnalyzers.AsyncScopeNotAwaitedId);
    }

    [TestMethod]
    public async Task ETA003_TracingSession_FlowHandleWithoutEnd_ReportsWarning()
    {
        const string code = """
            using EmberTrace;
            class C
            {
                void M()
                {
                    var ts = new TracingSession();
                    var handle = ts.FlowStartNewHandle(1);
                }
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(code);

        AssertDiagnostic(diagnostics, UsageAnalyzers.FlowHandleNotEndedId, count: 1);
    }

    [TestMethod]
    public async Task ETA003_TracingSession_FlowHandleWithEnd_NoDiagnostic()
    {
        const string code = """
            using EmberTrace;
            using EmberTrace.Flow;
            class C
            {
                void M()
                {
                    var ts = new TracingSession();
                    FlowHandle handle = ts.FlowStartNewHandle(1);
                    handle.End();
                }
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(code);

        AssertNoDiagnostic(diagnostics, UsageAnalyzers.FlowHandleNotEndedId);
    }

    [TestMethod]
    public async Task NoFalsePositives_EmptyClass_NoDiagnostics()
    {
        const string code = """
            class C { }
            """;

        var diagnostics = await GetDiagnosticsAsync(code);
        Assert.IsEmpty(diagnostics);
    }

    [TestMethod]
    public async Task NoFalsePositives_AllPatternsCorrect_NoDiagnostics()
    {
        const string code = """
            using EmberTrace;
            using EmberTrace.Flow;
            using System.Threading.Tasks;
            class C
            {
                void Sync()
                {
                    using var scope = Tracer.Scope(1);
                    FlowHandle handle = Tracer.FlowStartNewHandle(2);
                    handle.End();
                }
                async Task Async()
                {
                    await using var scope = Tracer.ScopeAsync(3);
                }
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(code);
        Assert.IsEmpty(diagnostics);
    }

    private static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(string code)
    {
        var parseOptions = CSharpParseOptions.Default
            .WithLanguageVersion(LanguageVersion.Latest);

        var tree = CSharpSyntaxTree.ParseText(code, parseOptions);

        var references = BuildReferences();

        var compilation = CSharpCompilation.Create(
            assemblyName: "TestAssembly",
            syntaxTrees: [tree],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var analyzer = new UsageAnalyzers();
        var compilationWithAnalyzers = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(analyzer),
            new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty));

        return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
    }

    private static IReadOnlyList<MetadataReference> BuildReferences()
    {
        var refs = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(Tracer).Assembly.Location)
        };

        var tpa = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (!string.IsNullOrEmpty(tpa))
        {
            foreach (var path in tpa.Split(Path.PathSeparator))
            {
                if (File.Exists(path))
                    refs.Add(MetadataReference.CreateFromFile(path));
            }
        }

        return refs;
    }

    private static void AssertDiagnostic(
        ImmutableArray<Diagnostic> diagnostics, string id, int count)
    {
        var matched = diagnostics.Where(d => d.Id == id).ToArray();
        Assert.HasCount(count, matched,
            $"Expected {count} diagnostic(s) with id '{id}', " +
            $"but got {matched.Length}. All diagnostics: [{string.Join(", ", diagnostics.Select(d => d.Id))}]");
    }

    private static void AssertNoDiagnostic(ImmutableArray<Diagnostic> diagnostics, string id)
    {
        var matched = diagnostics.Where(d => d.Id == id).ToArray();
        Assert.IsEmpty(matched,
            $"Expected no diagnostics with id '{id}', but got {matched.Length}: " +
            string.Join("; ", matched.Select(d => d.GetMessage())));
    }
}
