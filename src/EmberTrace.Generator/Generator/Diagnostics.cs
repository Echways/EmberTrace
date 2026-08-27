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
        true);

    internal static readonly DiagnosticDescriptor EmptyName = new(
        "ETG002",
        "Empty TraceId name",
        "TraceId '{0}' has an empty name",
        "EmberTrace.Generator",
        DiagnosticSeverity.Warning,
        true);

    internal static readonly DiagnosticDescriptor EmptyCategory = new(
        "ETG003",
        "Empty TraceId category",
        "TraceId '{0}' has an empty category",
        "EmberTrace.Generator",
        DiagnosticSeverity.Warning,
        true);

    internal static readonly DiagnosticDescriptor InvalidTraceIdArgument = new(
        "ETG004",
        "Invalid TraceId argument",
        "TraceId is ignored because its arguments are not a constant 'int' id and 'string' name",
        "EmberTrace.Generator",
        DiagnosticSeverity.Warning,
        true);

    internal static readonly DiagnosticDescriptor NonConstantTraceField = new(
        "ETG005",
        "Trace metadata attribute on a non-constant field",
        "Trace metadata attributes are ignored here because they only apply to 'const int' fields",
        "EmberTrace.Generator",
        DiagnosticSeverity.Warning,
        true);

    internal static readonly DiagnosticDescriptor ConflictingConstantName = new(
        "ETG006",
        "Conflicting TraceIds constant name",
        "TraceId names '{0}' and '{1}' both normalize to '{2}'; '{1}' is emitted as '{3}'",
        "EmberTrace.Generator",
        DiagnosticSeverity.Warning,
        true);

    internal static readonly DiagnosticDescriptor NotPartial = new(
        "ETG010",
        "Trace requires a partial method declaration",
        "'{0}' must be declared 'partial' with no implementation for EmberTrace to generate its body",
        "EmberTrace.Generator",
        DiagnosticSeverity.Error,
        true);

    internal static readonly DiagnosticDescriptor MissingCore = new(
        "ETG011",
        "Trace requires a Core method",
        "'{0}' needs a '{1}' method with the same signature to hold the body",
        "EmberTrace.Generator",
        DiagnosticSeverity.Error,
        true);

    internal static readonly DiagnosticDescriptor UnsupportedShape = new(
        "ETG012",
        "Unsupported method shape",
        "EmberTrace cannot wrap '{0}': ref returns, by-reference parameters on asynchronous methods, unsafe methods and interface members are not supported",
        "EmberTrace.Generator",
        DiagnosticSeverity.Error,
        true);

    internal static readonly DiagnosticDescriptor TypeNotPartial = new(
        "ETG013",
        "Containing type is not partial",
        "'{0}' and every type enclosing it must be declared 'partial'",
        "EmberTrace.Generator",
        DiagnosticSeverity.Error,
        true);

    internal static readonly DiagnosticDescriptor AmbiguousInterface = new(
        "ETG014",
        "Ambiguous decorator interface",
        "'{0}' implements {1} interfaces; set Interface = typeof(...) on the Trace attribute",
        "EmberTrace.Generator",
        DiagnosticSeverity.Error,
        true);

    internal static readonly DiagnosticDescriptor UntracedMember = new(
        "ETG015",
        "Member is forwarded without a scope",
        "'{0}' has an unsupported shape and is forwarded by the decorator without tracing",
        "EmberTrace.Generator",
        DiagnosticSeverity.Info,
        true);
}