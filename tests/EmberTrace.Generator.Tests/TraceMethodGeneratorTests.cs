namespace EmberTrace.Generator.Tests;

[TestClass]
public class TraceMethodGeneratorTests
{
    [TestMethod]
    public void VoidMethod_IsWrappedInASynchronousScope()
    {
        var source = Wrapper("""
                             using EmberTrace.Abstractions.Attributes;

                             namespace Acme.Orders;

                             public partial class OrderService
                             {
                                 [Trace]
                                 public partial void Save();

                                 private void SaveCore() { }
                             }
                             """);

        StringAssert.Contains(source, "namespace Acme.Orders");
        StringAssert.Contains(source, "partial class OrderService");
        StringAssert.Contains(source, "public partial void Save()");
        StringAssert.Contains(source, "using var __emberTraceScope = global::EmberTrace.Tracer.Scope(");
        StringAssert.Contains(source, "SaveCore();");
        Assert.DoesNotContain("return SaveCore", source);
    }

    [TestMethod]
    public void ValueReturningMethod_ReturnsTheCoreResult()
    {
        var source = Wrapper("""
                             using EmberTrace.Abstractions.Attributes;

                             public partial class C
                             {
                                 [Trace]
                                 public partial int Sum(int a, int b);

                                 private int SumCore(int a, int b) => a + b;
                             }
                             """);

        StringAssert.Contains(source, "public partial int Sum(int a, int b)");
        StringAssert.Contains(source, "return SumCore(a, b);");
    }

    [TestMethod]
    public void GenericMethod_KeepsTypeParametersAndConstraints()
    {
        var source = Wrapper("""
                             using EmberTrace.Abstractions.Attributes;

                             public partial class C
                             {
                                 [Trace]
                                 public partial T Pick<T>(T value) where T : class, new();

                                 private T PickCore<T>(T value) where T : class, new() => value;
                             }
                             """);

        StringAssert.Contains(source, "public partial T Pick<T>(T value) where T : class, new()");
        StringAssert.Contains(source, "return PickCore<T>(value);");
    }

    [TestMethod]
    public void NestedTypes_AreReopenedOutermostFirst()
    {
        var source = Wrapper("""
                             using EmberTrace.Abstractions.Attributes;

                             public partial class Outer
                             {
                                 public partial class Inner
                                 {
                                     [Trace]
                                     public partial void M();

                                     private void MCore() { }
                                 }
                             }
                             """);

        var outer = source.IndexOf("partial class Outer", StringComparison.Ordinal);
        var inner = source.IndexOf("partial class Inner", StringComparison.Ordinal);

        Assert.IsGreaterThan(-1, outer);
        Assert.IsGreaterThan(outer, inner);
    }

    [TestMethod]
    public void DefaultParameterValues_AreNotRepeated()
    {
        var source = Wrapper("""
                             using EmberTrace.Abstractions.Attributes;

                             public partial class C
                             {
                                 [Trace]
                                 public partial void M(int a = 5);

                                 private void MCore(int a = 5) { }
                             }
                             """);

        StringAssert.Contains(source, "public partial void M(int a)");
        Assert.DoesNotContain("int a = 5", source);
    }

    [TestMethod]
    public void GlobalNamespace_EmitsNoNamespaceBlock()
    {
        var source = Wrapper("""
                             using EmberTrace.Abstractions.Attributes;

                             public partial class C
                             {
                                 [Trace]
                                 public partial void M();

                                 private void MCore() { }
                             }
                             """);

        Assert.DoesNotContain("namespace ", source);
    }

    [TestMethod]
    public void TaskMethod_BranchesOnIsRunningAndDelegatesToAnAsyncHelper()
    {
        var source = Wrapper("""
                             using System.Threading.Tasks;
                             using EmberTrace.Abstractions.Attributes;

                             public partial class C
                             {
                                 [Trace]
                                 public partial Task<int> GetAsync(int id);

                                 private async Task<int> GetAsyncCore(int id) { await Task.Yield(); return id; }
                             }
                             """);

        StringAssert.Contains(source,
            "=> global::EmberTrace.Tracer.IsRunning ? GetAsync__EmberTraceTraced(id) : GetAsyncCore(id);");
        StringAssert.Contains(source, "[global::System.Diagnostics.DebuggerNonUserCode]");
        StringAssert.Contains(source,
            "private async global::System.Threading.Tasks.Task<int> GetAsync__EmberTraceTraced(int id)");
        StringAssert.Contains(source, "await using var __emberTraceScope = global::EmberTrace.Tracer.ScopeAsync(");
        StringAssert.Contains(source, "return await GetAsyncCore(id).ConfigureAwait(false);");
    }

    [TestMethod]
    public void NonGenericTaskMethod_AwaitsWithoutReturning()
    {
        var source = Wrapper("""
                             using System.Threading.Tasks;
                             using EmberTrace.Abstractions.Attributes;

                             public partial class C
                             {
                                 [Trace]
                                 public partial ValueTask SaveAsync();

                                 private ValueTask SaveCoreAsync() => default;
                                 private ValueTask SaveAsyncCore() => default;
                             }
                             """);

        StringAssert.Contains(source, "await SaveAsyncCore().ConfigureAwait(false);");
        Assert.DoesNotContain("return await SaveAsyncCore", source);
    }

    [TestMethod]
    public void StaticAsyncMethod_GetsAStaticHelper()
    {
        var source = Wrapper("""
                             using System.Threading.Tasks;
                             using EmberTrace.Abstractions.Attributes;

                             public partial class C
                             {
                                 [Trace]
                                 public static partial Task M();

                                 private static Task MCore() => Task.CompletedTask;
                             }
                             """);

        StringAssert.Contains(source, "public static partial global::System.Threading.Tasks.Task M()");
        StringAssert.Contains(source,
            "private static async global::System.Threading.Tasks.Task M__EmberTraceTraced()");
    }

    [TestMethod]
    public void GenericAsyncMethod_PassesTypeArgumentsToTheHelper()
    {
        var source = Wrapper("""
                             using System.Threading.Tasks;
                             using EmberTrace.Abstractions.Attributes;

                             public partial class C
                             {
                                 [Trace]
                                 public partial Task<T> EchoAsync<T>(T value) where T : notnull;

                                 private Task<T> EchoAsyncCore<T>(T value) where T : notnull => Task.FromResult(value);
                             }
                             """);

        StringAssert.Contains(source,
            "=> global::EmberTrace.Tracer.IsRunning ? EchoAsync__EmberTraceTraced<T>(value) : EchoAsyncCore<T>(value);");
        StringAssert.Contains(source,
            "private async global::System.Threading.Tasks.Task<T> EchoAsync__EmberTraceTraced<T>(T value) where T : notnull");
    }

    [TestMethod]
    public void ReadonlyAsyncStructMethod_ReportsETG012()
    {
        var diagnostics = GeneratorTestHost.Run("""
                                                using System.Threading.Tasks;
                                                using EmberTrace.Abstractions.Attributes;

                                                public partial struct S
                                                {
                                                    [Trace]
                                                    public readonly partial Task M();

                                                    private readonly Task MCore() => Task.CompletedTask;
                                                }
                                                """).Diagnostics;

        Assert.IsTrue(diagnostics.Any(d => d.Id == "ETG012"),
            "A readonly struct member cannot call a non-readonly async helper without CS8656");
    }

    [TestMethod]
    public void TracedMethod_JoinsTheMetadataProvider()
    {
        var result = GeneratorTestHost.Run("""
                                           using EmberTrace.Abstractions.Attributes;

                                           namespace Acme;

                                           [TraceCategory("Orders")]
                                           public partial class OrderService
                                           {
                                               [Trace]
                                               public partial void Save();

                                               private void SaveCore() { }
                                           }
                                           """);

        var provider = result.Source("EmberTrace.GeneratedTraceMetadataProvider.g.cs");

        StringAssert.Contains(provider, @"@""OrderService.Save""");
        StringAssert.Contains(provider, @"@""Orders""");
    }

    [TestMethod]
    public void DifferentNamesOnOneId_StillReportETG001()
    {
        var result = GeneratorTestHost.Run("""
                                           using EmberTrace.Abstractions.Attributes;
                                           [assembly: TraceId(7, "Dup")]

                                           public partial class C
                                           {
                                               [Trace(Id = 7)]
                                               public partial void Dup();

                                               private void DupCore() { }
                                           }
                                           """);

        Assert.IsTrue(result.Diagnostics.Any(d => d.Id == "ETG001"),
            "Two different names on one id must still collide");
    }

    internal static string Wrapper(string code)
    {
        var sources = GeneratorTestHost.Run(code).Sources
            .Where(pair => pair.Key.StartsWith("EmberTrace.Trace.", StringComparison.Ordinal))
            .ToList();

        Assert.HasCount(1, sources);
        return sources[0].Value;
    }
}
