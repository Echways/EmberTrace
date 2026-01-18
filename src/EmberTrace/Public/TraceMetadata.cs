using System.Reflection;

namespace EmberTrace.Public;

public readonly record struct TraceMeta(int Id, string Name, string? Category);

public static class TraceMetadata
{
    public static ITraceMetadataProvider CreateDefault()
    {
        var asm = Assembly.GetEntryAssembly();
        if (asm is not null)
        {
            var t = asm.GetType("EmberTrace.Internal.Metadata.GeneratedTraceMetadataProvider", throwOnError: false);
            if (t is not null && Activator.CreateInstance(t) is ITraceMetadataProvider p)
                return p;
        }

        return new DictionaryTraceMetadataProvider();
    }
}
