using EmberTrace.Extensions.Hosting.Configuration;
using EmberTrace.Extensions.Hosting.Hosting;
using EmberTrace.Extensions.Hosting.Recording;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

public static class EmberTraceServiceCollectionExtensions
{
    public static IServiceCollection AddEmberTrace(
        this IServiceCollection services,
        Action<EmberTraceOptions>? configure = null)
    {
        return services.AddEmberTrace(EmberTraceOptions.SectionName, configure);
    }

    public static IServiceCollection AddEmberTrace(
        this IServiceCollection services,
        string sectionName,
        Action<EmberTraceOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionName);

        var builder = services.AddOptions<EmberTraceOptions>();
        ClearBoundCollections(builder);
        builder.BindConfiguration(sectionName);
        Finish(services, builder, configure);
        return services;
    }

    public static IServiceCollection AddEmberTrace(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<EmberTraceOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration is IConfigurationSection existing
            ? existing
            : configuration.GetSection(EmberTraceOptions.SectionName);

        var builder = services.AddOptions<EmberTraceOptions>();
        ClearBoundCollections(builder);
        builder.Bind(section);
        Finish(services, builder, configure);
        return services;
    }

    private static void ClearBoundCollections(OptionsBuilder<EmberTraceOptions> builder)
    {
        builder.Configure(static options =>
        {
            options.EnabledCategories = [];
            options.DisabledCategories = [];
            options.Requests.IgnoredPaths = [];
        });
    }

    private static void Finish(
        IServiceCollection services,
        OptionsBuilder<EmberTraceOptions> builder,
        Action<EmberTraceOptions>? configure)
    {
        if (configure is not null)
            builder.Configure(configure);

        builder.PostConfigure(static options =>
        {
            if (options.Requests.IgnoredPaths.Length == 0)
                options.Requests.IgnoredPaths = EmberTraceRequestOptions.DefaultIgnoredPaths;
        });

        builder.ValidateOnStart();

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<EmberTraceOptions>, EmberTraceOptionsValidator>());
        services.TryAddSingleton<EmberTraceRecorder>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, EmberTraceHostedService>());

        HttpTraceIds.EnsureRegistered();
    }
}
