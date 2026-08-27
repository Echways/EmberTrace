using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace EmberTrace.Generator.Generator;

internal static class SymbolDiscovery
{
    internal const string TraceIdAttribute = "EmberTrace.Abstractions.Attributes.TraceIdAttribute";
    internal const string TraceNameAttribute = "EmberTrace.Abstractions.Attributes.TraceNameAttribute";
    internal const string TraceCategoryAttribute = "EmberTrace.Abstractions.Attributes.TraceCategoryAttribute";

    internal static ImmutableArray<TraceItem> FromAssembly(GeneratorAttributeSyntaxContext context)
    {
        var builder = ImmutableArray.CreateBuilder<TraceItem>(context.Attributes.Length);

        foreach (var attribute in context.Attributes)
        {
            var location = LocationInfo.From(attribute.ApplicationSyntaxReference?.GetSyntax());
            var arguments = attribute.ConstructorArguments;

            builder.Add(arguments.Length >= 2 && arguments[0].Value is int id && arguments[1].Value is string name
                ? new TraceItem(id, name, arguments.Length >= 3 ? arguments[2].Value as string : null, location)
                : TraceItem.Malformed(location));
        }

        return builder.ToImmutable();
    }

    internal static TraceItem? FromNamedField(GeneratorAttributeSyntaxContext context)
    {
        return FromField(context);
    }

    internal static TraceItem? FromCategorizedField(GeneratorAttributeSyntaxContext context)
    {
        return StringArgumentOf(context.TargetSymbol, TraceNameAttribute) is null ? FromField(context) : null;
    }

    private static TraceItem? FromField(GeneratorAttributeSyntaxContext context)
    {
        if (context.TargetSymbol is not IFieldSymbol field)
            return null;

        var location =
            LocationInfo.From(context.Attributes[0].ApplicationSyntaxReference?.GetSyntax() ?? context.TargetNode);

        if (field is not { HasConstantValue: true, ConstantValue: int id })
            return TraceItem.Malformed(location);

        return new TraceItem(
            id,
            StringArgumentOf(field, TraceNameAttribute) ?? field.Name,
            StringArgumentOf(field, TraceCategoryAttribute),
            location);
    }

    private static string? StringArgumentOf(ISymbol symbol, string attributeFullName)
    {
        foreach (var attribute in symbol.GetAttributes().Where(attribute =>
                     attribute.AttributeClass?.ToDisplayString() == attributeFullName
                     && attribute.ConstructorArguments.Length == 1
                     && attribute.ConstructorArguments[0].Value is string))
            return (string)attribute.ConstructorArguments[0].Value!;

        return null;
    }
}