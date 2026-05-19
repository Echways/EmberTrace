using Microsoft.CodeAnalysis.Diagnostics;

namespace EmberTrace.Generator.Generator;

internal static class NameFormatting
{
    internal static string Escape(string s) => "@\"" + s.Replace("\"", "\"\"") + "\"";

    internal static bool GetBoolOption(AnalyzerConfigOptions options, string key)
        => options.TryGetValue(key, out var value)
           && bool.TryParse(value, out var enabled)
           && enabled;
}
