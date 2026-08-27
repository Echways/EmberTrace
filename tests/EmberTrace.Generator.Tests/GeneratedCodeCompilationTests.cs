namespace EmberTrace.Generator.Tests;

[TestClass]
public class GeneratedCodeCompilationTests
{
    [TestMethod]
    [DataRow("void M()", "void MCore() { }")]
    [DataRow("int M()", "int MCore() => 1;")]
    [DataRow("string? M(string? a)", "string? MCore(string? a) => a;")]
    [DataRow("(int First, string Second) M()", "(int First, string Second) MCore() => (1, \"a\");")]
    [DataRow("void M(int a = 5)", "void MCore(int a = 5) { }")]
    [DataRow("void M(params int[] a)", "void MCore(params int[] a) { }")]
    [DataRow("void M(ref int a, out int b, in int c)", "void MCore(ref int a, out int b, in int c) { b = c; a = c; }")]
    [DataRow("void M(int @class)", "void MCore(int @class) { }")]
    [DataRow("T M<T>(T a) where T : struct", "T MCore<T>(T a) where T : struct => a;")]
    [DataRow("T? M<T>(T? a) where T : class", "T? MCore<T>(T? a) where T : class => a;")]
    [DataRow("System.Span<byte> M(byte[] a)", "System.Span<byte> MCore(byte[] a) => a;")]
    [DataRow("System.Threading.Tasks.Task M()",
        "System.Threading.Tasks.Task MCore() => System.Threading.Tasks.Task.CompletedTask;")]
    [DataRow("System.Threading.Tasks.Task<int> M()",
        "System.Threading.Tasks.Task<int> MCore() => System.Threading.Tasks.Task.FromResult(1);")]
    [DataRow("System.Threading.Tasks.ValueTask M()", "System.Threading.Tasks.ValueTask MCore() => default;")]
    [DataRow("System.Threading.Tasks.ValueTask<int> M()",
        "System.Threading.Tasks.ValueTask<int> MCore() => new System.Threading.Tasks.ValueTask<int>(1);")]
    [DataRow("System.Threading.Tasks.Task<T> M<T>(T a) where T : notnull",
        "System.Threading.Tasks.Task<T> MCore<T>(T a) where T : notnull => System.Threading.Tasks.Task.FromResult(a);")]
    public void Shape_Compiles(string signature, string core)
    {
        GeneratorTestHost.RunAndCompile($$"""
                                          using EmberTrace.Abstractions.Attributes;

                                          namespace Acme.Services;

                                          public partial class Service
                                          {
                                              [Trace]
                                              public partial {{signature}};

                                              private {{core}}
                                          }
                                          """);
    }

    [TestMethod]
    public void StaticVirtualAndOverride_Compile()
    {
        GeneratorTestHost.RunAndCompile("""
                                        using EmberTrace.Abstractions.Attributes;

                                        namespace Acme.Services;

                                        public partial class Base
                                        {
                                            [Trace]
                                            public static partial void S();

                                            private static void SCore() { }

                                            [Trace]
                                            public virtual partial void V();

                                            protected void VCore() { }
                                        }

                                        public partial class Derived : Base
                                        {
                                            [Trace]
                                            public override partial void V();

                                            private new void VCore() { }
                                        }
                                        """);
    }

    [TestMethod]
    public void NestedGenericAndRecordTypes_Compile()
    {
        GeneratorTestHost.RunAndCompile("""
                                        using EmberTrace.Abstractions.Attributes;

                                        namespace Acme.Services;

                                        public partial class Outer<TKey> where TKey : notnull
                                        {
                                            public readonly partial struct Inner
                                            {
                                                [Trace]
                                                public partial int M(TKey key);

                                                private int MCore(TKey key) => key.GetHashCode();
                                            }
                                        }

                                        public partial record Rec
                                        {
                                            [Trace]
                                            public partial void M();

                                            private void MCore() { }
                                        }

                                        public partial record struct RecStruct
                                        {
                                            [Trace]
                                            public partial void M();

                                            private void MCore() { }
                                        }
                                        """);
    }

    [TestMethod]
    public void OverloadsInTheGlobalNamespace_Compile()
    {
        GeneratorTestHost.RunAndCompile("""
                                        using EmberTrace.Abstractions.Attributes;

                                        public partial class Service
                                        {
                                            [Trace]
                                            public partial void M(int a);

                                            private void MCore(int a) { }

                                            [Trace]
                                            public partial void M(string a);

                                            private void MCore(string a) { }
                                        }
                                        """);
    }

    [TestMethod]
    public void PartialTypeSplitAcrossDeclarations_Compiles()
    {
        GeneratorTestHost.RunAndCompile("""
                                        using EmberTrace.Abstractions.Attributes;

                                        namespace Acme.Services;

                                        public partial class Service
                                        {
                                            [Trace]
                                            public partial void M();
                                        }

                                        public partial class Service
                                        {
                                            private void MCore() { }
                                        }
                                        """);
    }
}
