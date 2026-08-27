using System.Collections.Immutable;
using EmberTrace.Abstractions.Attributes;
using EmberTrace.Generator.Generator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace EmberTrace.Generator.Tests;

internal sealed record GeneratorOutput(
    IReadOnlyDictionary<string, string> Sources,
    ImmutableArray<Diagnostic> Diagnostics)
{
    internal string Source(string hintName)
    {
        Assert.IsTrue(Sources.ContainsKey(hintName),
            $"Expected generated source '{hintName}', got [{string.Join(", ", Sources.Keys)}]");
        return Sources[hintName];
    }

    internal string SourceEndingWith(string suffix)
    {
        var matches = Sources.Where(pair => pair.Key.EndsWith(suffix, StringComparison.Ordinal)).ToList();
        Assert.AreEqual(1, matches.Count,
            $"Expected exactly one generated source ending with '{suffix}', got [{string.Join(", ", Sources.Keys)}]");
        return matches[0].Value;
    }
}

internal static class GeneratorTestHost
{
    internal static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);

    private static readonly IReadOnlyList<MetadataReference> References = BuildReferences();

    internal static GeneratorOutput Run(string code, bool generateTraceIds = false)
    {
        var result = Driver(generateTraceIds).RunGenerators(Compile(code)).GetRunResult().Results[0];

        return new GeneratorOutput(
            result.GeneratedSources.ToDictionary(s => s.HintName, s => s.SourceText.ToString(), StringComparer.Ordinal),
            result.Diagnostics);
    }

    internal static GeneratorOutput RunAndCompile(string code, params string[] ignoredDiagnosticIds)
    {
        var driver = Driver(false)
            .RunGeneratorsAndUpdateCompilation(Compile(code), out var updated, out _);

        var result = driver.GetRunResult().Results[0];

        var problems = updated.GetDiagnostics()
            .Where(d => d.Severity >= DiagnosticSeverity.Warning)
            .Where(d => !ignoredDiagnosticIds.Contains(d.Id))
            .ToList();

        Assert.IsEmpty(problems,
            "Generated code must compile without errors or warnings:\n"
            + string.Join("\n", problems.Select(d => d.ToString()))
            + "\n--- generated ---\n"
            + string.Join("\n", result.GeneratedSources.Select(s => s.HintName + ":\n" + s.SourceText)));

        return new GeneratorOutput(
            result.GeneratedSources.ToDictionary(s => s.HintName, s => s.SourceText.ToString(), StringComparer.Ordinal),
            result.Diagnostics);
    }

    internal static GeneratorDriver Driver(bool generateTraceIds)
    {
        return CSharpGeneratorDriver.Create(
            [new TraceMetadataGenerator().AsSourceGenerator()],
            optionsProvider: new TestOptionsProvider(generateTraceIds),
            parseOptions: ParseOptions,
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, true));
    }

    internal static CSharpCompilation Compile(params string[] sources)
    {
        return CSharpCompilation.Create(
            "TestAssembly",
            sources.Select(source => CSharpSyntaxTree.ParseText(source, ParseOptions)),
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
    }

    private static IReadOnlyList<MetadataReference> BuildReferences()
    {
        var refs = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(TraceIdAttribute).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Tracer).Assembly.Location)
        };

        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string tpa)
            foreach (var path in tpa.Split(Path.PathSeparator))
                if (File.Exists(path))
                    refs.Add(MetadataReference.CreateFromFile(path));

        return refs;
    }

    private sealed class TestOptionsProvider(bool generateTraceIds) : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions { get; } = new Options(generateTraceIds);

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree)
        {
            return GlobalOptions;
        }

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile)
        {
            return GlobalOptions;
        }

        private sealed class Options(bool generateTraceIds) : AnalyzerConfigOptions
        {
            public override bool TryGetValue(string key, out string value)
            {
                value = generateTraceIds.ToString();
                return key == "build_property.EmberTraceGenerateTraceIds";
            }
        }
    }
}
