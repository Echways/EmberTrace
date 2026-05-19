using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace EmberTrace.Generator.Generator;

internal static class TraceIdsGenerator
{
    internal static void TryEmit(SourceProductionContext spc, List<TraceItem> items, Microsoft.CodeAnalysis.Diagnostics.AnalyzerConfigOptionsProvider options)
    {
        if (items.Count == 0)
            return;

        if (!NameFormatting.GetBoolOption(options.GlobalOptions, "build_property.EmberTraceGenerateTraceIds"))
            return;

        var src = SourceEmitter.RenderTraceIds(items);
        spc.AddSource("TraceIds.g.cs", SourceText.From(src, Encoding.UTF8));
    }
}
