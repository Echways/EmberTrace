using System.Collections.Immutable;
using System.Text;

namespace EmberTrace.Generator.Generator;

internal static class DecoratorEmitter
{
    internal static string HintName(DecoratorItem item)
    {
        var sb = new StringBuilder("EmberTrace.Decorator.");

        foreach (var c in item.Namespace + "_" + item.DecoratorName + item.TypeParameters)
            sb.Append(char.IsLetterOrDigit(c) ? c : '_');

        return sb.Append(".g.cs").ToString();
    }

    internal static string Render(DecoratorItem item, ImmutableArray<ResolvedTraceMethod> methods,
        bool emitRegistration)
    {
        var sb = new StringBuilder();

        sb.AppendLine("#nullable enable");
        sb.AppendLine();

        var indent = 0;

        if (item.Namespace.Length > 0)
        {
            sb.Append("namespace ").AppendLine(item.Namespace);
            sb.AppendLine("{");
            indent = 1;
        }

        WrapperEmitter.Indent(sb, indent)
            .Append(item.Accessibility).Append(" sealed class ")
            .Append(item.DecoratorName).Append(item.TypeParameters)
            .Append(" : ").Append(item.InterfaceType)
            .AppendLine(item.Constraints);
        WrapperEmitter.Indent(sb, indent).AppendLine("{");

        WrapperEmitter.Indent(sb, indent + 1)
            .Append("private readonly ").Append(item.InterfaceType).AppendLine(" _inner;");
        sb.AppendLine();
        WrapperEmitter.Indent(sb, indent + 1)
            .Append("public ").Append(item.DecoratorName)
            .Append('(').Append(item.InterfaceType).AppendLine(" inner)");
        WrapperEmitter.Indent(sb, indent + 1).AppendLine("{");
        WrapperEmitter.Indent(sb, indent + 2).AppendLine("_inner = inner;");
        WrapperEmitter.Indent(sb, indent + 1).AppendLine("}");

        foreach (var method in methods)
        {
            sb.AppendLine();
            WrapperEmitter.RenderMember(sb, indent + 1, method);
        }

        foreach (var member in item.Forwarded.Values)
        {
            sb.AppendLine();
            WrapperEmitter.Indent(sb, indent + 1).AppendLine(member);
        }

        WrapperEmitter.Indent(sb, indent).AppendLine("}");

        if (emitRegistration)
            RenderRegistration(sb, indent, item);

        if (item.Namespace.Length > 0)
            sb.AppendLine("}");

        return sb.ToString();
    }

    private static void RenderRegistration(StringBuilder sb, int indent, DecoratorItem item)
    {
        sb.AppendLine();
        WrapperEmitter.Indent(sb, indent)
            .Append(item.Accessibility).Append(" static class ")
            .Append(item.DecoratorName).AppendLine("Registration");
        WrapperEmitter.Indent(sb, indent).AppendLine("{");
        WrapperEmitter.Indent(sb, indent + 1)
            .Append("public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection Add")
            .Append(item.DecoratorName).Append(item.TypeParameters)
            .AppendLine("(")
            .Append(' ', (indent + 3) * 4)
            .AppendLine("this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services,")
            .Append(' ', (indent + 3) * 4)
            .Append("global::Microsoft.Extensions.DependencyInjection.ServiceLifetime lifetime")
            .AppendLine(" = global::Microsoft.Extensions.DependencyInjection.ServiceLifetime.Scoped)")
            .Append(' ', (indent + 1) * 4)
            .AppendLine(item.Constraints.Length == 0 ? string.Empty : item.Constraints.TrimStart());
        WrapperEmitter.Indent(sb, indent + 1).AppendLine("{");
        WrapperEmitter.Indent(sb, indent + 2)
            .Append("services.Add(new global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor(")
            .Append("typeof(").Append(item.InterfaceType).AppendLine("),");
        WrapperEmitter.Indent(sb, indent + 3)
            .Append("provider => new ").Append(item.DecoratorName).Append(item.TypeParameters)
            .Append("(global::Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance<")
            .Append(item.ClassName).Append(item.TypeParameters).AppendLine(">(provider)),");
        WrapperEmitter.Indent(sb, indent + 3).AppendLine("lifetime));");
        WrapperEmitter.Indent(sb, indent + 2).AppendLine("return services;");
        WrapperEmitter.Indent(sb, indent + 1).AppendLine("}");
        WrapperEmitter.Indent(sb, indent).AppendLine("}");
    }
}
