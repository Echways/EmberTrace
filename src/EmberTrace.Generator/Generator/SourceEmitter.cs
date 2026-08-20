using System.Collections.Immutable;
using System.Text;

namespace EmberTrace.Generator.Generator;

internal static class SourceEmitter
{
    internal static string RenderProvider(ImmutableArray<TraceItem> items)
    {
        var sb = new StringBuilder();

        sb.AppendLine("using System.Collections;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine();
        sb.AppendLine("namespace EmberTrace.Internal.Metadata");
        sb.AppendLine("{");
        sb.AppendLine(
            "    internal sealed class GeneratedTraceMetadataProvider : global::EmberTrace.Metadata.ITraceMetadataProvider, IEnumerable<global::EmberTrace.Metadata.TraceMeta>");
        sb.AppendLine("    {");
        sb.AppendLine(
            "        private static readonly Dictionary<int, global::EmberTrace.Metadata.TraceMeta> Map = new()");
        sb.AppendLine("        {");

        var seen = new HashSet<int>();
        foreach (var item in items)
        {
            if (!seen.Add(item.Id))
                continue;

            sb.Append("            [");
            sb.Append(item.Id);
            sb.Append("] = new global::EmberTrace.Metadata.TraceMeta(");
            sb.Append(item.Id);
            sb.Append(", ");
            sb.Append(NameFormatting.Escape(item.Name!));
            sb.Append(", ");
            sb.Append(item.Category is null ? "null" : NameFormatting.Escape(item.Category));
            sb.AppendLine("),");
        }

        sb.AppendLine("        };");
        sb.AppendLine();
        sb.AppendLine(
            "        public bool TryGet(int id, out global::EmberTrace.Metadata.TraceMeta meta) => Map.TryGetValue(id, out meta);");
        sb.AppendLine();
        sb.AppendLine(
            "        public IEnumerator<global::EmberTrace.Metadata.TraceMeta> GetEnumerator() => Map.Values.GetEnumerator();");
        sb.AppendLine();
        sb.AppendLine("        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    internal static class EmberTraceMetadataModuleInitializer");
        sb.AppendLine("    {");
        sb.AppendLine("        [System.Runtime.CompilerServices.ModuleInitializer]");
        sb.AppendLine("        internal static void Init()");
        sb.AppendLine("        {");
        sb.AppendLine(
            "            global::EmberTrace.Metadata.TraceMetadata.Register(new GeneratedTraceMetadataProvider());");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    internal static string RenderTraceIds(ImmutableArray<TraceConstant> constants)
    {
        var sb = new StringBuilder();

        sb.AppendLine("namespace EmberTrace");
        sb.AppendLine("{");
        sb.AppendLine("    public static class TraceIds");
        sb.AppendLine("    {");

        foreach (var constant in constants)
        {
            sb.Append("        public const int ");
            sb.Append(constant.Name);
            sb.Append(" = ");
            sb.Append(constant.Id);
            sb.AppendLine(";");
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }
}