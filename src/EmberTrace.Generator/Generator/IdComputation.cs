using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis.CSharp;

namespace EmberTrace.Generator.Generator;

internal static class IdComputation
{
    internal static string NormalizeConstName(string name, int id)
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

    internal static string EnsureUniqueName(string baseName, HashSet<string> used, Dictionary<string, int> counters)
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
}
