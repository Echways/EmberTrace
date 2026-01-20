using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace EmberTrace.Generator.Generator;

[Generator]
public sealed class TraceMetadataGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var compilationProvider = context.CompilationProvider;

        context.RegisterSourceOutput(compilationProvider, static (spc, compilation) =>
        {
            var items = Collect(compilation);
            var src = Render(items);
            spc.AddSource("EmberTrace.GeneratedTraceMetadataProvider.g.cs", SourceText.From(src, Encoding.UTF8));
        });
    }

    private static List<(int Id, string Name, string? Category)> Collect(Compilation compilation)
    {
        var list = new List<(int, string, string?)>();

        foreach (var attr in compilation.Assembly.GetAttributes())
        {
            var a = attr.AttributeClass;
            if (a is null) continue;

            var fullName = a.ToDisplayString();
            if (fullName != "EmberTrace.Abstractions.Attributes.TraceIdAttribute"
                && fullName != "EmberTrace.Abstractions.TraceIdAttribute")
                continue;

            if (attr.ConstructorArguments.Length < 2)
                continue;

            var id = (int)attr.ConstructorArguments[0].Value!;
            var name = (string)attr.ConstructorArguments[1].Value!;

            string? category = null;
            if (attr.ConstructorArguments.Length >= 3)
                category = attr.ConstructorArguments[2].Value as string;

            list.Add((id, name, category));
        }

        return list;
    }

    private static string Render(List<(int Id, string Name, string? Category)> items)
    {
        var sb = new StringBuilder();

        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine();
        sb.AppendLine("namespace EmberTrace.Internal.Metadata");
        sb.AppendLine("{");
        sb.AppendLine("    internal sealed class GeneratedTraceMetadataProvider : global::EmberTrace.Metadata.ITraceMetadataProvider");
        sb.AppendLine("    {");
        sb.AppendLine("        private static readonly Dictionary<int, global::EmberTrace.Metadata.TraceMeta> Map = new()");
        sb.AppendLine("        {");

        var seen = new HashSet<int>();
        for (int i = 0; i < items.Count; i++)
        {
            var it = items[i];
            if (!seen.Add(it.Id))
                continue;

            sb.Append("            [");
            sb.Append(it.Id);
            sb.Append("] = new global::EmberTrace.Metadata.TraceMeta(");
            sb.Append(it.Id);
            sb.Append(", ");
            sb.Append(Escape(it.Name));
            sb.Append(", ");
            sb.Append(it.Category is null ? "null" : Escape(it.Category));
            sb.AppendLine("),");
        }

        sb.AppendLine("        };");
        sb.AppendLine();
        sb.AppendLine("        public bool TryGet(int id, out global::EmberTrace.Metadata.TraceMeta meta) => Map.TryGetValue(id, out meta);");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    internal static class EmberTraceMetadataModuleInitializer");
        sb.AppendLine("    {");
        sb.AppendLine("        [System.Runtime.CompilerServices.ModuleInitializer]");
        sb.AppendLine("        internal static void Init()");
        sb.AppendLine("        {");
        sb.AppendLine("            global::EmberTrace.Metadata.TraceMetadata.Register(new GeneratedTraceMetadataProvider());");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private static string Escape(string s)
    {
        return "@\"" + s.Replace("\"", "\"\"") + "\"";
    }
}
