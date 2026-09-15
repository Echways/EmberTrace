using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EmberTrace.Generator.Generator;

internal enum TraceReturnKind
{
    Void,
    Value,
    Task,
    TaskOfT,
    ValueTask,
    ValueTaskOfT
}

internal static class TraceMethodShape
{
    internal static TraceReturnKind ReturnKind(IMethodSymbol method)
    {
        if (method.ReturnsVoid)
            return TraceReturnKind.Void;

        switch (method.ReturnType.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
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

    internal static bool IsAsync(TraceReturnKind kind)
    {
        return kind is not (TraceReturnKind.Void or TraceReturnKind.Value);
    }

    internal static bool HasTraceableSignature(IMethodSymbol method)
    {
        if (method.RefKind != RefKind.None || IsAsyncEnumerable(method.ReturnType))
            return false;

        if (!IsAsync(ReturnKind(method)))
            return true;

        foreach (var parameter in method.Parameters)
            if (parameter.RefKind != RefKind.None || parameter.Type.IsRefLikeType)
                return false;

        return true;
    }

    internal static bool CanWrap(IMethodSymbol method, MethodDeclarationSyntax node)
    {
        return method.ContainingType.TypeKind != TypeKind.Interface
               && !method.IsExtern
               && method.ExplicitInterfaceImplementations.IsEmpty
               && !node.Modifiers.Any(SyntaxKind.UnsafeKeyword)
               && !(IsAsync(ReturnKind(method)) && node.Modifiers.Any(SyntaxKind.ReadOnlyKeyword))
               && HasTraceableSignature(method);
    }

    private static bool IsAsyncEnumerable(ITypeSymbol type)
    {
        return type.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
               == "global::System.Collections.Generic.IAsyncEnumerable<T>";
    }
}
