using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EmberTrace.Generator.Generator;

internal static class MethodSignature
{
    internal static readonly SymbolDisplayFormat Qualified = SymbolDisplayFormat.FullyQualifiedFormat
        .AddMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    private static readonly SymbolDisplayFormat Short = SymbolDisplayFormat.MinimallyQualifiedFormat;

    internal static string Render(ITypeSymbol type)
    {
        return type.ToDisplayString(Qualified);
    }

    internal static string Identifier(string name)
    {
        return SyntaxFacts.GetKeywordKind(name) == SyntaxKind.None ? name : "@" + name;
    }

    internal static string Modifiers(MethodDeclarationSyntax node)
    {
        var parts = new List<string>();
        foreach (var modifier in node.Modifiers)
            if (!modifier.IsKind(SyntaxKind.PartialKeyword))
                parts.Add(modifier.Text);

        parts.Add("partial");
        return string.Join(" ", parts);
    }

    internal static string HelperModifiers(MethodDeclarationSyntax node)
    {
        return node.Modifiers.Any(SyntaxKind.StaticKeyword) ? "private static async" : "private async";
    }

    internal static ImmutableArray<ParameterInfo> Parameters(IMethodSymbol method)
    {
        var builder = ImmutableArray.CreateBuilder<ParameterInfo>(method.Parameters.Length);

        foreach (var parameter in method.Parameters)
            builder.Add(new ParameterInfo(Modifier(parameter), Render(parameter.Type),
                Identifier(parameter.Name)));

        return builder.ToImmutable();
    }

    internal static string TypeParameters(IMethodSymbol method)
    {
        return method.TypeParameters.Length == 0
            ? string.Empty
            : "<" + string.Join(", ", method.TypeParameters.Select(p => p.Name)) + ">";
    }

    internal static string Constraints(IMethodSymbol method)
    {
        var sb = new StringBuilder();

        foreach (var parameter in method.TypeParameters)
            sb.Append(ConstraintOf(parameter));

        return sb.ToString();
    }

    internal static string ConstraintOf(ITypeParameterSymbol parameter)
    {
        var parts = new List<string>();

        if (parameter.HasNotNullConstraint)
            parts.Add("notnull");

        if (parameter.HasUnmanagedTypeConstraint)
            parts.Add("unmanaged");
        else if (parameter.HasValueTypeConstraint)
            parts.Add("struct");

        if (parameter.HasReferenceTypeConstraint)
            parts.Add(parameter.ReferenceTypeConstraintNullableAnnotation == NullableAnnotation.Annotated
                ? "class?"
                : "class");

        foreach (var constraint in parameter.ConstraintTypes)
            parts.Add(Render(constraint));

        if (parameter.HasConstructorConstraint)
            parts.Add("new()");

        return parts.Count == 0
            ? string.Empty
            : " where " + parameter.Name + " : " + string.Join(", ", parts);
    }

    internal static ImmutableArray<string> TypeChain(INamedTypeSymbol type)
    {
        var headers = new List<string>();

        for (var current = type; current is not null; current = current.ContainingType)
            headers.Insert(0, Header(current));

        return headers.ToImmutableArray();
    }

    internal static string SignatureKey(IMethodSymbol method)
    {
        return method.Name
               + "`" + method.TypeParameters.Length
               + "(" + string.Join(",", method.Parameters.Select(p => p.RefKind + ":" + Render(p.Type))) + ")";
    }

    internal static string DisplaySignature(IMethodSymbol method)
    {
        var typeParameters = method.TypeParameters.Length == 0
            ? string.Empty
            : "<" + string.Join(", ", method.TypeParameters.Select(p => p.Name)) + ">";

        return typeParameters
               + "(" + string.Join(", ", method.Parameters.Select(p => p.Type.ToDisplayString(Short))) + ")";
    }

    internal static TraceReturnKind ReturnKind(IMethodSymbol method)
    {
        if (method.ReturnsVoid)
            return TraceReturnKind.Void;

        var name = method.ReturnType.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        switch (name)
        {
            case "global::System.Threading.Tasks.Task":
                return TraceReturnKind.Task;
            case "global::System.Threading.Tasks.Task<TResult>":
                return TraceReturnKind.TaskOfT;
            case "global::System.Threading.Tasks.ValueTask":
                return TraceReturnKind.ValueTask;
            case "global::System.Threading.Tasks.ValueTask<TResult>":
                return TraceReturnKind.ValueTaskOfT;
            default:
                return TraceReturnKind.Value;
        }
    }

    private static string Modifier(IParameterSymbol parameter)
    {
        if (parameter.IsParams)
            return "params";

        switch (parameter.RefKind)
        {
            case RefKind.Ref:
                return "ref";
            case RefKind.Out:
                return "out";
            case RefKind.In:
                return "in";
            case RefKind.RefReadOnlyParameter:
                return "ref readonly";
            default:
                return string.Empty;
        }
    }

    private static string Header(INamedTypeSymbol type)
    {
        var prefix = string.Empty;

        if (type.IsReadOnly)
            prefix += "readonly ";

        if (type.IsRefLikeType)
            prefix += "ref ";

        var keyword = type.TypeKind switch
        {
            TypeKind.Interface => "interface",
            TypeKind.Struct => type.IsRecord ? "record struct" : "struct",
            _ => type.IsRecord ? "record" : "class"
        };

        var name = type.TypeParameters.Length == 0
            ? type.Name
            : type.Name + "<" + string.Join(", ", type.TypeParameters.Select(p => p.Name)) + ">";

        return prefix + "partial " + keyword + " " + name;
    }
}
