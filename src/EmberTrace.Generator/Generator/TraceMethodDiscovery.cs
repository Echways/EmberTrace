using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EmberTrace.Generator.Generator;

internal enum TraceMethodError
{
    None,
    NotPartial,
    MissingCore,
    UnsupportedShape,
    TypeNotPartial
}

internal readonly record struct TraceMethodResult(TraceMethodError Error, LocationInfo? Location, TraceMethodItem Item);

internal static class TraceMethodDiscovery
{
    internal const string TraceAttribute = "EmberTrace.Abstractions.Attributes.TraceAttribute";
    private const string CoreSuffix = "Core";

    internal static TraceMethodResult? From(GeneratorAttributeSyntaxContext context)
    {
        if (context.TargetSymbol is not IMethodSymbol method || context.TargetNode is not MethodDeclarationSyntax node)
            return null;

        var location = LocationInfo.From(node);
        var type = method.ContainingType;

        if (type.TypeKind == TypeKind.Interface || method.IsExtern || method.RefKind != RefKind.None
            || node.Modifiers.Any(SyntaxKind.UnsafeKeyword) || IsAsyncEnumerable(method.ReturnType))
            return Failure(TraceMethodError.UnsupportedShape, location, type, method);

        if (!method.IsPartialDefinition || method.PartialImplementationPart is not null)
            return Failure(TraceMethodError.NotPartial, location, type, method);

        if (!IsPartialAllTheWayOut(type))
            return Failure(TraceMethodError.TypeNotPartial, location, type, method);

        var returnKind = MethodSignature.ReturnKind(method);
        var isAsync = returnKind != TraceReturnKind.Void && returnKind != TraceReturnKind.Value;

        if (isAsync && node.Modifiers.Any(SyntaxKind.ReadOnlyKeyword))
            return Failure(TraceMethodError.UnsupportedShape, location, type, method);

        foreach (var parameter in method.Parameters)
            if (isAsync && (parameter.RefKind != RefKind.None || parameter.Type.IsRefLikeType))
                return Failure(TraceMethodError.UnsupportedShape, location, type, method);

        var coreName = method.Name + CoreSuffix;
        if (!HasCore(type, coreName, method))
            return Failure(TraceMethodError.MissingCore, location, type, method);

        return new TraceMethodResult(TraceMethodError.None, location, new TraceMethodItem(
            type.ContainingNamespace.IsGlobalNamespace
                ? string.Empty
                : type.ContainingNamespace.ToDisplayString(),
            new EquatableArray<string>(MethodSignature.TypeChain(type)),
            type.Name,
            method.Name,
            MethodSignature.SignatureKey(method),
            MethodSignature.DisplaySignature(method),
            MethodSignature.Modifiers(node),
            MethodSignature.HelperModifiers(node),
            MethodSignature.Render(method.ReturnType),
            returnKind,
            MethodSignature.TypeParameters(method),
            MethodSignature.Constraints(method),
            new EquatableArray<ParameterInfo>(MethodSignature.Parameters(method)),
            coreName,
            ExplicitName(method),
            ExplicitId(method),
            Category(method),
            location));
    }

    internal static Diagnostic? Diagnose(TraceMethodResult result)
    {
        var display = result.Item.TypeName + "." + result.Item.MethodName;
        var origin = result.Location?.ToLocation();

        switch (result.Error)
        {
            case TraceMethodError.NotPartial:
                return Diagnostic.Create(Diagnostics.NotPartial, origin, display);
            case TraceMethodError.MissingCore:
                return Diagnostic.Create(Diagnostics.MissingCore, origin, display,
                    result.Item.MethodName + CoreSuffix);
            case TraceMethodError.UnsupportedShape:
                return Diagnostic.Create(Diagnostics.UnsupportedShape, origin, display);
            case TraceMethodError.TypeNotPartial:
                return Diagnostic.Create(Diagnostics.TypeNotPartial, origin, display);
            default:
                return null;
        }
    }

    internal static bool IsAsyncEnumerable(ITypeSymbol type)
    {
        return type.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
               == "global::System.Collections.Generic.IAsyncEnumerable<T>";
    }

    private static TraceMethodResult Failure(TraceMethodError error, LocationInfo? location,
        INamedTypeSymbol type, IMethodSymbol method)
    {
        return new TraceMethodResult(error, location, new TraceMethodItem(
            string.Empty,
            default,
            type.Name,
            method.Name,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            TraceReturnKind.Void,
            string.Empty,
            string.Empty,
            default,
            string.Empty,
            null,
            0,
            null,
            location));
    }

    private static bool IsPartialAllTheWayOut(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.ContainingType)
        {
            if (current.DeclaringSyntaxReferences.Length == 0)
                return false;

            foreach (var reference in current.DeclaringSyntaxReferences)
                if (reference.GetSyntax() is not TypeDeclarationSyntax declaration
                    || !declaration.Modifiers.Any(SyntaxKind.PartialKeyword))
                    return false;
        }

        return true;
    }

    private static bool HasCore(INamedTypeSymbol type, string coreName, IMethodSymbol method)
    {
        foreach (var member in type.GetMembers(coreName))
        {
            if (member is not IMethodSymbol candidate)
                continue;

            if (candidate.TypeParameters.Length != method.TypeParameters.Length)
                continue;

            if (candidate.Parameters.Length != method.Parameters.Length)
                continue;

            if (MethodSignature.Render(candidate.ReturnType) != MethodSignature.Render(method.ReturnType))
                continue;

            var matches = true;
            for (var i = 0; i < candidate.Parameters.Length; i++)
                if (candidate.Parameters[i].RefKind != method.Parameters[i].RefKind
                    || MethodSignature.Render(candidate.Parameters[i].Type)
                    != MethodSignature.Render(method.Parameters[i].Type))
                {
                    matches = false;
                    break;
                }

            if (matches)
                return true;
        }

        return false;
    }

    private static string? ExplicitName(IMethodSymbol method)
    {
        foreach (var attribute in method.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() != TraceAttribute)
                continue;

            if (attribute.ConstructorArguments.Length == 1 && attribute.ConstructorArguments[0].Value is string name)
                return name;
        }

        return NamedString(method, SymbolDiscovery.TraceNameAttribute);
    }

    private static int ExplicitId(IMethodSymbol method)
    {
        foreach (var attribute in method.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() != TraceAttribute)
                continue;

            foreach (var argument in attribute.NamedArguments)
                if (argument.Key == "Id" && argument.Value.Value is int id)
                    return id;
        }

        return 0;
    }

    private static string? Category(IMethodSymbol method)
    {
        foreach (var attribute in method.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() != TraceAttribute)
                continue;

            foreach (var argument in attribute.NamedArguments)
                if (argument.Key == "Category" && argument.Value.Value is string category)
                    return category;
        }

        var onMethod = NamedString(method, SymbolDiscovery.TraceCategoryAttribute);
        if (onMethod is not null)
            return onMethod;

        for (var type = method.ContainingType; type is not null; type = type.ContainingType)
        {
            var onType = NamedString(type, SymbolDiscovery.TraceCategoryAttribute);
            if (onType is not null)
                return onType;
        }

        return null;
    }

    private static string? NamedString(ISymbol symbol, string attributeFullName)
    {
        foreach (var attribute in symbol.GetAttributes())
            if (attribute.AttributeClass?.ToDisplayString() == attributeFullName
                && attribute.ConstructorArguments.Length == 1
                && attribute.ConstructorArguments[0].Value is string value)
                return value;

        return null;
    }
}
