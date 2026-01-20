using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using EmberTrace.Metadata;
using EmberTrace.Sessions;

namespace EmberTrace;

public static class TraceExport
{
    public static void WriteChromeComplete(
        TraceSession session,
        Stream output,
        ITraceMetadataProvider? meta = null,
        bool sortByStartTimestamp = true,
        int pid = 1,
        string processName = "EmberTrace")
    {
        Export.ChromeTraceExporter.WriteComplete(session, output, meta, sortByStartTimestamp, pid, processName);
    }

    public static void WriteChromeBeginEnd(
        TraceSession session,
        Stream output,
        ITraceMetadataProvider? meta = null,
        bool sortByTimestamp = true,
        int pid = 1,
        string processName = "EmberTrace")
    {
        Export.ChromeTraceExporter.WriteBeginEnd(session, output, meta, sortByTimestamp, pid, processName);
    }

    public static TraceSession MarkedComplete(string name, string outputPath, Action body)
    {
        if (name is null) throw new ArgumentNullException(nameof(name));
        if (outputPath is null) throw new ArgumentNullException(nameof(outputPath));
        if (body is null) throw new ArgumentNullException(nameof(body));

        if (Tracer.IsRunning)
            throw new InvalidOperationException("Tracer session is already running.");

        var id = Tracer.Id(name);
        var meta = CreateOverlayMeta(id, name);

        Exception? error = null;
        TraceSession session;

        Tracer.Start();
        try
        {
            using (Tracer.Scope(id))
                body();
        }
        catch (Exception ex)
        {
            error = ex;
        }
        finally
        {
            session = Tracer.Stop();
        }

        EnsureDir(outputPath);
        using (var fs = File.Create(outputPath))
            WriteChromeComplete(session, fs, meta: meta);

        if (error is not null)
            throw error;

        return session;
    }

    public static async Task<TraceSession> MarkedCompleteAsync(string name, string outputPath, Func<Task> body)
    {
        if (name is null) throw new ArgumentNullException(nameof(name));
        if (outputPath is null) throw new ArgumentNullException(nameof(outputPath));
        if (body is null) throw new ArgumentNullException(nameof(body));

        if (Tracer.IsRunning)
            throw new InvalidOperationException("Tracer session is already running.");

        var id = Tracer.Id(name);
        var meta = CreateOverlayMeta(id, name);

        Exception? error = null;
        TraceSession session;

        Tracer.Start();
        try
        {
            await using (Tracer.ScopeAsync(id))
                await body().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            error = ex;
        }
        finally
        {
            session = Tracer.Stop();
        }

        EnsureDir(outputPath);
        using (var fs = File.Create(outputPath))
            WriteChromeComplete(session, fs, meta: meta);

        if (error is not null)
            throw error;

        return session;
    }

    public static TraceSession MarkedComplete(string name, Action body)
    {
        var path = DefaultTracePath(name);
        return MarkedComplete(name, path, body);
    }

    public static Task<TraceSession> MarkedCompleteAsync(string name, Func<Task> body)
    {
        var path = DefaultTracePath(name);
        return MarkedCompleteAsync(name, path, body);
    }

    static ITraceMetadataProvider CreateOverlayMeta(int id, string name)
    {
        var baseMeta = TraceMetadata.CreateDefault();
        return new OverlayTraceMetadataProvider(baseMeta, id, name);
    }

    static void EnsureDir(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
    }

    static string DefaultTracePath(string name)
    {
        var safe = SafeFileName(name);
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        return Path.Combine("traces", $"{safe}_{stamp}.json");
    }

    static string SafeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "trace";

        var invalid = Path.GetInvalidFileNameChars();
        var set = new HashSet<char>(invalid);

        Span<char> buffer = stackalloc char[name.Length];
        var n = 0;

        foreach (var t in name)
        {
            var c = t;
            if (set.Contains(c) || c == ' ')
                c = '_';
            buffer[n++] = c;
        }

        return new string(buffer[..n]);
    }

    sealed class OverlayTraceMetadataProvider(ITraceMetadataProvider @base, int id, string name)
        : ITraceMetadataProvider
    {
        private readonly TraceMeta _meta = new(id, name, "Marked");

        public bool TryGet(int id1, out TraceMeta metadata)
        {
            if (id1 == id)
            {
                metadata = _meta;
                return true;
            }

            return @base.TryGet(id1, out metadata);
        }
    }
}
