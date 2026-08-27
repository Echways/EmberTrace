using Microsoft.CodeAnalysis;

namespace EmberTrace.Generator.Tests;

[TestClass]
public class TraceMethodDiagnosticsTests
{
    [TestMethod]
    public void NonPartialMethod_ReportsETG010()
    {
        AssertDiagnostic("ETG010", """
                                   using EmberTrace.Abstractions.Attributes;
                                   public partial class C
                                   {
                                       [Trace]
                                       public void M() { }
                                   }
                                   """);
    }

    [TestMethod]
    public void MissingCoreMethod_ReportsETG011()
    {
        AssertDiagnostic("ETG011", """
                                   using EmberTrace.Abstractions.Attributes;
                                   public partial class C
                                   {
                                       [Trace]
                                       public partial void M();
                                   }
                                   """);
    }

    [TestMethod]
    public void CoreWithADifferentSignature_ReportsETG011()
    {
        AssertDiagnostic("ETG011", """
                                   using EmberTrace.Abstractions.Attributes;
                                   public partial class C
                                   {
                                       [Trace]
                                       public partial void M(int a);
                                       private void MCore(string a) { }
                                   }
                                   """);
    }

    [TestMethod]
    public void RefReturn_ReportsETG012()
    {
        AssertDiagnostic("ETG012", """
                                   using EmberTrace.Abstractions.Attributes;
                                   public partial class C
                                   {
                                       private int _value;
                                       [Trace]
                                       public partial ref int M();
                                       private ref int MCore() => ref _value;
                                   }
                                   """);
    }

    [TestMethod]
    public void RefParameterOnAnAsyncMethod_ReportsETG012()
    {
        AssertDiagnostic("ETG012", """
                                   using System.Threading.Tasks;
                                   using EmberTrace.Abstractions.Attributes;
                                   public partial class C
                                   {
                                       [Trace]
                                       public partial Task M(ref int a);
                                       private Task MCore(ref int a) => Task.CompletedTask;
                                   }
                                   """);
    }

    [TestMethod]
    public void MethodOnAnInterface_ReportsETG012()
    {
        AssertDiagnostic("ETG012", """
                                   using EmberTrace.Abstractions.Attributes;
                                   public partial interface I
                                   {
                                       [Trace]
                                       public partial void M();
                                   }
                                   """);
    }

    [TestMethod]
    public void AsyncEnumerableReturn_ReportsETG012()
    {
        AssertDiagnostic("ETG012", """
                                   using System.Collections.Generic;
                                   using EmberTrace.Abstractions.Attributes;
                                   public partial class C
                                   {
                                       [Trace]
                                       public partial IAsyncEnumerable<int> M();
                                       private IAsyncEnumerable<int> MCore() => null!;
                                   }
                                   """);
    }

    [TestMethod]
    public void NonPartialContainingType_ReportsETG013()
    {
        AssertDiagnostic("ETG013", """
                                   using EmberTrace.Abstractions.Attributes;
                                   public class C
                                   {
                                       [Trace]
                                       public partial void M();
                                       private void MCore() { }
                                   }
                                   """);
    }

    [TestMethod]
    public void NonPartialOuterType_ReportsETG013()
    {
        AssertDiagnostic("ETG013", """
                                   using EmberTrace.Abstractions.Attributes;
                                   public class Outer
                                   {
                                       public partial class Inner
                                       {
                                           [Trace]
                                           public partial void M();
                                           private void MCore() { }
                                       }
                                   }
                                   """);
    }

    private static void AssertDiagnostic(string id, string code)
    {
        var diagnostics = GeneratorTestHost.Run(code).Diagnostics;

        Assert.IsTrue(diagnostics.Any(d => d.Id == id && d.Severity == DiagnosticSeverity.Error),
            $"Expected {id}, got [{string.Join(", ", diagnostics.Select(d => d.Id))}]");
    }
}
