using EmberTrace.Format.Internal;
using EmberTrace.Metadata;
using EmberTrace.Sessions;

namespace EmberTrace;

public static class TraceFormat
{
    public const string FileExtension = ".ember";
    public const string CompressedFileExtension = ".ember.gz";

    public static void Write(TraceSession session, Stream destination)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(destination);

        var header = new SessionHeader(
            FormatConstants.Version,
            session.WasOverflow,
            session.TimestampFrequency,
            session.StartTimestamp,
            session.EndTimestamp,
            session.EventCount,
            session.DroppedEvents,
            session.DroppedChunks,
            session.SampledOutEvents,
            session.IsSnapshot,
            session.StartedAtUtc?.UtcTicks ?? 0);

        TraceFormatWriter.WriteHeader(destination, header);
        TraceFormatWriter.WriteThreadNames(destination, session.ThreadNames);
        TraceFormatWriter.WriteMetadata(destination, CollectMetadata(session));
        if (session.Environment is not null || session.EnvironmentAtStop is not null)
            TraceFormatWriter.WriteEnvironment(destination, session.Environment, session.EnvironmentAtStop);
        TraceFormatWriter.WriteEvents(destination, EnumerateSorted(session), header.EventCount);
        destination.WriteByte(FormatConstants.Section.EndOfFile);
        destination.Flush();
    }

    public static void Write(TraceSession session, string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        using var stream = File.Create(path);
        if (path.EndsWith(CompressedFileExtension, StringComparison.OrdinalIgnoreCase))
            GzipSupport.Write(buffered => Write(session, buffered), stream);
        else
            Write(session, stream);
    }

    public static TraceSession Read(Stream source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var owned = new List<IDisposable>();
        try
        {
            var stream = GzipSupport.Unwrap(source, owned, out var compressed);
            try
            {
                return TraceFormatReader.ReadSession(stream);
            }
            catch (EndOfStreamException e) when (compressed)
            {
                throw new InvalidDataException(GzipSupport.TruncatedMessage, e);
            }
        }
        finally
        {
            GzipSupport.Dispose(owned);
        }
    }

    public static TraceSession Read(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        using var stream = File.OpenRead(path);
        return Read(stream);
    }

    public static TraceStream OpenRead(Stream source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return Open(source, null);
    }

    public static TraceStream OpenRead(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan);
        return Open(file, file);
    }

    private static TraceStream Open(Stream source, IDisposable? ownedSource)
    {
        var owned = new List<IDisposable>();
        if (ownedSource is not null)
            owned.Add(ownedSource);

        try
        {
            var stream = GzipSupport.Unwrap(source, owned, out var compressed);
            try
            {
                return new TraceStream(TraceFormatReader.ReadPreamble(stream), stream, owned, compressed);
            }
            catch (EndOfStreamException e) when (compressed)
            {
                throw new InvalidDataException(GzipSupport.TruncatedMessage, e);
            }
        }
        catch
        {
            GzipSupport.Dispose(owned);
            throw;
        }
    }

    private static IEnumerable<TraceEventRecord> EnumerateSorted(TraceSession session)
    {
        foreach (var e in session.EnumerateEventsSorted())
            yield return e;
    }

    private static List<TraceMeta> CollectMetadata(TraceSession session)
    {
        var provider = session.Metadata;
        var seen = new HashSet<int>();
        var entries = new List<TraceMeta>();

        foreach (var e in session.EnumerateEvents())
        {
            if (!seen.Add(e.Id))
                continue;

            if (provider.TryGet(e.Id, out var meta))
                entries.Add(meta);
        }

        entries.Sort(static (a, b) => a.Id.CompareTo(b.Id));
        return entries;
    }
}
