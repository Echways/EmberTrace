using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EmberTrace.RoslynAnalyzers.Tests;

[TestClass]
public class UsageCodeFixProviderTests
{
    [TestMethod]
    [DataRow("void M() { var scope = Tracer.Scope(1); }", "using var scope = Tracer.Scope(1);")]
    [DataRow("async Task M() { var scope = Tracer.ScopeAsync(1); }", "await using var scope = Tracer.ScopeAsync(1);")]
    [DataRow("async Task M() { using var scope = Tracer.ScopeAsync(1); }", "await using var scope = Tracer.ScopeAsync(1);")]
    public async Task LocalDeclaration_GainsTheMissingKeywords(string method, string expected)
    {
        var fixedCode = await Fix(method);

        StringAssert.Contains(fixedCode, expected);
        AnalyzerTestHost.AssertCompilesWithTheGenerator(fixedCode);
    }

    [TestMethod]
    [DataRow("void M() { Console.WriteLine(0); Tracer.Scope(1); Console.WriteLine(1); Console.WriteLine(2); }",
        "using (Tracer.Scope(1))")]
    [DataRow("async Task M() { Console.WriteLine(0); Tracer.ScopeAsync(1); Console.WriteLine(1); Console.WriteLine(2); }",
        "await using (Tracer.ScopeAsync(1))")]
    public async Task BareInvocation_WrapsTheRestOfTheBlock(string method, string expectedUsing)
    {
        var fixedCode = await Fix(method);

        var block = CSharpSyntaxTree.ParseText(fixedCode).GetRoot().DescendantNodes()
            .OfType<MethodDeclarationSyntax>().Single().Body!;

        Assert.HasCount(2, block.Statements);
        StringAssert.Contains(block.Statements[0].ToString(), "WriteLine(0)");
        StringAssert.StartsWith(block.Statements[1].ToString(), expectedUsing);
        StringAssert.Contains(block.Statements[1].ToString(), "WriteLine(1)");
        StringAssert.Contains(block.Statements[1].ToString(), "WriteLine(2)");
        AnalyzerTestHost.AssertCompilesWithTheGenerator(fixedCode);
    }

    private static Task<string> Fix(string method)
    {
        return AnalyzerTestHost.ApplyFixAsync(new UsageAnalyzers(), new UsageCodeFixProvider(),
            "using System;\nusing System.Threading.Tasks;\nusing EmberTrace;\nclass C\n{\n" + method + "\n}");
    }
}
