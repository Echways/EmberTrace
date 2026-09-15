using System.Diagnostics;

namespace EmberTrace.ActivityBridge;

public static class ActivityBridge
{
    private const int TraceIdBytes = 16;

    public static bool TryGetCurrentFlowId(out long flowId)
    {
        var activity = Activity.Current;
        flowId = activity is { IdFormat: ActivityIdFormat.W3C } ? FlowIdFromTraceId(activity.TraceId) : 0;
        return flowId != 0;
    }

    public static long FlowIdFromTraceId(ActivityTraceId traceId)
    {
        Span<byte> bytes = stackalloc byte[TraceIdBytes];
        Span<char> hex = stackalloc char[TraceIdBytes * 2];

        traceId.CopyTo(bytes);
        Convert.TryToHexStringLower(bytes, hex, out _);

        return Hash(hex);
    }

    public static long FlowIdFromTraceId(string traceId)
    {
        return string.IsNullOrWhiteSpace(traceId) ? 0 : Hash(traceId);
    }

    private static long Hash(ReadOnlySpan<char> traceId)
    {
        unchecked
        {
            const ulong offset = 14695981039346656037;
            const ulong prime = 1099511628211;

            var hash = offset;
            foreach (var c in traceId)
            {
                hash ^= c;
                hash *= prime;
            }

            hash &= 0x7FFFFFFFFFFFFFFF;
            return (long)(hash == 0 ? 1 : hash);
        }
    }
}
