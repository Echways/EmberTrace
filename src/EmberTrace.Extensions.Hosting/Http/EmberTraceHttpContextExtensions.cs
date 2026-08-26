using Microsoft.AspNetCore.Http;

namespace EmberTrace.Extensions.Hosting.Http;

public static class EmberTraceHttpContextExtensions
{
    public static long GetEmberTraceFlowId(this HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.Items.TryGetValue(EmberTraceMiddleware.FlowIdItemKey, out var value) && value is long flowId
            ? flowId
            : 0;
    }
}
