using Microsoft.CodeAnalysis;

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

internal readonly record struct ParameterInfo(string Modifier, string Type, string Name)
{
    internal string Declaration =>
        Modifier.Length == 0 ? Type + " " + Name : Modifier + " " + Type + " " + Name;

    internal string Argument =>
        Modifier switch
        {
            "ref" => "ref " + Name,
            "out" => "out " + Name,
            "in" => "in " + Name,
            "ref readonly" => "in " + Name,
            _ => Name
        };
}

internal readonly record struct TraceMethodItem(
    string Namespace,
    EquatableArray<string> TypeChain,
    string TypeName,
    string MethodName,
    string SignatureKey,
    string DisplaySignature,
    string Modifiers,
    string HelperModifiers,
    string ReturnType,
    TraceReturnKind ReturnKind,
    string TypeParameters,
    string Constraints,
    EquatableArray<ParameterInfo> Parameters,
    string CoreName,
    string? ExplicitName,
    int ExplicitId,
    string? Category,
    LocationInfo? Location)
{
    internal Location? Origin => Location?.ToLocation();

    internal bool IsAsync => ReturnKind != TraceReturnKind.Void && ReturnKind != TraceReturnKind.Value;

    internal bool ReturnsValue => ReturnKind is TraceReturnKind.Value or TraceReturnKind.TaskOfT
        or TraceReturnKind.ValueTaskOfT;

    internal string TypeKey => Namespace + "|" + string.Join("|", TypeChain.Values);
}
