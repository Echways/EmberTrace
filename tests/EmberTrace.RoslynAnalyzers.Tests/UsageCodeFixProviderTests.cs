using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EmberTrace.RoslynAnalyzers.Tests;

[TestClass]
public class UsageCodeFixProviderTests
{
    [TestMethod]
    public async Task ETA001_LocalDeclaration_GetsUsingKeyword()
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

        var fixedCode = await ApplyFixAsync(code, UsageAnalyzers.ScopeNotDisposedId);

        StringAssert.Contains(fixedCode, "using var scope = Tracer.Scope(1);");
    }

    [TestMethod]
    public async Task ETA002_LocalDeclaration_GetsAwaitUsingKeyword()
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

        var fixedCode = await ApplyFixAsync(code, UsageAnalyzers.AsyncScopeNotAwaitedId);

        StringAssert.Contains(fixedCode, "await using var scope = Tracer.ScopeAsync(1);");
    }

    [TestMethod]
    public async Task ETA002_UsingDeclaration_GainsAwaitOnly()
    {
        const string code = """
            using EmberTrace;
            using System.Threading.Tasks;
            class C
            {
                async Task M()
                {
                    using var scope = Tracer.ScopeAsync(1);
                }
            }
            """;

        var fixedCode = await ApplyFixAsync(code, UsageAnalyzers.AsyncScopeNotAwaitedId);

        StringAssert.Contains(fixedCode, "await using var scope = Tracer.ScopeAsync(1);");
    }

    [TestMethod]
    public async Task ETA001_BareInvocation_WrapsRestOfBlock()
    {
        const string code = """
            using EmberTrace;
            using System;
            class C
            {
                void M()
                {
                    Console.WriteLine("before");
                    Tracer.Scope(1);
                    Console.WriteLine("inside");
                    Console.WriteLine("also inside");
                }
            }
            """;

        var fixedCode = await ApplyFixAsync(code, UsageAnalyzers.ScopeNotDisposedId);

        StringAssert.Contains(fixedCode, "using (Tracer.Scope(1))");

        var usingIndex = fixedCode.IndexOf("using (Tracer.Scope(1))", StringComparison.Ordinal);
        Assert.IsLessThan(usingIndex, fixedCode.IndexOf("\"before\"", StringComparison.Ordinal), "Preceding statements stay outside the scope");
        Assert.IsGreaterThan(usingIndex, fixedCode.IndexOf("\"inside\"", StringComparison.Ordinal), "Following statements move inside the scope");
        Assert.IsGreaterThan(usingIndex, fixedCode.IndexOf("\"also inside\"", StringComparison.Ordinal), "Every following statement moves inside the scope");
    }

    [TestMethod]
    public async Task ETA001_BareInvocation_KeepsCodeCompilable()
    {
        const string code = """
            using EmberTrace;
            class C
            {
                void M()
                {
                    Tracer.Scope(1);
                }
            }
            """;

        var fixedCode = await ApplyFixAsync(code, UsageAnalyzers.ScopeNotDisposedId);

        var tree = CSharpSyntaxTree.ParseText(fixedCode);
        Assert.IsEmpty(tree.GetDiagnostics().ToArray(), fixedCode);
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
                ImmutableArray.Create<DiagnosticAnalyzer>(new UsageAnalyzers()),
                new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty))
            .GetAnalyzerDiagnosticsAsync();

        var diagnostic = diagnostics.Single(d => d.Id == diagnosticId);

        var actions = new List<CodeAction>();
        await new UsageCodeFixProvider().RegisterCodeFixesAsync(
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
            MetadataReference.CreateFromFile(typeof(Tracer).Assembly.Location)
        };

        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string tpa)
        {
            foreach (var path in tpa.Split(Path.PathSeparator))
            {
                if (File.Exists(path))
                    refs.Add(MetadataReference.CreateFromFile(path));
            }
        }

        return refs;
    }
}
