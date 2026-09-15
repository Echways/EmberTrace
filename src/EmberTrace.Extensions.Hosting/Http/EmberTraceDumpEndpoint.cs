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
    private static readonly DumpFormat[] Formats =
    [
        new("ember", "application/octet-stream", TraceFormat.FileExtension, static (session, stream) =>
            TraceFormat.Write(session, stream)),
        new("chrome", "application/json", ".json", static (session, stream) =>
            TraceExport.WriteChromeComplete(session, stream, session.Metadata)),
        new("collapsed", "text/plain; charset=utf-8", ".folded", WriteCollapsed)
    ];

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

        var format = ResolveFormat(context.Request.Query["format"].ToString());
        if (format is null)
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

        using var buffer = new MemoryStream();
        format.Write(session, buffer);

        WriteHeaders(context.Response, session, options, format, buffer.Length);

        buffer.Position = 0;
        await buffer.CopyToAsync(context.Response.Body, context.RequestAborted);
    }

    private static DumpFormat? ResolveFormat(string requested)
    {
        var name = string.IsNullOrEmpty(requested) ? Formats[0].Name : requested;
        return Array.Find(Formats, format => string.Equals(format.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private static void WriteCollapsed(TraceSession session, Stream stream)
    {
        using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true);
        TraceText.WriteCollapsedStacks(session.Process(), writer);
    }

    internal static TimeSpan ResolveWindow(HttpRequest request, EmberTraceDumpOptions options)
    {
        var window = ParseWindow(request.Query["window"].ToString(), options.MaxWindow) ?? options.Window;

        if (window < TimeSpan.Zero)
            window = TimeSpan.Zero;

        return window > options.MaxWindow ? options.MaxWindow : window;
    }

    private static TimeSpan? ParseWindow(string raw, TimeSpan maxWindow)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
        {
            if (double.IsNaN(seconds))
                return null;

            if (seconds <= 0)
                return TimeSpan.Zero;

            return seconds >= maxWindow.TotalSeconds ? maxWindow : TimeSpan.FromSeconds(seconds);
        }

        return TimeSpan.TryParse(raw, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    private static void WriteHeaders(
        HttpResponse response,
        TraceSession session,
        EmberTraceDumpOptions options,
        DumpFormat format,
        long length)
    {
        var fileName = DumpFileName.Create(options.FileNamePrefix, null, DateTimeOffset.UtcNow, format.Extension);

        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = format.ContentType;
        response.ContentLength = length;
        response.Headers.ContentDisposition = $"attachment; filename=\"{fileName}\"";
        response.Headers["X-EmberTrace-Events"] =
            session.EventCount.ToString(CultureInfo.InvariantCulture);
        response.Headers["X-EmberTrace-Dropped"] =
            session.DroppedEvents.ToString(CultureInfo.InvariantCulture);
    }

    private static bool IsAllowedCaller(HttpContext context, EmberTraceDumpOptions options)
    {
        if (!options.RestrictToLoopback)
            return true;

        if (context.Request.Headers.ContainsKey("X-Forwarded-For") || context.Request.Headers.ContainsKey("Forwarded"))
            return false;

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

    private sealed record DumpFormat(string Name, string ContentType, string Extension, Action<TraceSession, Stream> Write);
}
