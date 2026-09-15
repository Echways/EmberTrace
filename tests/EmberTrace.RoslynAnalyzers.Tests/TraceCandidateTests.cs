using System.Collections.Immutable;
using EmberTrace.Abstractions.Attributes;
using EmberTrace.Generator.Generator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace EmberTrace.RoslynAnalyzers.Tests;

[TestClass]
public class TraceCandidateTests
{
    [TestMethod]
    public async Task MethodOpeningWithAManualScope_ReportsETA004()
    {
        var diagnostics = await GetDiagnosticsAsync("""
                                                    using EmberTrace;

                                                    public class C
                                                    {
                                                        public int M(int a)
                                                        {
                                                            using var scope = Tracer.Scope(1);
                                                            return a;
                                                        }
                                                    }
                                                    """);

        Assert.IsTrue(diagnostics.Any(d => d.Id == "ETA004"));
    }

    [TestMethod]
    public async Task MethodWithoutAScope_ReportsNothing()
    {
        var diagnostics = await GetDiagnosticsAsync("""
                                                    public class C
                                                    {
                                                        public int M(int a) => a;
                                                    }
                                                    """);

        Assert.IsEmpty(diagnostics.Where(d => d.Id == "ETA004"));
    }

    [TestMethod]
    public async Task MethodAlreadyPartial_ReportsNothing()
    {
        var diagnostics = await GetDiagnosticsAsync("""
                                                    using EmberTrace;
                                                    using EmberTrace.Abstractions.Attributes;

                                                    public partial class C
                                                    {
                                                        [Trace]
                                                        public partial int M(int a);

                                                        private int MCore(int a)
                                                        {
                                                            using var scope = Tracer.Scope(1);
                                                            return a;
                                                        }
                                                    }
                                                    """);

        Assert.IsEmpty(diagnostics.Where(d => d.Id == "ETA004"));
    }

    [TestMethod]
    public async Task Fix_SplitsTheMethodIntoAPartialDeclarationAndACore()
    {
        var fixedSource = await ApplyFixAsync("""
                                              using EmberTrace;

                                              public partial class C
                                              {
                                                  public int M(int a)
                                                  {
                                                      using var scope = Tracer.Scope(1);
                                                      return a;
                                                  }
                                              }
                                              """, "ETA004");

        StringAssert.Contains(fixedSource, "[Trace]");
        StringAssert.Contains(fixedSource, "public partial int M(int a);");
        StringAssert.Contains(fixedSource, "private int MCore(int a)");
        Assert.DoesNotContain("using var scope = Tracer.Scope(1);", fixedSource);
    }

    [TestMethod]
    public async Task UnsafeMethod_ReportsNothing()
    {
        var diagnostics = await GetDiagnosticsAsync("""
                                                    using EmberTrace;

                                                    public class C
                                                    {
                                                        public unsafe int M(int* a)
                                                        {
                                                            using var scope = Tracer.Scope(1);
                                                            return *a;
                                                        }
                                                    }
                                                    """);

        Assert.IsEmpty(diagnostics.Where(d => d.Id == "ETA004"));
    }

    [TestMethod]
    public async Task ExplicitInterfaceImplementation_ReportsNothing()
    {
        var diagnostics = await GetDiagnosticsAsync("""
                                                    using System;
                                                    using EmberTrace;

                                                    public class C : IDisposable
                                                    {
                                                        void IDisposable.Dispose()
                                                        {
                                                            using var scope = Tracer.Scope(1);
                                                        }
                                                    }
                                                    """);

        Assert.IsEmpty(diagnostics.Where(d => d.Id == "ETA004"));
    }

    [TestMethod]
    public async Task MethodWhoseCoreNameIsTaken_ReportsNothing()
    {
        var diagnostics = await GetDiagnosticsAsync("""
                                                    using EmberTrace;

                                                    public class C
                                                    {
                                                        public int M(int a)
                                                        {
                                                            using var scope = Tracer.Scope(1);
                                                            return MCore(a);
                                                        }

                                                        private int MCore(int a) => a;
                                                    }
                                                    """);

        Assert.IsEmpty(diagnostics.Where(d => d.Id == "ETA004"));
    }

    [TestMethod]
    public async Task Fix_OnAnAsyncMethod_CompilesWithTheGenerator()
    {
        var fixedSource = await ApplyFixAsync("""
                                              using System.Threading.Tasks;
                                              using EmberTrace;
                                              using EmberTrace.Abstractions.Attributes;

                                              public partial class C
                                              {
                                                  public async Task<int> M(int a)
                                                  {
                                                      await using var scope = Tracer.ScopeAsync(1);
                                                      await Task.Yield();
                                                      return a;
                                                  }
                                              }
                                              """, "ETA004");

        StringAssert.Contains(fixedSource, "public partial Task<int> M(int a);");
        StringAssert.Contains(fixedSource, "private async Task<int> MCore(int a)");
        AssertCompilesWithTheGenerator(fixedSource);
    }

    [TestMethod]
    public async Task Fix_OnAStaticMethod_KeepsTheCoreStatic()
    {
        var fixedSource = await ApplyFixAsync("""
                                              using EmberTrace;
                                              using EmberTrace.Abstractions.Attributes;

                                              public partial class C
                                              {
                                                  public static int M(int a)
                                                  {
                                                      using var scope = Tracer.Scope(1);
                                                      return a;
                                                  }
                                              }
                                              """, "ETA004");

        StringAssert.Contains(fixedSource, "private static int MCore(int a)");
        AssertCompilesWithTheGenerator(fixedSource);
    }

    [TestMethod]
    public async Task Fix_OnAMethodWithoutAccessibility_DeclaresItPrivate()
    {
        var fixedSource = await ApplyFixAsync("""
                                              using EmberTrace;
                                              using EmberTrace.Abstractions.Attributes;

                                              public partial class C
                                              {
                                                  int M(int a)
                                                  {
                                                      using var scope = Tracer.Scope(1);
                                                      return a;
                                                  }
                                              }
                                              """, "ETA004");

        StringAssert.Contains(fixedSource, "private partial int M(int a);");
        AssertCompilesWithTheGenerator(fixedSource);
    }

    private static void AssertCompilesWithTheGenerator(string source)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);
        var compilation = CSharpCompilation.Create(
            "Fixed",
            [CSharpSyntaxTree.ParseText(source, parseOptions)],
            BuildReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        CSharpGeneratorDriver
            .Create([new TraceMetadataGenerator().AsSourceGenerator()], parseOptions: parseOptions)
            .RunGeneratorsAndUpdateCompilation(compilation, out var updated, out _);

        var errors = updated.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();

        Assert.IsEmpty(errors, string.Join("\n", errors) + "\n--- fixed source ---\n" + source);
    }

    private static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(string code)
    {
        var parseOptions = CSharpParseOptions.Default
            .WithLanguageVersion(LanguageVersion.Latest);

        var tree = CSharpSyntaxTree.ParseText(code, parseOptions);

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [tree],
            BuildReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var compilationWithAnalyzers = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(new TraceCandidateAnalyzer()),
            new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty));

        return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
    }

    private static async Task<string> ApplyFixAsync(string code, string diagnosticId)
    {
        using var workspace = new AdhocWorkspace();

        var projectId = ProjectId.CreateNewId();
        var documentId = DocumentId.CreateNewId(projectId);

        var solution = workspace.CurrentSolution
            .AddProject(projectId, "TestProject", "TestProject", LanguageNames.CSharp)
            .WithProjectCompilationOptions(projectId, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .WithProjectParseOptions(projectId, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest))
            .AddMetadataReferences(projectId, BuildReferences())
            .AddDocument(documentId, "Test.cs", SourceText.From(code));

        var document = solution.GetDocument(documentId)!;
        var compilation = await document.Project.GetCompilationAsync();

        var diagnostics = await compilation!
            .WithAnalyzers(
                ImmutableArray.Create<DiagnosticAnalyzer>(new TraceCandidateAnalyzer()),
                new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty))
            .GetAnalyzerDiagnosticsAsync();

        var diagnostic = diagnostics.Single(d => d.Id == diagnosticId);

        var actions = new List<CodeAction>();
        await new TraceCandidateCodeFixProvider().RegisterCodeFixesAsync(
            new CodeFixContext(document, diagnostic, (action, _) => actions.Add(action), CancellationToken.None));

        Assert.HasCount(1, actions, "Exactly one fix should be offered");

        var operations = await actions[0].GetOperationsAsync(CancellationToken.None);
        var changed = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution;

        return (await changed.GetDocument(documentId)!.GetTextAsync()).ToString();
    }

    private static IReadOnlyList<MetadataReference> BuildReferences()
    {
        var refs = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(Tracer).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(TraceAttribute).Assembly.Location)
        };

        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string tpa)
            foreach (var path in tpa.Split(Path.PathSeparator))
                if (File.Exists(path))
                    refs.Add(MetadataReference.CreateFromFile(path));

        return refs;
    }
}
