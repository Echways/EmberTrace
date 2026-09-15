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

internal static class AnalyzerTestHost
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);

    private static readonly IReadOnlyList<MetadataReference> References = BuildReferences();

    public static async Task<ImmutableArray<Diagnostic>> DiagnosticsAsync(DiagnosticAnalyzer analyzer, string code)
    {
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [CSharpSyntaxTree.ParseText(code, ParseOptions)],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));

        return await compilation
            .WithAnalyzers([analyzer], new AnalyzerOptions([]))
            .GetAnalyzerDiagnosticsAsync();
    }

    public static async Task<string> ApplyFixAsync(DiagnosticAnalyzer analyzer, CodeFixProvider fix, string code)
    {
        using var workspace = new AdhocWorkspace();

        var projectId = ProjectId.CreateNewId();
        var documentId = DocumentId.CreateNewId(projectId);

        var solution = workspace.CurrentSolution
            .AddProject(projectId, "TestProject", "TestProject", LanguageNames.CSharp)
            .WithProjectCompilationOptions(projectId, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .WithProjectParseOptions(projectId, ParseOptions)
            .AddMetadataReferences(projectId, References)
            .AddDocument(documentId, "Test.cs", SourceText.From(code));

        var document = solution.GetDocument(documentId)!;
        var compilation = await document.Project.GetCompilationAsync();
        var diagnostic = (await compilation!
                .WithAnalyzers([analyzer], new AnalyzerOptions([]))
                .GetAnalyzerDiagnosticsAsync())
            .Single(d => fix.FixableDiagnosticIds.Contains(d.Id));

        var actions = new List<CodeAction>();
        await fix.RegisterCodeFixesAsync(
            new CodeFixContext(document, diagnostic, (action, _) => actions.Add(action), CancellationToken.None));

        Assert.HasCount(1, actions);

        var operations = await actions[0].GetOperationsAsync(CancellationToken.None);
        var changed = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution;

        return (await changed.GetDocument(documentId)!.GetTextAsync()).ToString();
    }

    public static void AssertCompilesWithTheGenerator(string source)
    {
        var compilation = CSharpCompilation.Create(
            "Fixed",
            [CSharpSyntaxTree.ParseText(source, ParseOptions)],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        CSharpGeneratorDriver
            .Create([new TraceMetadataGenerator().AsSourceGenerator()], parseOptions: ParseOptions)
            .RunGeneratorsAndUpdateCompilation(compilation, out var updated, out _);

        var errors = updated.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();

        Assert.IsEmpty(errors, string.Join("\n", errors) + "\n--- source ---\n" + source);
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
