using System.Collections.Immutable;
using EmberTrace.Internal;

namespace EmberTrace.Generator.Generator;

internal readonly record struct ResolvedTraceMethod(TraceMethodItem Item, int Id, string Name, string? Category);

internal static class TraceMethodNaming
{
    internal static ImmutableArray<ResolvedTraceMethod> Resolve(ImmutableArray<TraceMethodItem> items)
    {
        var ordered = items
            .OrderBy(item => item.TypeKey, StringComparer.Ordinal)
            .ThenBy(item => item.SignatureKey, StringComparer.Ordinal)
            .ToImmutableArray();

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var item in ordered)
        {
            var key = item.TypeKey + "|" + item.MethodName;
            counts.TryGetValue(key, out var count);
            counts[key] = count + 1;
        }

        var builder = ImmutableArray.CreateBuilder<ResolvedTraceMethod>(ordered.Length);

        foreach (var item in ordered)
        {
            var name = item.ExplicitName;

            if (name is null)
            {
                name = item.TypeName + "." + item.MethodName;
                if (counts[item.TypeKey + "|" + item.MethodName] > 1)
                    name += item.DisplaySignature;
            }

            var id = item.ExplicitId != 0 ? item.ExplicitId : TraceIds.Stable(name);
            builder.Add(new ResolvedTraceMethod(item, id, name, item.Category));
        }

        return builder.ToImmutable();
    }
}
