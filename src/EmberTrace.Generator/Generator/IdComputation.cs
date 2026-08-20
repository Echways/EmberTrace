using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace EmberTrace.Generator.Generator;

internal static class IdComputation
{
    internal static string NormalizeConstName(string name, int id)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "TraceId_" + id.ToString(CultureInfo.InvariantCulture);

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
            return "TraceId_" + id.ToString(CultureInfo.InvariantCulture);

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

    internal static ImmutableArray<TraceConstant> ResolveConstants(ImmutableArray<TraceItem> items, SourceProductionContext spc)
    {
        var builder = ImmutableArray.CreateBuilder<TraceConstant>(items.Length);
        var owners = new Dictionary<string, string>(StringComparer.Ordinal);
        var used = new HashSet<string>(StringComparer.Ordinal);

        foreach (var item in items)
        {
            var normalized = NormalizeConstName(item.Name!, item.Id);

            var name = normalized;
            for (var suffix = 2; !used.Add(name); suffix++)
                name = normalized + "_" + suffix.ToString(CultureInfo.InvariantCulture);

            if (owners.TryGetValue(normalized, out var owner))
                spc.ReportDiagnostic(Diagnostic.Create(Diagnostics.ConflictingConstantName, item.Origin, owner, item.Name, normalized, name));
            else
                owners.Add(normalized, item.Name!);

            builder.Add(new TraceConstant(name, item.Id));
        }

        return builder.ToImmutable();
    }
}
