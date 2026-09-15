using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

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

        StringAssert.Contains(provider,
            @"[1000] = new global::EmberTrace.Metadata.TraceMeta(1000, @""App"", @""App"")");
        StringAssert.Contains(provider,
            @"[2000] = new global::EmberTrace.Metadata.TraceMeta(2000, @""Worker"", @""Workers"")");
    }

    [TestMethod]
    public void NoTraceIds_EmitsNothing()
    {
        var result = Run("class C { }");

        Assert.IsEmpty(result.Sources);
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
            Run(declaredAscending, true).Source("TraceIds.g.cs"),
            Run(declaredDescending, true).Source("TraceIds.g.cs"));
    }

    [TestMethod]
    public void ClashingNormalizedNames_ReportETG006AndKeepLowestIdUnsuffixed()
    {
        var result = Run("""
                         using EmberTrace.Abstractions.Attributes;
                         [assembly: TraceId(1, "Io Wait")]
                         [assembly: TraceId(2, "Io-Wait")]
                         """, true);

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
    [DataRow("[assembly: TraceId(7, \"First\")] [assembly: TraceId(7, \"Second\")]", "ETG001", 1)]
    [DataRow("[assembly: TraceId(7, \"Same\", \"C\")] [assembly: TraceId(7, \"Same\", \"C\")]", "ETG001", 0)]
    [DataRow("[assembly: TraceId(7, \" \")]", "ETG002", 1)]
    [DataRow("[assembly: TraceId(7, \"Name\", \"\")]", "ETG003", 1)]
    [DataRow("[assembly: TraceId(7, \"Name\", null)]", "ETG003", 0)]
    [DataRow("[assembly: TraceId(\"x\", \"Broken\")]", "ETG004", 1)]
    [DataRow("static class Ids { [TraceName(\"Cpu\")] public static int Cpu = 10; }", "ETG005", 1)]
    [DataRow("static class Ids { [TraceName(\"Cpu\")] public const int Cpu = 10; }", "ETG005", 0)]
    public void Declarations_ReportTheExpectedDiagnostics(string declarations, string id, int expected)
    {
        var diagnostics = Run("using EmberTrace.Abstractions.Attributes;\n" + declarations).Diagnostics;

        Assert.HasCount(expected, diagnostics.Where(d => d.Id == id));
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

        StringAssert.Contains(provider,
            @"[10] = new global::EmberTrace.Metadata.TraceMeta(10, @""Cpu Work"", @""CPU"")");
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
                         """, true);

        Assert.IsFalse(result.Sources.ContainsKey("TraceIds.g.cs"));
    }

    [TestMethod]
    public void TraceCategoryOnAType_IsIgnoredWithoutDiagnostics()
    {
        var result = Run("""
                         using EmberTrace.Abstractions.Attributes;

                         [TraceCategory("Orders")]
                         public class OrderService
                         {
                             [TraceName("Fetch")]
                             public const int Fetch = 4100;
                         }
                         """);

        Assert.IsEmpty(result.Diagnostics);
        StringAssert.Contains(
            result.Source("EmberTrace.GeneratedTraceMetadataProvider.g.cs"),
            @"[4100] = new global::EmberTrace.Metadata.TraceMeta(4100, @""Fetch"", null)");
    }

    [TestMethod]
    public void EditingUnrelatedFile_KeepsGeneratorOutputCached()
    {
        const string attributes = """
                                  using EmberTrace.Abstractions.Attributes;
                                  [assembly: TraceId(1000, "App", "App")]
                                  """;

        var compilation = Compile(attributes, "class Unrelated { }");
        var driver = Driver(false).RunGenerators(compilation);

        var edited = compilation.ReplaceSyntaxTree(
            compilation.SyntaxTrees.Last(),
            CSharpSyntaxTree.ParseText("class Unrelated { int Added; }", GeneratorTestHost.ParseOptions));

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

        var driver = Driver(false).RunGenerators(compilation);

        var edited = compilation.ReplaceSyntaxTree(
            compilation.SyntaxTrees[0],
            CSharpSyntaxTree.ParseText("""
                                       using EmberTrace.Abstractions.Attributes;
                                       [assembly: TraceId(1000, "Application", "App")]
                                       """, GeneratorTestHost.ParseOptions));

        Assert.Contains(IncrementalStepRunReason.Modified, OutputStepReasons(driver.RunGenerators(edited)));
    }

    [TestMethod]
    public void GeneratedProvider_Compiles()
    {
        var result = GeneratorTestHost.RunAndCompile("""
                                                     using EmberTrace.Abstractions.Attributes;
                                                     [assembly: TraceId(1000, "App", "App")]
                                                     """);

        Assert.IsTrue(result.Sources.ContainsKey("EmberTrace.GeneratedTraceMetadataProvider.g.cs"));
    }

    [TestMethod]
    public void RunAndCompile_FailsWhenTheCompilationDoesNot()
    {
        Assert.ThrowsExactly<AssertFailedException>(
            () => GeneratorTestHost.RunAndCompile("class C { int X => \"not an int\"; }"));
    }

    private static IReadOnlyList<IncrementalStepRunReason> OutputStepReasons(GeneratorDriver driver)
    {
        return driver.GetRunResult()
            .Results[0]
            .TrackedOutputSteps.SelectMany(kvp => kvp.Value)
            .SelectMany(step => step.Outputs)
            .Select(output => output.Reason)
            .ToArray();
    }

    private static GeneratorOutput Run(string code, bool generateTraceIds = false)
    {
        return GeneratorTestHost.Run(code, generateTraceIds);
    }

    private static GeneratorDriver Driver(bool generateTraceIds)
    {
        return GeneratorTestHost.Driver(generateTraceIds);
    }

    private static CSharpCompilation Compile(params string[] sources)
    {
        return GeneratorTestHost.Compile(sources);
    }
}
