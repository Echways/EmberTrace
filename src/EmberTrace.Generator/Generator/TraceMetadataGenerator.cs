using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace EmberTrace.Generator.Generator;

[Generator]
public sealed class TraceMetadataGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var compilationAndOptions = context.CompilationProvider.Combine(context.AnalyzerConfigOptionsProvider);

        context.RegisterSourceOutput(compilationAndOptions, static (spc, pair) =>
        {
            var (compilation, options) = pair;
            var items = SymbolDiscovery.Collect(compilation, spc);

            var providerSrc = SourceEmitter.RenderProvider(items);
            spc.AddSource("EmberTrace.GeneratedTraceMetadataProvider.g.cs", SourceText.From(providerSrc, Encoding.UTF8));

            TraceIdsGenerator.TryEmit(spc, items, options);
        });
    }
}
