using EmberTrace.Format.Internal;
using EmberTrace.Metadata;
using EmberTrace.Sessions;

namespace EmberTrace;

public sealed class TraceStream : IDisposable
{
    private readonly Stream _stream;
    private readonly List<IDisposable> _owned;
    private readonly bool _compressed;
    private readonly int _eventCount;
    private bool _enumerated;
    private bool _disposed;

    internal TraceStream(StreamPreamble preamble, Stream stream, List<IDisposable> owned, bool compressed)
    {
        _stream = stream;
        _owned = owned;
        _compressed = compressed;
        _eventCount = preamble.EventCount;

        StartTimestamp = preamble.Header.StartTimestamp;
        EndTimestamp = preamble.Header.EndTimestamp;
        TimestampFrequency = preamble.Header.TimestampFrequency;
        WasOverflow = preamble.Header.WasOverflow;
        DroppedEvents = preamble.Header.DroppedEvents;
        ThreadNames = preamble.ThreadNames;
        Metadata = preamble.Metadata;
        Environment = preamble.Environment;
        EnvironmentAtStop = preamble.EnvironmentAtStop;
    }

    public long StartTimestamp { get; }
    public long EndTimestamp { get; }
    public long TimestampFrequency { get; }
    public bool WasOverflow { get; }
    public long DroppedEvents { get; }
    public long EventCount => _eventCount;
    public IReadOnlyDictionary<int, string> ThreadNames { get; }
    public ITraceMetadataProvider Metadata { get; }
    public TraceEnvironment? Environment { get; }
    public TraceEnvironment? EnvironmentAtStop { get; }

    public IEnumerable<TraceEventRecord> Events
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_enumerated)
                throw new InvalidOperationException("The events of a TraceStream can be enumerated only once.");

            _enumerated = true;
            return Enumerate();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        GzipSupport.Dispose(_owned);
    }

    private IEnumerable<TraceEventRecord> Enumerate()
    {
        using var events = TraceFormatReader.EnumerateEvents(_stream, _eventCount).GetEnumerator();

        while (true)
        {
            TraceEventRecord current;
            try
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (!events.MoveNext())
                    break;

                current = events.Current;
            }
            catch (EndOfStreamException e) when (_compressed)
            {
                throw new InvalidDataException(GzipSupport.TruncatedMessage, e);
            }

            yield return current;
        }

        try
        {
            TraceFormatReader.ReadEndOfFile(_stream);
        }
        catch (EndOfStreamException e) when (_compressed)
        {
            throw new InvalidDataException(GzipSupport.TruncatedMessage, e);
        }
    }
}