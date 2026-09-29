using System.Diagnostics;
using EmberTrace.Extensions.Hosting.Configuration;
using EmberTrace.Extensions.Hosting.Recording;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
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

        var options = _options.CurrentValue;
        var requests = options.Requests;

        if (!requests.Enabled || !Tracer.IsRunning || IsIgnored(context.Request.Path, options))
        {
            await _next(context);
            return;
        }

        var started = Stopwatch.GetTimestamp();
        var id = ResolveId(context, requests);
        var flowId = requests.RecordFlow ? ResolveFlowId() : 0;

        if (flowId != 0)
        {
            context.Items[FlowIdItemKey] = flowId;
            Tracer.FlowStart(id, flowId);
        }

        try
        {
            await InvokeTracedAsync(context, id, flowId);
        }
        finally
        {
            CaptureIfSlow(context, options, id, started);
        }
    }

    private async Task InvokeTracedAsync(HttpContext context, int id, long flowId)
    {
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

    private static void CaptureIfSlow(HttpContext context, EmberTraceOptions options, int id, long started)
    {
        var elapsed = Stopwatch.GetElapsedTime(started);
        if (!options.SlowRequests.Enabled || elapsed < options.SlowRequests.Threshold)
            return;

        var request = HttpTraceIds.Provider.TryGet(id, out var meta) ? meta.Name : context.Request.Path.Value ?? "/";
        context.RequestServices?.GetService<SlowRequestCapture>()?.TryCapture(options, request, started, elapsed);
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
        return ActivityFlow.TryGetCurrentFlowId(out var flowId) ? flowId : Tracer.NewFlowId();
    }

    private static bool IsIgnored(PathString path, EmberTraceOptions options)
    {
        var dump = options.Dump;
        if (dump.Enabled && path.StartsWithSegments(new PathString(dump.Path), StringComparison.OrdinalIgnoreCase))
            return true;

        var ignored = options.Requests.IgnoredPaths;
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
