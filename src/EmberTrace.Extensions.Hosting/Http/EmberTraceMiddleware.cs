using System.Diagnostics;
using EmberTrace.Extensions.Hosting.Configuration;
using EmberTrace.Extensions.Hosting.Recording;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using ActivityFlow = EmberTrace.ActivityBridge.ActivityBridge;

namespace EmberTrace.Extensions.Hosting.Http;

public sealed class EmberTraceMiddleware
{
    internal const string FlowIdItemKey = "EmberTrace.FlowId";

    private readonly RequestDelegate _next;
    private readonly IOptionsMonitor<EmberTraceOptions> _options;

    public EmberTraceMiddleware(RequestDelegate next, IOptionsMonitor<EmberTraceOptions> options)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var requests = _options.CurrentValue.Requests;

        if (!requests.Enabled || !Tracer.IsRunning || IsIgnored(context.Request.Path, requests.IgnoredPaths))
        {
            await _next(context);
            return;
        }

        var id = ResolveId(context, requests);
        var flowId = requests.RecordFlow ? ResolveFlowId() : 0;

        if (flowId != 0)
        {
            context.Items[FlowIdItemKey] = flowId;
            Tracer.FlowStart(id, flowId);
        }

        await using (Tracer.ScopeAsync(id))
        {
            try
            {
                await _next(context);
            }
            finally
            {
                if (flowId != 0)
                    Tracer.FlowEnd(id, flowId);
            }
        }
    }

    private static int ResolveId(HttpContext context, EmberTraceRequestOptions requests)
    {
        var method = context.Request.Method;
        var fallback = string.Concat("HTTP ", method);

        if (requests.UseRoutePattern
            && context.GetEndpoint() is RouteEndpoint route
            && route.RoutePattern.RawText is { Length: > 0 } pattern)
            return HttpTraceIds.Resolve(
                string.Concat(method, " ", pattern),
                fallback,
                requests.Category,
                requests.MaxTrackedRoutes);

        return HttpTraceIds.Resolve(fallback, fallback, requests.Category, requests.MaxTrackedRoutes);
    }

    private static long ResolveFlowId()
    {
        var activity = Activity.Current;

        if (activity is { IdFormat: ActivityIdFormat.W3C })
        {
            var flowId = ActivityFlow.FlowIdFromTraceId(activity.TraceId.ToHexString());
            if (flowId != 0)
                return flowId;
        }

        return Tracer.NewFlowId();
    }

    private static bool IsIgnored(PathString path, string[] ignored)
    {
        for (var i = 0; i < ignored.Length; i++)
        {
            var candidate = ignored[i];
            if (string.IsNullOrWhiteSpace(candidate) || candidate[0] != '/')
                continue;

            if (path.StartsWithSegments(new PathString(candidate), StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
