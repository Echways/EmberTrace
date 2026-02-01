using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace EmberTrace.Generator.Generator;

[Generator]
public sealed class TraceMetadataGenerator : IIncrementalGenerator
{
    private static readonly DiagnosticDescriptor DuplicateIdDiagnostic = new(
        "ETG001",
        "Duplicate TraceId",
        "Duplicate TraceId '{0}' already used by '{1}'",
        "EmberTrace.Generator",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor EmptyNameDiagnostic = new(
        "ETG002",
        "Empty TraceId name",
        "TraceId '{0}' has an empty name",
        "EmberTrace.Generator",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor EmptyCategoryDiagnostic = new(
        "ETG003",
        "Empty TraceId category",
        "TraceId '{0}' has an empty category",
        "EmberTrace.Generator",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var compilationAndOptions = context.CompilationProvider.Combine(context.AnalyzerConfigOptionsProvider);

        context.RegisterSourceOutput(compilationAndOptions, static (spc, pair) =>
        {
            var (compilation, options) = pair;
            var items = Collect(compilation, spc);
            var src = RenderProvider(items);
            spc.AddSource("EmberTrace.GeneratedTraceMetadataProvider.g.cs", SourceText.From(src, Encoding.UTF8));

            if (items.Count > 0 && GetBoolOption(options.GlobalOptions, "build_property.EmberTraceGenerateTraceIds"))
            {
                var traceIdsSrc = RenderTraceIds(items);
                spc.AddSource("TraceIds.g.cs", SourceText.From(traceIdsSrc, Encoding.UTF8));
            }
        });
    }

    private static List<TraceItem> Collect(Compilation compilation, SourceProductionContext spc)
    {
        var list = new List<TraceItem>();
        var seen = new Dictionary<int, string>();

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
            var name = attr.ConstructorArguments[1].Value as string ?? string.Empty;

            var location = attr.ApplicationSyntaxReference?.GetSyntax().GetLocation();
            if (string.IsNullOrWhiteSpace(name))
                spc.ReportDiagnostic(Diagnostic.Create(EmptyNameDiagnostic, location, id));

            string? category = null;
            if (attr.ConstructorArguments.Length >= 3)
                category = attr.ConstructorArguments[2].Value as string;

            if (category is not null && string.IsNullOrWhiteSpace(category))
                spc.ReportDiagnostic(Diagnostic.Create(EmptyCategoryDiagnostic, location, id));

            if (seen.TryGetValue(id, out var existing))
                spc.ReportDiagnostic(Diagnostic.Create(DuplicateIdDiagnostic, location, id, existing));
            else
                seen.Add(id, name);

            list.Add(new TraceItem(id, name, category, location));
        }

        return list;
    }

    private static string RenderProvider(List<TraceItem> items)
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

    private static string RenderTraceIds(List<TraceItem> items)
    {
        var sb = new StringBuilder();

        sb.AppendLine("namespace EmberTrace");
        sb.AppendLine("{");
        sb.AppendLine("    public static class TraceIds");
        sb.AppendLine("    {");

        var used = new HashSet<string>(StringComparer.Ordinal);
        var counters = new Dictionary<string, int>(StringComparer.Ordinal);

        for (int i = 0; i < items.Count; i++)
        {
            var it = items[i];
            var baseName = NormalizeConstName(it.Name, it.Id);
            var name = EnsureUniqueName(baseName, used, counters);

            sb.Append("        public const int ");
            sb.Append(name);
            sb.Append(" = ");
            sb.Append(it.Id);
            sb.AppendLine(";");
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private static string EnsureUniqueName(string baseName, HashSet<string> used, Dictionary<string, int> counters)
    {
        if (used.Add(baseName))
        {
            counters[baseName] = 1;
            return baseName;
        }

        var i = counters.TryGetValue(baseName, out var current) ? current : 1;
        string candidate;
        do
        {
            i++;
            candidate = baseName + "_" + i.ToString();
        } while (!used.Add(candidate));

        counters[baseName] = i;
        return candidate;
    }

    private static string NormalizeConstName(string name, int id)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "TraceId_" + id.ToString();

        var sb = new StringBuilder(name.Length);
        var newToken = true;

        for (int i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (c <= 127 && char.IsLetterOrDigit(c))
            {
                if (newToken)
                {
                    sb.Append(char.IsLetter(c) ? char.ToUpperInvariant(c) : c);
                    newToken = false;
                }
                else
                {
                    sb.Append(c);
                }
            }
            else
            {
                newToken = true;
            }
        }

        if (sb.Length == 0)
            return "TraceId_" + id.ToString();

        if (char.IsDigit(sb[0]))
            sb.Insert(0, '_');

        var candidate = sb.ToString();
        if (SyntaxFacts.GetKeywordKind(candidate) != SyntaxKind.None
            || SyntaxFacts.GetContextualKeywordKind(candidate) != SyntaxKind.None)
        {
            candidate = "_" + candidate;
        }

        return candidate;
    }

    private static string Escape(string s)
    {
        return "@\"" + s.Replace("\"", "\"\"") + "\"";
    }

    private static bool GetBoolOption(AnalyzerConfigOptions options, string key)
    {
        return options.TryGetValue(key, out var value)
            && bool.TryParse(value, out var enabled)
            && enabled;
    }

    private readonly struct TraceItem
    {
        public TraceItem(int id, string name, string? category, Location? location)
        {
            Id = id;
            Name = name;
            Category = category;
            Location = location;
        }

        public int Id { get; }
        public string Name { get; }
        public string? Category { get; }
        public Location? Location { get; }
    }
}
