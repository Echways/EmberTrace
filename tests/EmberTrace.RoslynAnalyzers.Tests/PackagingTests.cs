using Microsoft.CodeAnalysis;

namespace EmberTrace.RoslynAnalyzers.Tests;

[TestClass]
public class PackagingTests
{
    private const string Composition = "System.Composition.AttributedModel";

    [TestMethod]
    public void CodeFixes_DoNotOutpaceTheCompositionVersionRoslynIsBuiltAgainst()
    {
        var host = ReferencedVersion(typeof(Workspace).Assembly);
        var fixes = ReferencedVersion(typeof(UsageCodeFixProvider).Assembly);

        Assert.IsTrue(fixes <= host,
            $"Code fixes reference {Composition} {fixes}, but Roslyn is built against {host}; the IDE will not load them.");
    }

    private static Version ReferencedVersion(System.Reflection.Assembly assembly)
    {
        return assembly.GetReferencedAssemblies().Single(name => name.Name == Composition).Version!;
    }
}
