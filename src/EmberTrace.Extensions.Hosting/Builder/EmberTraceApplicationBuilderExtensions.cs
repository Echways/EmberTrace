using EmberTrace.Extensions.Hosting.Http;

namespace Microsoft.AspNetCore.Builder;

public static class EmberTraceApplicationBuilderExtensions
{
    public static IApplicationBuilder UseEmberTrace(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.UseMiddleware<EmberTraceMiddleware>();
    }
}
