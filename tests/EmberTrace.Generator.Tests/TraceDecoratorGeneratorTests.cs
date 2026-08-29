using Microsoft.CodeAnalysis;

namespace EmberTrace.Generator.Tests;

[TestClass]
public class TraceDecoratorGeneratorTests
{
    [TestMethod]
    public void TwoInterfaces_WithoutAnExplicitChoice_ReportETG014()
    {
        var diagnostics = GeneratorTestHost.Run("""
                                                using EmberTrace.Abstractions.Attributes;

                                                public interface IA { void M(); }
                                                public interface IB { void N(); }

                                                [Trace]
                                                public partial class S : IA, IB
                                                {
                                                    public void M() { }
                                                    public void N() { }
                                                }
                                                """).Diagnostics;

        Assert.IsTrue(diagnostics.Any(d => d.Id == "ETG014" && d.Severity == DiagnosticSeverity.Error));
    }

    [TestMethod]
    public void NoInterface_ReportsETG014()
    {
        var diagnostics = GeneratorTestHost.Run("""
                                                using EmberTrace.Abstractions.Attributes;

                                                [Trace]
                                                public partial class S
                                                {
                                                    public void M() { }
                                                }
                                                """).Diagnostics;

        Assert.IsTrue(diagnostics.Any(d => d.Id == "ETG014"));
    }

    [TestMethod]
    public void ExplicitInterface_ResolvesTheAmbiguity()
    {
        var diagnostics = GeneratorTestHost.Run("""
                                                using EmberTrace.Abstractions.Attributes;

                                                public interface IA { void M(); }
                                                public interface IB { void N(); }

                                                [Trace(Interface = typeof(IA))]
                                                public partial class S : IA, IB
                                                {
                                                    public void M() { }
                                                    public void N() { }
                                                }
                                                """).Diagnostics;

        Assert.IsEmpty(diagnostics.Where(d => d.Id == "ETG014"));
    }

    [TestMethod]
    public void UnsupportedMember_ReportsETG015AsInfo()
    {
        var diagnostics = GeneratorTestHost.Run("""
                                                using System.Collections.Generic;
                                                using EmberTrace.Abstractions.Attributes;

                                                public interface IA { IAsyncEnumerable<int> Stream(); }

                                                [Trace]
                                                public partial class S : IA
                                                {
                                                    public IAsyncEnumerable<int> Stream() => null!;
                                                }
                                                """).Diagnostics;

        Assert.IsTrue(diagnostics.Any(d => d.Id == "ETG015" && d.Severity == DiagnosticSeverity.Info));
    }

    [TestMethod]
    public void Decorator_WrapsEveryTraceableMemberAndForwardsTheRest()
    {
        var source = GeneratorTestHost.RunAndCompile("""
                                                     using System.Threading.Tasks;
                                                     using EmberTrace.Abstractions.Attributes;

                                                     namespace Acme;

                                                     public interface IOrderService
                                                     {
                                                         int Count { get; }
                                                         int Price(int quantity);
                                                         Task<int> PlaceAsync(int quantity);
                                                     }

                                                     [Trace]
                                                     public partial class OrderService : IOrderService
                                                     {
                                                         public int Count => 1;
                                                         public int Price(int quantity) => quantity;
                                                         public Task<int> PlaceAsync(int quantity) => Task.FromResult(quantity);
                                                     }
                                                     """).Sources
            .First(pair => pair.Key.StartsWith("EmberTrace.Decorator.", StringComparison.Ordinal)).Value;

        StringAssert.Contains(source, "public sealed class TracedOrderService : global::Acme.IOrderService");
        StringAssert.Contains(source, "private readonly global::Acme.IOrderService _inner;");
        StringAssert.Contains(source, "public int Count { get => _inner.Count; }");
        StringAssert.Contains(source, "return _inner.Price(quantity);");
        StringAssert.Contains(source,
            "=> global::EmberTrace.Tracer.IsRunning ? PlaceAsync__EmberTraceTraced(quantity) : _inner.PlaceAsync(quantity);");
    }

    [TestMethod]
    public void DecoratorScopeNames_UseTheClassName()
    {
        var provider = GeneratorTestHost.Run("""
                                             using EmberTrace.Abstractions.Attributes;

                                             namespace Acme;

                                             public interface IOrderService { void Save(); }

                                             [Trace]
                                             public partial class OrderService : IOrderService
                                             {
                                                 public void Save() { }
                                             }
                                             """).Source("EmberTrace.GeneratedTraceMetadataProvider.g.cs");

        StringAssert.Contains(provider, @"@""OrderService.Save""");
    }

    [TestMethod]
    public void GenericInterface_ProducesAGenericDecorator()
    {
        GeneratorTestHost.RunAndCompile("""
                                        using EmberTrace.Abstractions.Attributes;

                                        namespace Acme;

                                        public interface IRepository<T> where T : class
                                        {
                                            T? Find(int id);
                                        }

                                        [Trace]
                                        public partial class Repository<T> : IRepository<T> where T : class
                                        {
                                            public T? Find(int id) => null;
                                        }
                                        """);
    }

    [TestMethod]
    public void WithServiceCollection_ARegistrationIsEmitted()
    {
        var source = Decorator(GeneratorTestHost.Library("Di", ServiceCollectionSource));

        StringAssert.Contains(source, "AddTracedOrderService(");
    }

    [TestMethod]
    public void ServiceCollectionInSeveralReferences_StillEmitsTheRegistration()
    {
        var source = Decorator(
            GeneratorTestHost.Library("Di.Abstractions", ServiceCollectionSource),
            GeneratorTestHost.Library("Di.Shared", ServiceCollectionSource));

        StringAssert.Contains(source, "AddTracedOrderService(");
    }

    [TestMethod]
    public void WithoutServiceCollection_NoRegistrationIsEmitted()
    {
        var source = GeneratorTestHost.Run("""
                                           using EmberTrace.Abstractions.Attributes;

                                           namespace Acme;

                                           public interface IOrderService { void Save(); }

                                           [Trace]
                                           public partial class OrderService : IOrderService
                                           {
                                               public void Save() { }
                                           }
                                           """).Sources
            .First(pair => pair.Key.StartsWith("EmberTrace.Decorator.", StringComparison.Ordinal)).Value;

        Assert.DoesNotContain("IServiceCollection", source);
    }

    private const string ServiceCollectionSource = """
                                                   namespace Microsoft.Extensions.DependencyInjection
                                                   {
                                                       public interface IServiceCollection { }
                                                   }
                                                   """;

    private static string Decorator(params MetadataReference[] references)
    {
        return GeneratorTestHost.Run("""
                                     using EmberTrace.Abstractions.Attributes;

                                     namespace Acme;

                                     public interface IOrderService { void Save(); }

                                     [Trace]
                                     public partial class OrderService : IOrderService
                                     {
                                         public void Save() { }
                                     }
                                     """, false, references).Sources
            .First(pair => pair.Key.StartsWith("EmberTrace.Decorator.", StringComparison.Ordinal)).Value;
    }
}
