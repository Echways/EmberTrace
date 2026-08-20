using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace EmberTrace.Generator.Generator;

[Generator]
public sealed class TraceMetadataGenerator : IIncrementalGenerator
{
    private const string GenerateTraceIdsOption = "build_property.EmberTraceGenerateTraceIds";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var assemblyItems = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                SymbolDiscovery.TraceIdAttribute,
                static (_, _) => true,
                static (attributeContext, _) => SymbolDiscovery.FromAssembly(attributeContext))
            .SelectMany(static (items, _) => items)
            .Collect()
            .Select(static (items, _) => new EquatableArray<TraceItem>(items));

        var fieldItems = Fields(context, SymbolDiscovery.TraceNameAttribute, SymbolDiscovery.FromNamedField)
            .Combine(Fields(context, SymbolDiscovery.TraceCategoryAttribute, SymbolDiscovery.FromCategorizedField))
            .Select(static (pair, _) => new EquatableArray<TraceItem>(pair.Left.AddRange(pair.Right)));

        var generateTraceIds = context.AnalyzerConfigOptionsProvider
            .Select(static (options, _) => NameFormatting.GetBoolOption(options.GlobalOptions, GenerateTraceIdsOption));

        context.RegisterSourceOutput(
            assemblyItems.Combine(fieldItems).Combine(generateTraceIds),
            static (spc, input) => Emit(spc, input.Left.Left, input.Left.Right, input.Right));
    }

    private static IncrementalValueProvider<ImmutableArray<TraceItem>> Fields(
        IncrementalGeneratorInitializationContext context,
        string attributeFullName,
        Func<GeneratorAttributeSyntaxContext, TraceItem?> read)
    {
        return context.SyntaxProvider
            .ForAttributeWithMetadataName(attributeFullName, static (_, _) => true,
                (attributeContext, _) => read(attributeContext))
            .Where(static item => item.HasValue)
            .Select(static (item, _) => item!.Value)
            .Collect();
    }

    private static void Emit(
        SourceProductionContext spc,
        EquatableArray<TraceItem> assemblyItems,
        EquatableArray<TraceItem> fieldItems,
        bool generateTraceIds)
    {
        var fromAssembly = TraceCatalog.Validate(assemblyItems, Diagnostics.InvalidTraceIdArgument, spc);
        var fromFields = TraceCatalog.Validate(fieldItems, Diagnostics.NonConstantTraceField, spc);

        var all = fromAssembly.AddRange(fromFields).Sort(TraceItem.Ordering);
        TraceCatalog.ReportContentIssues(all, spc);

        if (all.IsEmpty)
            return;

        spc.AddSource(
            "EmberTrace.GeneratedTraceMetadataProvider.g.cs",
            SourceText.From(SourceEmitter.RenderProvider(all), Encoding.UTF8));

        if (!generateTraceIds || fromAssembly.IsEmpty)
            return;

        spc.AddSource(
            "TraceIds.g.cs",
            SourceText.From(SourceEmitter.RenderTraceIds(IdComputation.ResolveConstants(fromAssembly, spc)),
                Encoding.UTF8));
    }
}