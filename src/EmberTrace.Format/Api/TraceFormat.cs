using EmberTrace.Format.Internal;
using EmberTrace.Metadata;
using EmberTrace.Sessions;

namespace EmberTrace;

public static class TraceFormat
{
    public const string FileExtension = ".ember";

    public static void Write(TraceSession session, Stream destination)
    {
        if (session is null) throw new ArgumentNullException(nameof(session));
        if (destination is null) throw new ArgumentNullException(nameof(destination));

        var header = new SessionHeader(
            FormatConstants.Version,
            session.WasOverflow,
            session.TimestampFrequency,
            session.StartTimestamp,
            session.EndTimestamp,
            session.EventCount,
            session.DroppedEvents,
            session.DroppedChunks,
            session.SampledOutEvents);

        TraceFormatWriter.WriteHeader(destination, header);
        TraceFormatWriter.WriteThreadNames(destination, session.ThreadNames);
        TraceFormatWriter.WriteMetadata(destination, CollectMetadata(session));
        TraceFormatWriter.WriteEvents(destination, EnumerateSorted(session), header.EventCount);
        destination.WriteByte(FormatConstants.Section.EndOfFile);
        destination.Flush();
    }

    public static void Write(TraceSession session, string path)
    {
        if (path is null) throw new ArgumentNullException(nameof(path));

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        using var stream = File.Create(path);
        Write(session, stream);
    }

    public static TraceSession Read(Stream source)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));

        return TraceFormatReader.ReadSession(source);
    }

    public static TraceSession Read(string path)
    {
        if (path is null) throw new ArgumentNullException(nameof(path));

        using var stream = File.OpenRead(path);
        return Read(stream);
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
