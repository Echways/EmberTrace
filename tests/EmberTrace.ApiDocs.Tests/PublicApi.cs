using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace EmberTrace.ApiDocs.Tests;

internal sealed record ApiMember(string Id, IReadOnlyList<string> Parameters);

internal static class PublicApi
{
    private static readonly string[] SynthesizedRecordMembers =
        ["Equals", "Deconstruct", "PrintMembers", "op_Equality", "op_Inequality", "<Clone>$"];

    public static IReadOnlyList<ApiMember> Of(Assembly assembly)
    {
        var references = ReferencePaths()
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToList();

        var target = references.Single(reference =>
            string.Equals(reference.Display, assembly.Location, StringComparison.OrdinalIgnoreCase));

        var compilation = CSharpCompilation.Create("ApiDocs", references: references);
        var symbol = (IAssemblySymbol)compilation.GetAssemblyOrModuleSymbol(target)!;

        var members = new List<ApiMember>();
        Collect(symbol.GlobalNamespace, members);
        members.Sort(static (a, b) => string.CompareOrdinal(a.Id, b.Id));
        return members;
    }

    private static IEnumerable<string> ReferencePaths()
    {
        var trusted = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        return Directory.GetFiles(AppContext.BaseDirectory, "*.dll")
            .Where(IsManaged)
            .Concat(trusted)
            .GroupBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First());
    }

    private static bool IsManaged(string path)
    {
        try
        {
            AssemblyName.GetAssemblyName(path);
            return true;
        }
        catch (BadImageFormatException)
        {
            return false;
        }
    }

    private static void Collect(INamespaceOrTypeSymbol container, List<ApiMember> members)
    {
        foreach (var member in container.GetMembers())
            switch (member)
            {
                case INamespaceSymbol nested:
                    Collect(nested, members);
                    break;
                case INamedTypeSymbol type when IsVisible(type):
                    members.Add(new ApiMember(type.GetDocumentationCommentId()!, ParametersOf(type)));
                    if (type.TypeKind != TypeKind.Delegate)
                        Collect(type, members);
                    break;
                case INamedTypeSymbol:
                    break;
                default:
                    if (IsDocumented(member))
                        members.Add(new ApiMember(member.GetDocumentationCommentId()!, ParametersOf(member)));
                    break;
            }
    }

    private static IReadOnlyList<string> ParametersOf(ISymbol symbol)
    {
        return symbol switch
        {
            IMethodSymbol method => method.Parameters.Select(p => p.Name).ToArray(),
            INamedTypeSymbol { DelegateInvokeMethod: { } invoke } => invoke.Parameters.Select(p => p.Name).ToArray(),
            _ => []
        };
    }

    private static bool IsVisible(ISymbol symbol)
    {
        return symbol.DeclaredAccessibility is Accessibility.Public or Accessibility.Protected
            or Accessibility.ProtectedOrInternal;
    }

    private static bool IsDocumented(ISymbol member)
    {
        if (!IsVisible(member) || member.IsImplicitlyDeclared)
            return false;

        if (member.GetAttributes().Any(a => a.AttributeClass?.Name == "CompilerGeneratedAttribute"))
            return false;

        if (member is not IMethodSymbol method)
            return member.CanBeReferencedByName;

        if (method.MethodKind is MethodKind.PropertyGet or MethodKind.PropertySet or MethodKind.EventAdd
            or MethodKind.EventRemove or MethodKind.StaticConstructor)
            return false;

        if (method.IsOverride)
            return false;

        if (method.ContainingType.IsRecord && SynthesizedRecordMembers.Contains(method.Name))
            return false;

        return !(method.MethodKind == MethodKind.Constructor && method.Parameters.IsEmpty &&
                 method.ContainingType.IsValueType);
    }
}
