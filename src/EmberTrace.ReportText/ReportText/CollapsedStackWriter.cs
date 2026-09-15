using System.Globalization;
using System.Text;
using EmberTrace.Analysis.Model;
using EmberTrace.Metadata;

namespace EmberTrace.ReportText;

internal static class CollapsedStackWriter
{
    public static void Write(ProcessedTrace trace, TextWriter output, ITraceMetadataProvider? meta)
    {
        var path = new StringBuilder(256);

        foreach (var child in trace.GlobalRoot.Children)
            WriteNode(child, path, output, meta);
    }

    private static void WriteNode(CallTreeNode node, StringBuilder path, TextWriter output,
        ITraceMetadataProvider? meta)
    {
        var length = path.Length;
        if (length > 0)
            path.Append(';');

        meta.Resolve(node.Id, out var name, out _);
        AppendFrame(path, name);

        var microseconds = (long)Math.Round(node.ExclusiveMs * 1000.0);
        if (microseconds > 0)
        {
            output.Write(path);
            output.Write(' ');
            output.Write(microseconds.ToString(CultureInfo.InvariantCulture));
            output.Write('\n');
        }

        foreach (var child in node.Children)
            WriteNode(child, path, output, meta);

        path.Length = length;
    }

    private static void AppendFrame(StringBuilder path, string name)
    {
        foreach (var c in name)
            path.Append(c switch
            {
                ';' => ':',
                '\r' or '\n' => ' ',
                _ => c
            });
    }
}
