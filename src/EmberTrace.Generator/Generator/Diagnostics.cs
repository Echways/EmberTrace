using Microsoft.CodeAnalysis;

namespace EmberTrace.Generator.Generator;

internal static class Diagnostics
{
    internal static readonly DiagnosticDescriptor DuplicateId = new(
        "ETG001",
        "Duplicate TraceId",
        "Duplicate TraceId '{0}' already used by '{1}'",
        "EmberTrace.Generator",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor EmptyName = new(
        "ETG002",
        "Empty TraceId name",
        "TraceId '{0}' has an empty name",
        "EmberTrace.Generator",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor EmptyCategory = new(
        "ETG003",
        "Empty TraceId category",
        "TraceId '{0}' has an empty category",
        "EmberTrace.Generator",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);
}
