using Microsoft.CodeAnalysis;

namespace EmberTrace.Generator.Tests;

[TestClass]
public class TraceMethodDiagnosticsTests
{
    [TestMethod]
    [DataRow("ETG010", """
                       public partial class C
                       {
                           [Trace]
                           public void M() { }
                       }
                       """)]
    [DataRow("ETG011", """
                       public partial class C
                       {
                           [Trace]
                           public partial void M();
                       }
                       """)]
    [DataRow("ETG011", """
                       public partial class C
                       {
                           [Trace]
                           public partial void M(int a);
                           private void MCore(string a) { }
                       }
                       """)]
    [DataRow("ETG012", """
                       public partial class C
                       {
                           private int _value;
                           [Trace]
                           public partial ref int M();
                           private ref int MCore() => ref _value;
                       }
                       """)]
    [DataRow("ETG012", """
                       public partial class C
                       {
                           [Trace]
                           public partial Task M(ref int a);
                           private Task MCore(ref int a) => Task.CompletedTask;
                       }
                       """)]
    [DataRow("ETG012", """
                       public partial interface I
                       {
                           [Trace]
                           public partial void M();
                       }
                       """)]
    [DataRow("ETG012", """
                       public partial class C
                       {
                           [Trace]
                           public partial IAsyncEnumerable<int> M();
                           private IAsyncEnumerable<int> MCore() => null!;
                       }
                       """)]
    [DataRow("ETG012", """
                       public partial struct S
                       {
                           [Trace]
                           public readonly partial Task M();
                           private readonly Task MCore() => Task.CompletedTask;
                       }
                       """)]
    [DataRow("ETG013", """
                       public class C
                       {
                           [Trace]
                           public partial void M();
                           private void MCore() { }
                       }
                       """)]
    [DataRow("ETG013", """
                       public class Outer
                       {
                           public partial class Inner
                           {
                               [Trace]
                               public partial void M();
                               private void MCore() { }
                           }
                       }
                       """)]
    public void UnsupportedTraceMethod_ReportsAnErrorAndEmitsNoWrapper(string id, string declaration)
    {
        var output = GeneratorTestHost.Run(
            "using System.Collections.Generic;\nusing System.Threading.Tasks;\nusing EmberTrace.Abstractions.Attributes;\n"
            + declaration);

        Assert.AreEqual(id, output.Diagnostics.Single(d => d.Severity == DiagnosticSeverity.Error).Id);
        Assert.IsFalse(output.Sources.Keys.Any(key => key.StartsWith("EmberTrace.Trace.", StringComparison.Ordinal)));
    }
}
