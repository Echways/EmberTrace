using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
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

        var methodResults = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                TraceMethodDiscovery.TraceAttribute,
                static (node, _) => node is MethodDeclarationSyntax,
                static (attributeContext, _) => TraceMethodDiscovery.From(attributeContext))
            .Where(static result => result.HasValue)
            .Select(static (result, _) => result!.Value)
            .Collect()
            .Select(static (results, _) => new EquatableArray<TraceMethodResult>(results));

        var decorators = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                TraceMethodDiscovery.TraceAttribute,
                static (node, _) => node is ClassDeclarationSyntax,
                static (attributeContext, _) => DecoratorDiscovery.From(attributeContext))
            .Where(static item => item.HasValue)
            .Select(static (item, _) => item!.Value)
            .Collect()
            .Select(static (items, _) => new EquatableArray<DecoratorItem>(items));

        var hasServiceCollection = context.CompilationProvider
            .Select(static (compilation, _) =>
                compilation.GetTypeByMetadataName("Microsoft.Extensions.DependencyInjection.IServiceCollection")
                    is not null);

        var generateTraceIds = context.AnalyzerConfigOptionsProvider
            .Select(static (options, _) => NameFormatting.GetBoolOption(options.GlobalOptions, GenerateTraceIdsOption));

        context.RegisterSourceOutput(
            assemblyItems.Combine(fieldItems).Combine(methodResults).Combine(decorators).Combine(generateTraceIds),
            static (spc, input) => Emit(spc, input.Left.Left.Left.Left, input.Left.Left.Left.Right,
                input.Left.Left.Right, input.Left.Right, input.Right));

        context.RegisterSourceOutput(methodResults, static (spc, results) => EmitWrappers(spc, results));

        context.RegisterSourceOutput(
            decorators.Combine(hasServiceCollection),
            static (spc, input) => EmitDecorators(spc, input.Left, input.Right));
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
        EquatableArray<TraceMethodResult> methodResults,
        EquatableArray<DecoratorItem> decorators,
        bool generateTraceIds)
    {
        var fromAssembly = TraceCatalog.Validate(assemblyItems, Diagnostics.InvalidTraceIdArgument, spc);
        var fromFields = TraceCatalog.Validate(fieldItems, Diagnostics.NonConstantTraceField, spc);
        var fromMethods = MetadataFor(methodResults);
        var fromDecorators = MetadataForDecorators(decorators);

        var all = fromAssembly.AddRange(fromFields).AddRange(fromMethods).AddRange(fromDecorators)
            .Sort(TraceItem.Ordering);
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

    private static ImmutableArray<TraceItem> MetadataFor(EquatableArray<TraceMethodResult> results)
    {
        var builder = ImmutableArray.CreateBuilder<TraceItem>();

        foreach (var resolved in TraceMethodNaming.Resolve(Valid(results)))
            builder.Add(new TraceItem(resolved.Id, resolved.Name, resolved.Category, resolved.Item.Location));

        return builder.ToImmutable();
    }

    private static ImmutableArray<TraceItem> MetadataForDecorators(EquatableArray<DecoratorItem> decorators)
    {
        var builder = ImmutableArray.CreateBuilder<TraceItem>();

        foreach (var decorator in decorators)
        {
            if (decorator.InterfaceType.Length == 0)
                continue;

            foreach (var resolved in TraceMethodNaming.Resolve(decorator.Methods.Values))
                builder.Add(new TraceItem(resolved.Id, resolved.Name, resolved.Category, decorator.Location));
        }

        return builder.ToImmutable();
    }

    private static void EmitWrappers(SourceProductionContext spc, EquatableArray<TraceMethodResult> results)
    {
        foreach (var result in results)
        {
            var diagnostic = TraceMethodDiscovery.Diagnose(result);
            if (diagnostic is not null)
                spc.ReportDiagnostic(diagnostic);
        }

        foreach (var group in TraceMethodNaming.Resolve(Valid(results))
                     .GroupBy(method => method.Item.TypeKey, StringComparer.Ordinal))
        {
            var methods = group.ToImmutableArray();

            spc.AddSource(
                WrapperEmitter.HintName(methods[0].Item),
                SourceText.From(WrapperEmitter.Render(methods), Encoding.UTF8));
        }
    }

    private static void EmitDecorators(SourceProductionContext spc, EquatableArray<DecoratorItem> items,
        bool emitRegistration)
    {
        foreach (var item in items)
        {
            foreach (var diagnostic in DecoratorDiscovery.Diagnose(item))
                spc.ReportDiagnostic(diagnostic);

            if (item.InterfaceType.Length == 0)
                continue;

            spc.AddSource(
                DecoratorEmitter.HintName(item),
                SourceText.From(
                    DecoratorEmitter.Render(item, TraceMethodNaming.Resolve(item.Methods.Values), emitRegistration),
                    Encoding.UTF8));
        }
    }

    private static ImmutableArray<TraceMethodItem> Valid(EquatableArray<TraceMethodResult> results)
    {
        var builder = ImmutableArray.CreateBuilder<TraceMethodItem>();

        foreach (var result in results)
            if (result.Error == TraceMethodError.None)
                builder.Add(result.Item);

        return builder.ToImmutable();
    }
}
