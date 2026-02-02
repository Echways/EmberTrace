using System;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace EmberTrace.ActivityBridge;

public static class ActivityBridge
{
    private static readonly ActivityAccess Access = new();

    [RequiresUnreferencedCode("Uses reflection to access Activity.Current and TraceId.")]
    public static bool TryGetCurrentFlowId(out long flowId)
    {
        if (!Access.TryGetCurrentTraceId(out var traceId))
        {
            flowId = 0;
            return false;
        }

        flowId = FlowIdFromTraceId(traceId);
        return flowId != 0;
    }

    internal static long FlowIdFromTraceId(string traceId)
    {
        if (string.IsNullOrWhiteSpace(traceId))
            return 0;

        unchecked
        {
            const ulong offset = 14695981039346656037;
            const ulong prime = 1099511628211;

            ulong hash = offset;
            for (int i = 0; i < traceId.Length; i++)
            {
                hash ^= traceId[i];
                hash *= prime;
            }

            hash &= 0x7FFFFFFFFFFFFFFF;
            if (hash == 0)
                hash = 1;

            return (long)hash;
        }
    }

    private sealed class ActivityAccess
    {
        private readonly PropertyInfo? _currentProperty;
        private readonly PropertyInfo? _traceIdProperty;
        private readonly bool _available;

        public ActivityAccess()
        {
            var type = Type.GetType("System.Diagnostics.Activity, System.Diagnostics.DiagnosticSource");
            if (type is null)
                return;

            _currentProperty = type.GetProperty("Current", BindingFlags.Public | BindingFlags.Static);
            _traceIdProperty = type.GetProperty("TraceId", BindingFlags.Public | BindingFlags.Instance);
            _available = _currentProperty is not null && _traceIdProperty is not null;
        }

        [RequiresUnreferencedCode("Uses reflection to access Activity.Current and TraceId.")]
        public bool TryGetCurrentTraceId(out string traceId)
        {
            traceId = string.Empty;
            if (!_available)
                return false;

            var activity = _currentProperty?.GetValue(null);
            if (activity is null)
                return false;

            var traceIdValue = _traceIdProperty?.GetValue(activity);
            if (traceIdValue is null)
                return false;

            traceId = traceIdValue.ToString() ?? string.Empty;
            return traceId.Length > 0;
        }
    }
}
