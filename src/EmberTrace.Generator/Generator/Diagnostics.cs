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

    internal static readonly DiagnosticDescriptor InvalidTraceIdArgument = new(
        "ETG004",
        "Invalid TraceId argument",
        "TraceId is ignored because its arguments are not a constant 'int' id and 'string' name",
        "EmberTrace.Generator",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor NonConstantTraceField = new(
        "ETG005",
        "Trace metadata attribute on a non-constant field",
        "Trace metadata attributes are ignored here because they only apply to 'const int' fields",
        "EmberTrace.Generator",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor ConflictingConstantName = new(
        "ETG006",
        "Conflicting TraceIds constant name",
        "TraceId names '{0}' and '{1}' both normalize to '{2}'; '{1}' is emitted as '{3}'",
        "EmberTrace.Generator",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);
}
