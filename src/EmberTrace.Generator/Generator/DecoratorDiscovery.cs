using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;

namespace EmberTrace.Generator.Generator;

internal readonly record struct DecoratorItem(
    string Namespace,
    string Accessibility,
    string ClassName,
    string DecoratorName,
    string InterfaceType,
    string TypeParameters,
    string Constraints,
    EquatableArray<TraceMethodItem> Methods,
    EquatableArray<string> Forwarded,
    EquatableArray<string> Untraced,
    LocationInfo? Location,
    int InterfaceCount);

internal static class DecoratorDiscovery
{
    internal static DecoratorItem? From(GeneratorAttributeSyntaxContext context)
    {
        if (context.TargetSymbol is not INamedTypeSymbol type)
            return null;

        var location = LocationInfo.From(context.TargetNode);
        var target = Target(type, context);

        if (target is null)
            return new DecoratorItem(string.Empty, "public", type.Name, "Traced" + type.Name, string.Empty,
                string.Empty, string.Empty, default, default, default, location,
                type.Interfaces.Length);

        var methods = ImmutableArray.CreateBuilder<TraceMethodItem>();
        var forwarded = ImmutableArray.CreateBuilder<string>();
        var untraced = ImmutableArray.CreateBuilder<string>();

        foreach (var member in target.GetMembers())
            switch (member)
            {
                case IMethodSymbol { MethodKind: MethodKind.Ordinary } method when IsTraceable(method):
                    methods.Add(Method(type, method));
                    break;
                case IMethodSymbol { MethodKind: MethodKind.Ordinary } method:
                    forwarded.Add(ForwardMethod(method));
                    untraced.Add(target.Name + "." + method.Name);
                    break;
                case IPropertySymbol property:
                    forwarded.Add(ForwardProperty(property));
                    break;
                case IEventSymbol @event:
                    forwarded.Add(ForwardEvent(@event));
                    break;
            }

        return new DecoratorItem(
            type.ContainingNamespace.IsGlobalNamespace ? string.Empty : type.ContainingNamespace.ToDisplayString(),
            type.DeclaredAccessibility == Accessibility.Public ? "public" : "internal",
            type.Name,
            "Traced" + type.Name,
            MethodSignature.Render(target),
            TypeParametersOf(type),
            ConstraintsOf(type),
            new EquatableArray<TraceMethodItem>(methods.ToImmutable()),
            new EquatableArray<string>(forwarded.ToImmutable()),
            new EquatableArray<string>(untraced.ToImmutable()),
            location,
            type.Interfaces.Length);
    }

    internal static ImmutableArray<Diagnostic> Diagnose(DecoratorItem item)
    {
        var builder = ImmutableArray.CreateBuilder<Diagnostic>();
        var origin = item.Location?.ToLocation();

        if (item.InterfaceType.Length == 0)
        {
            builder.Add(Diagnostic.Create(Diagnostics.AmbiguousInterface, origin, item.ClassName,
                item.InterfaceCount));
            return builder.ToImmutable();
        }

        foreach (var member in item.Untraced.Values)
            builder.Add(Diagnostic.Create(Diagnostics.UntracedMember, origin, member));

        return builder.ToImmutable();
    }

    private static INamedTypeSymbol? Target(INamedTypeSymbol type, GeneratorAttributeSyntaxContext context)
    {
        foreach (var attribute in context.Attributes)
            foreach (var argument in attribute.NamedArguments)
                if (argument.Key == "Interface" && argument.Value.Value is INamedTypeSymbol chosen)
                    return chosen;

        return type.Interfaces.Length == 1 ? type.Interfaces[0] : null;
    }

    private static bool IsTraceable(IMethodSymbol method)
    {
        if (method.RefKind != RefKind.None || TraceMethodDiscovery.IsAsyncEnumerable(method.ReturnType))
            return false;

        var kind = MethodSignature.ReturnKind(method);
        if (kind == TraceReturnKind.Void || kind == TraceReturnKind.Value)
            return true;

        foreach (var parameter in method.Parameters)
            if (parameter.RefKind != RefKind.None || parameter.Type.IsRefLikeType)
                return false;

        return true;
    }

    private static TraceMethodItem Method(INamedTypeSymbol type, IMethodSymbol method)
    {
        return new TraceMethodItem(
            string.Empty,
            default,
            type.Name,
            method.Name,
            MethodSignature.SignatureKey(method),
            MethodSignature.DisplaySignature(method),
            "public",
            "private async",
            MethodSignature.Render(method.ReturnType),
            MethodSignature.ReturnKind(method),
            MethodSignature.TypeParameters(method),
            MethodSignature.Constraints(method),
            new EquatableArray<ParameterInfo>(MethodSignature.Parameters(method)),
            "_inner." + method.Name,
            null,
            0,
            Category(type),
            null);
    }

    private static string ForwardMethod(IMethodSymbol method)
    {
        var parameters = MethodSignature.Parameters(method);
        var typeParameters = MethodSignature.TypeParameters(method);

        return "public " + MethodSignature.Render(method.ReturnType) + " " + method.Name + typeParameters
               + "(" + string.Join(", ", parameters.Select(p => p.Declaration)) + ")"
               + MethodSignature.Constraints(method)
               + " => _inner." + method.Name + typeParameters
               + "(" + string.Join(", ", parameters.Select(p => p.Argument)) + ");";
    }

    private static string ForwardProperty(IPropertySymbol property)
    {
        var name = property.IsIndexer
            ? "this[" + string.Join(", ", property.Parameters.Select(p =>
                MethodSignature.Render(p.Type) + " " + MethodSignature.Identifier(p.Name))) + "]"
            : property.Name;

        var access = property.IsIndexer
            ? "_inner[" + string.Join(", ", property.Parameters.Select(p =>
                MethodSignature.Identifier(p.Name))) + "]"
            : "_inner." + property.Name;

        var accessors = new StringBuilder();

        if (property.GetMethod is not null)
            accessors.Append(" get => ").Append(access).Append(';');

        if (property.SetMethod is not null)
            accessors.Append(" set => ").Append(access).Append(" = value;");

        return "public " + MethodSignature.Render(property.Type) + " " + name + " {" + accessors + " }";
    }

    private static string ForwardEvent(IEventSymbol @event)
    {
        return "public event " + MethodSignature.Render(@event.Type) + " " + @event.Name
               + " { add => _inner." + @event.Name + " += value;"
               + " remove => _inner." + @event.Name + " -= value; }";
    }

    private static string TypeParametersOf(INamedTypeSymbol type)
    {
        return type.TypeParameters.Length == 0
            ? string.Empty
            : "<" + string.Join(", ", type.TypeParameters.Select(p => p.Name)) + ">";
    }

    private static string ConstraintsOf(INamedTypeSymbol type)
    {
        var sb = new StringBuilder();

        foreach (var parameter in type.TypeParameters)
            sb.Append(MethodSignature.ConstraintOf(parameter));

        return sb.ToString();
    }

    private static string? Category(INamedTypeSymbol type)
    {
        foreach (var attribute in type.GetAttributes())
            if (attribute.AttributeClass?.ToDisplayString() == SymbolDiscovery.TraceCategoryAttribute
                && attribute.ConstructorArguments.Length == 1
                && attribute.ConstructorArguments[0].Value is string category)
                return category;

        return null;
    }
}
