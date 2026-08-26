using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using EmberTrace.Extensions.Hosting.Configuration;
using EmberTrace.Extensions.Hosting.Recording;
using EmberTrace.Sessions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EmberTrace.Extensions.Hosting.Http;

internal static class EmberTraceDumpEndpoint
{
    private const string ChromeFormat = "chrome";
    private const string EmberFormat = "ember";

    public static async Task HandleAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var options = context.RequestServices
            .GetRequiredService<IOptionsMonitor<EmberTraceOptions>>()
            .CurrentValue
            .Dump;

        if (!options.Enabled || !IsAllowedCaller(context, options))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        if (!HasValidApiKey(context, options))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var format = context.Request.Query["format"].ToString();
        if (string.IsNullOrEmpty(format))
            format = EmberFormat;

        if (!string.Equals(format, EmberFormat, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(format, ChromeFormat, StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var recorder = context.RequestServices.GetRequiredService<EmberTraceRecorder>();
        if (!recorder.IsRunning)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return;
        }

        var session = recorder.Snapshot(ResolveWindow(context.Request, options));
        var chrome = string.Equals(format, ChromeFormat, StringComparison.OrdinalIgnoreCase);

        using var buffer = new MemoryStream();
        if (chrome)
            TraceExport.WriteChromeComplete(session, buffer, session.Metadata);
        else
            TraceFormat.Write(session, buffer);

        WriteHeaders(context.Response, session, options, chrome, buffer.Length);

        buffer.Position = 0;
        await buffer.CopyToAsync(context.Response.Body, context.RequestAborted);
    }

    internal static TimeSpan ResolveWindow(HttpRequest request, EmberTraceDumpOptions options)
    {
        var window = options.Window;
        var raw = request.Query["window"].ToString();

        if (!string.IsNullOrWhiteSpace(raw))
        {
            if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
                window = TimeSpan.FromSeconds(seconds);
            else if (TimeSpan.TryParse(raw, CultureInfo.InvariantCulture, out var parsed))
                window = parsed;
        }

        if (window < TimeSpan.Zero)
            window = TimeSpan.Zero;

        return window > options.MaxWindow ? options.MaxWindow : window;
    }

    private static void WriteHeaders(
        HttpResponse response,
        TraceSession session,
        EmberTraceDumpOptions options,
        bool chrome,
        long length)
    {
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var extension = chrome ? ".json" : TraceFormat.FileExtension;

        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = chrome ? "application/json" : "application/octet-stream";
        response.ContentLength = length;
        response.Headers.ContentDisposition =
            $"attachment; filename=\"{options.FileNamePrefix}-{stamp}{extension}\"";
        response.Headers["X-EmberTrace-Events"] =
            session.EventCount.ToString(CultureInfo.InvariantCulture);
        response.Headers["X-EmberTrace-Dropped"] =
            session.DroppedEvents.ToString(CultureInfo.InvariantCulture);
    }

    private static bool IsAllowedCaller(HttpContext context, EmberTraceDumpOptions options)
    {
        if (!options.RestrictToLoopback)
            return true;

        var address = context.Connection.RemoteIpAddress;
        if (address is null)
            return true;

        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        return IPAddress.IsLoopback(address);
    }

    private static bool HasValidApiKey(HttpContext context, EmberTraceDumpOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
            return true;

        var provided = context.Request.Headers[EmberTraceDumpOptions.ApiKeyHeader].ToString();
        if (string.IsNullOrEmpty(provided))
            return false;

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(provided),
            Encoding.UTF8.GetBytes(options.ApiKey));
    }
}
