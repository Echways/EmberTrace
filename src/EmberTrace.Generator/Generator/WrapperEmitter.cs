using System.Collections.Immutable;
using System.Globalization;
using System.Text;

namespace EmberTrace.Generator.Generator;

internal static class WrapperEmitter
{
    private const string ScopeVariable = "__emberTraceScope";
    private const string HelperSuffix = "__EmberTraceTraced";

    internal static string HintName(TraceMethodItem item)
    {
        var sb = new StringBuilder("EmberTrace.Trace.");

        foreach (var c in item.TypeKey)
            sb.Append(char.IsLetterOrDigit(c) ? c : '_');

        return sb.Append(".g.cs").ToString();
    }

    internal static string Render(ImmutableArray<ResolvedTraceMethod> methods)
    {
        var first = methods[0].Item;
        var sb = new StringBuilder();

        sb.AppendLine("#nullable enable");
        sb.AppendLine();

        var indent = 0;

        if (first.Namespace.Length > 0)
        {
            sb.Append("namespace ").AppendLine(first.Namespace);
            sb.AppendLine("{");
            indent = 1;
        }

        foreach (var header in first.TypeChain.Values)
        {
            Indent(sb, indent).AppendLine(header);
            Indent(sb, indent).AppendLine("{");
            indent++;
        }

        var separate = false;
        foreach (var method in methods)
        {
            if (separate)
                sb.AppendLine();

            separate = true;
            RenderMember(sb, indent, method);
        }

        for (var i = first.TypeChain.Values.Length - 1; i >= 0; i--)
        {
            indent--;
            Indent(sb, indent).AppendLine("}");
        }

        if (first.Namespace.Length > 0)
            sb.AppendLine("}");

        return sb.ToString();
    }

    internal static void RenderMember(StringBuilder sb, int indent, ResolvedTraceMethod method)
    {
        if (method.Item.IsAsync)
            RenderAsync(sb, indent, method);
        else
            RenderSync(sb, indent, method);
    }

    internal static void RenderHeader(StringBuilder sb, int indent, string modifiers, TraceMethodItem item)
    {
        Indent(sb, indent)
            .Append(modifiers).Append(' ')
            .Append(item.ReturnType).Append(' ')
            .Append(item.MethodName).Append(item.TypeParameters)
            .Append('(').Append(Declarations(item)).Append(')')
            .AppendLine(item.Constraints);
    }

    internal static string Declarations(TraceMethodItem item)
    {
        return string.Join(", ", item.Parameters.Values.Select(p => p.Declaration));
    }

    internal static string Arguments(TraceMethodItem item)
    {
        return string.Join(", ", item.Parameters.Values.Select(p => p.Argument));
    }

    internal static string Call(TraceMethodItem item, string target)
    {
        return target + item.TypeParameters + "(" + Arguments(item) + ")";
    }

    internal static StringBuilder Indent(StringBuilder sb, int level)
    {
        return sb.Append(' ', level * 4);
    }

    private static void RenderSync(StringBuilder sb, int indent, ResolvedTraceMethod method)
    {
        var item = method.Item;

        RenderHeader(sb, indent, item.Modifiers, item);
        Indent(sb, indent).AppendLine("{");
        Indent(sb, indent + 1)
            .Append("using var ").Append(ScopeVariable)
            .Append(" = global::EmberTrace.Tracer.Scope(")
            .Append(method.Id.ToString(CultureInfo.InvariantCulture)).AppendLine(");");
        Indent(sb, indent + 1)
            .Append(item.ReturnsValue ? "return " : string.Empty)
            .Append(Call(item, item.CoreName)).AppendLine(";");
        Indent(sb, indent).AppendLine("}");
    }

    private static void RenderAsync(StringBuilder sb, int indent, ResolvedTraceMethod method)
    {
        var item = method.Item;
        var helper = item.MethodName + HelperSuffix;

        RenderHeader(sb, indent, item.Modifiers, item);
        Indent(sb, indent + 1)
            .Append("=> global::EmberTrace.Tracer.IsRunning ? ")
            .Append(Call(item, helper))
            .Append(" : ")
            .Append(Call(item, item.CoreName)).AppendLine(";");
        sb.AppendLine();
        Indent(sb, indent).AppendLine("[global::System.Diagnostics.DebuggerNonUserCode]");
        Indent(sb, indent)
            .Append(item.HelperModifiers).Append(' ')
            .Append(item.ReturnType).Append(' ')
            .Append(helper).Append(item.TypeParameters)
            .Append('(').Append(Declarations(item)).Append(')')
            .AppendLine(item.Constraints);
        Indent(sb, indent).AppendLine("{");
        Indent(sb, indent + 1)
            .Append("await using var ").Append(ScopeVariable)
            .Append(" = global::EmberTrace.Tracer.ScopeAsync(")
            .Append(method.Id.ToString(CultureInfo.InvariantCulture)).AppendLine(");");
        Indent(sb, indent + 1)
            .Append(item.ReturnsValue ? "return await " : "await ")
            .Append(Call(item, item.CoreName)).AppendLine(".ConfigureAwait(false);");
        Indent(sb, indent).AppendLine("}");
    }
}
