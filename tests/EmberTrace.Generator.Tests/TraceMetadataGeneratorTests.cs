using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using EmberTrace.Abstractions.Attributes;
using EmberTrace.Generator.Generator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EmberTrace.Generator.Tests;

[TestClass]
public class TraceMetadataGeneratorTests
{
    [TestMethod]
    public void AssemblyAttributes_ProduceMetadataProvider()
    {
        var result = Run("""
            using EmberTrace.Abstractions.Attributes;
            [assembly: TraceId(2000, "Worker", "Workers")]
            [assembly: TraceId(1000, "App", "App")]
            """);

        var provider = result.Source("EmberTrace.GeneratedTraceMetadataProvider.g.cs");

        StringAssert.Contains(provider, @"[1000] = new global::EmberTrace.Metadata.TraceMeta(1000, @""App"", @""App"")");
        StringAssert.Contains(provider, @"[2000] = new global::EmberTrace.Metadata.TraceMeta(2000, @""Worker"", @""Workers"")");
    }

    [TestMethod]
    public void NoTraceIds_EmitsNothing()
    {
        var result = Run("class C { }");

        Assert.IsEmpty(result.Sources, "An assembly without trace metadata must not carry a provider or module initializer");
    }

    [TestMethod]
    public void ConstantNames_AreOrderedByIdNotByDeclaration()
    {
        const string declaredAscending = """
            using EmberTrace.Abstractions.Attributes;
            [assembly: TraceId(1, "Io Wait")]
            [assembly: TraceId(2, "Cpu")]
            """;

        const string declaredDescending = """
            using EmberTrace.Abstractions.Attributes;
            [assembly: TraceId(2, "Cpu")]
            [assembly: TraceId(1, "Io Wait")]
            """;

        Assert.AreEqual(
            Run(declaredAscending, generateTraceIds: true).Source("TraceIds.g.cs"),
            Run(declaredDescending, generateTraceIds: true).Source("TraceIds.g.cs"),
            "Declaration order must not rename generated constants");
    }

    [TestMethod]
    public void ClashingNormalizedNames_ReportETG006AndKeepLowestIdUnsuffixed()
    {
        var result = Run("""
            using EmberTrace.Abstractions.Attributes;
            [assembly: TraceId(1, "Io Wait")]
            [assembly: TraceId(2, "Io-Wait")]
            """, generateTraceIds: true);

        var traceIds = result.Source("TraceIds.g.cs");

        StringAssert.Contains(traceIds, "public const int IoWait = 1;");
        StringAssert.Contains(traceIds, "public const int IoWait_2 = 2;");
        Assert.HasCount(1, result.Diagnostics.Where(d => d.Id == "ETG006").ToArray());
    }

    [TestMethod]
    public void MalformedArgument_ReportsETG004AndKeepsGenerating()
    {
        var result = Run("""
            using EmberTrace.Abstractions.Attributes;
            [assembly: TraceId("x", "Broken")]
            [assembly: TraceId(1000, "App")]
            """);

        Assert.HasCount(1, result.Diagnostics.Where(d => d.Id == "ETG004").ToArray());
        StringAssert.Contains(result.Source("EmberTrace.GeneratedTraceMetadataProvider.g.cs"), "[1000] =");
    }

    [TestMethod]
    public void DuplicateId_ReportsETG001()
    {
        var result = Run("""
            using EmberTrace.Abstractions.Attributes;
            [assembly: TraceId(7, "First")]
            [assembly: TraceId(7, "Second")]
            """);

        Assert.HasCount(1, result.Diagnostics.Where(d => d.Id == "ETG001").ToArray());
    }

    [TestMethod]
    public void ConstFieldAttributes_ContributeMetadata()
    {
        var result = Run("""
            using EmberTrace.Abstractions.Attributes;
            static class Ids
            {
                [TraceName("Cpu Work")]
                [TraceCategory("CPU")]
                public const int Cpu = 10;

                [TraceCategory("IO")]
                public const int IoWait = 20;
            }
            """);

        var provider = result.Source("EmberTrace.GeneratedTraceMetadataProvider.g.cs");

        StringAssert.Contains(provider, @"[10] = new global::EmberTrace.Metadata.TraceMeta(10, @""Cpu Work"", @""CPU"")");
        StringAssert.Contains(provider, @"[20] = new global::EmberTrace.Metadata.TraceMeta(20, @""IoWait"", @""IO"")");
    }

    [TestMethod]
    public void ConstFieldAttributes_DoNotProduceTraceIdConstants()
    {
        var result = Run("""
            using EmberTrace.Abstractions.Attributes;
            static class Ids
            {
                [TraceName("Cpu Work")]
                public const int Cpu = 10;
            }
            """, generateTraceIds: true);

        Assert.IsFalse(result.Sources.ContainsKey("TraceIds.g.cs"), "Fields already are the constants");
    }

    [TestMethod]
    public void NonConstantField_ReportsETG005()
    {
        var result = Run("""
            using EmberTrace.Abstractions.Attributes;
            static class Ids
            {
                [TraceName("Cpu Work")]
                public static int Cpu = 10;
            }
            """);

        Assert.HasCount(1, result.Diagnostics.Where(d => d.Id == "ETG005").ToArray());
    }

    [TestMethod]
    public void EditingUnrelatedFile_KeepsGeneratorOutputCached()
    {
        const string attributes = """
            using EmberTrace.Abstractions.Attributes;
            [assembly: TraceId(1000, "App", "App")]
            """;

        var compilation = Compile(attributes, "class Unrelated { }");
        var driver = Driver(generateTraceIds: false).RunGenerators(compilation);

        var edited = compilation.ReplaceSyntaxTree(
            compilation.SyntaxTrees.Last(),
            CSharpSyntaxTree.ParseText("class Unrelated { int Added; }", ParseOptions));

        foreach (var reason in OutputStepReasons(driver.RunGenerators(edited)))
            Assert.AreEqual(IncrementalStepRunReason.Cached, reason);
    }

    [TestMethod]
    public void EditingTraceIdDeclarations_RegeneratesOutput()
    {
        var compilation = Compile("""
            using EmberTrace.Abstractions.Attributes;
            [assembly: TraceId(1000, "App", "App")]
            """);

        var driver = Driver(generateTraceIds: false).RunGenerators(compilation);

        var edited = compilation.ReplaceSyntaxTree(
            compilation.SyntaxTrees[0],
            CSharpSyntaxTree.ParseText("""
                using EmberTrace.Abstractions.Attributes;
                [assembly: TraceId(1000, "Application", "App")]
                """, ParseOptions));

        Assert.Contains(IncrementalStepRunReason.Modified, OutputStepReasons(driver.RunGenerators(edited)));
    }

    private static IReadOnlyList<IncrementalStepRunReason> OutputStepReasons(GeneratorDriver driver)
        => driver.GetRunResult()
            .Results[0]
            .TrackedOutputSteps.SelectMany(kvp => kvp.Value)
            .SelectMany(step => step.Outputs)
            .Select(output => output.Reason)
            .ToArray();

    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);

    private static GeneratorOutput Run(string code, bool generateTraceIds = false)
    {
        var result = Driver(generateTraceIds).RunGenerators(Compile(code)).GetRunResult().Results[0];

        return new GeneratorOutput(
            result.GeneratedSources.ToDictionary(s => s.HintName, s => s.SourceText.ToString(), StringComparer.Ordinal),
            result.Diagnostics);
    }

    private static GeneratorDriver Driver(bool generateTraceIds)
        => CSharpGeneratorDriver.Create(
            generators: [new TraceMetadataGenerator().AsSourceGenerator()],
            optionsProvider: new TestOptionsProvider(generateTraceIds),
            parseOptions: ParseOptions,
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));

    private static CSharpCompilation Compile(params string[] sources)
        => CSharpCompilation.Create(
            "TestAssembly",
            sources.Select(source => CSharpSyntaxTree.ParseText(source, ParseOptions)),
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    private static readonly IReadOnlyList<MetadataReference> References = BuildReferences();

    private static IReadOnlyList<MetadataReference> BuildReferences()
    {
        var refs = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(TraceIdAttribute).Assembly.Location)
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

    private sealed record GeneratorOutput(IReadOnlyDictionary<string, string> Sources, ImmutableArray<Diagnostic> Diagnostics)
    {
        internal string Source(string hintName)
        {
            Assert.IsTrue(Sources.ContainsKey(hintName), $"Expected generated source '{hintName}', got [{string.Join(", ", Sources.Keys)}]");
            return Sources[hintName];
        }
    }

    private sealed class TestOptionsProvider(bool generateTraceIds) : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions { get; } = new Options(generateTraceIds);

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => GlobalOptions;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => GlobalOptions;

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
