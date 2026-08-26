using EmberTrace.Extensions.Hosting.Configuration;
using EmberTrace.Extensions.Hosting.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.AspNetCore.Builder;

public static class EmberTraceEndpointRouteBuilderExtensions
{
    public static IEndpointConventionBuilder MapEmberTraceDump(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var options = endpoints.ServiceProvider
            .GetRequiredService<IOptions<EmberTraceOptions>>()
            .Value
            .Dump;

        RequestDelegate handler = EmberTraceDumpEndpoint.HandleAsync;
        var builder = endpoints.MapGet(options.Path, handler).WithDisplayName("EmberTrace dump");

        if (!string.IsNullOrWhiteSpace(options.AuthorizationPolicy))
            builder.RequireAuthorization(options.AuthorizationPolicy);

        return builder;
    }
}
