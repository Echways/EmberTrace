using System.Buffers.Binary;
using EmberTrace.Metadata;
using EmberTrace.Sessions;

namespace EmberTrace.Format.Internal;

internal static class TraceFormatReader
{
    private const int MinimumThreadNameBytes = 2;
    private const int MinimumMetadataBytes = 3;
    private const int MinimumEventBytes = 3;
    private const int MaxPreallocatedEntries = 1 << 12;
    private const int MaxPreallocatedEvents = 1 << 16;

    public static SessionHeader ReadHeader(Stream stream)
    {
        Span<byte> buffer = stackalloc byte[FormatConstants.HeaderSize];
        stream.ReadExactly(buffer);

        if (!buffer[..8].SequenceEqual(FormatConstants.Magic))
            throw new InvalidDataException("The stream is not an EmberTrace session file (bad magic).");

        var version = BinaryPrimitives.ReadUInt16LittleEndian(buffer[8..]);
        if (version > FormatConstants.Version)
            throw new InvalidDataException(
                $"Unsupported EmberTrace session file version {version}; this build reads up to {FormatConstants.Version}.");

        var flags = BinaryPrimitives.ReadUInt16LittleEndian(buffer[10..]);
        var headerSize = BinaryPrimitives.ReadUInt32LittleEndian(buffer[12..]);

        var header = new SessionHeader(
            version,
            (flags & FormatConstants.FlagWasOverflow) != 0,
            BinaryPrimitives.ReadInt64LittleEndian(buffer[16..]),
            BinaryPrimitives.ReadInt64LittleEndian(buffer[24..]),
            BinaryPrimitives.ReadInt64LittleEndian(buffer[32..]),
            BinaryPrimitives.ReadInt64LittleEndian(buffer[40..]),
            BinaryPrimitives.ReadInt64LittleEndian(buffer[48..]),
            BinaryPrimitives.ReadInt64LittleEndian(buffer[56..]),
            BinaryPrimitives.ReadInt64LittleEndian(buffer[64..]),
            (flags & FormatConstants.FlagIsSnapshot) != 0);

        if (headerSize > FormatConstants.HeaderSize)
            Skip(stream, headerSize - FormatConstants.HeaderSize);

        return header;
    }

    public static Dictionary<int, string> ReadThreadNames(Stream stream)
    {
        var count = ReadCount(stream, MinimumThreadNameBytes);
        var result = new Dictionary<int, string>(Math.Min(count, MaxPreallocatedEntries));

        for (var i = 0; i < count; i++)
        {
            var threadId = (int)VarInt.ReadInt64(stream);
            result[threadId] = VarInt.ReadString(stream);
        }

        return result;
    }

    public static List<TraceMeta> ReadMetadata(Stream stream)
    {
        var count = ReadCount(stream, MinimumMetadataBytes);
        var result = new List<TraceMeta>(Math.Min(count, MaxPreallocatedEntries));

        for (var i = 0; i < count; i++)
        {
            var id = (int)VarInt.ReadInt64(stream);
            var name = VarInt.ReadString(stream);

            var hasCategory = stream.ReadByte();
            if (hasCategory < 0)
                throw new EndOfStreamException("Unexpected end of stream while reading metadata.");

            var category = hasCategory == 1 ? VarInt.ReadString(stream) : null;
            result.Add(new TraceMeta(id, name, category));
        }

        return result;
    }

    public static TraceSession ReadSession(Stream stream)
    {
        var header = ReadHeader(stream);

        Dictionary<int, string>? threadNames = null;
        List<TraceMeta>? metadata = null;
        List<TraceEventRecord>? events = null;

        while (true)
        {
            var sectionId = stream.ReadByte();
            if (sectionId < 0)
                throw new EndOfStreamException("Unexpected end of stream; the end-of-file section is missing.");

            if (sectionId == FormatConstants.Section.EndOfFile)
                break;

            switch (sectionId)
            {
                case FormatConstants.Section.ThreadNames:
                    threadNames = ReadThreadNames(stream);
                    break;
                case FormatConstants.Section.Metadata:
                    metadata = ReadMetadata(stream);
                    break;
                case FormatConstants.Section.Events:
                    events = ReadEvents(stream);
                    break;
                default:
                    throw new InvalidDataException(
                        $"Unknown section id {sectionId} in an EmberTrace session file of version {header.Version}.");
            }
        }

        return TraceSession.FromEvents(
            events ?? [],
            header.StartTimestamp,
            header.EndTimestamp,
            header.TimestampFrequency,
            threadNames,
            metadata is null ? null : TraceMetadata.FromEntries(metadata),
            header.DroppedEvents,
            header.DroppedChunks,
            header.SampledOutEvents,
            header.WasOverflow,
            null,
            header.IsSnapshot);
    }

    public static List<TraceEventRecord> ReadEvents(Stream stream)
    {
        var count = ReadCount(stream, MinimumEventBytes);
        var result = new List<TraceEventRecord>(Math.Min(count, MaxPreallocatedEvents));

        long previousTimestamp = 0;
        var previousThreadId = 0;
        var previousTrackId = 0;
        long previousSequence = 0;

        for (var i = 0; i < count; i++)
        {
            var flagsByte = stream.ReadByte();
            if (flagsByte < 0)
                throw new EndOfStreamException("Unexpected end of stream while reading events.");

            var flags = (byte)flagsByte;
            var kind = (byte)(flags & FormatConstants.EventFlags.KindMask);

            if (kind == FormatConstants.EventFlags.KindExtended)
            {
                var extended = stream.ReadByte();
                if (extended < 0)
                    throw new EndOfStreamException("Unexpected end of stream while reading an extended event kind.");

                kind = (byte)extended;
            }

            var timestamp = previousTimestamp + (long)VarInt.ReadUInt64(stream);
            var id = (int)VarInt.ReadInt64(stream);

            var threadId = (flags & FormatConstants.EventFlags.SameThreadId) != 0
                ? previousThreadId
                : (int)VarInt.ReadInt64(stream);

            var trackId = (flags & FormatConstants.EventFlags.SameTrackId) != 0
                ? previousTrackId
                : (int)VarInt.ReadInt64(stream);

            var flowId = (flags & FormatConstants.EventFlags.HasFlowId) != 0
                ? VarInt.ReadInt64(stream)
                : 0;

            var value = (flags & FormatConstants.EventFlags.HasValue) != 0
                ? VarInt.ReadInt64(stream)
                : 0;

            var sequence = (flags & FormatConstants.EventFlags.SequenceIsPrevPlusOne) != 0
                ? previousSequence + 1
                : VarInt.ReadInt64(stream);

            result.Add(new TraceEventRecord(
                id, threadId, timestamp, (TraceEventKind)kind, flowId, value, sequence, trackId));

            previousTimestamp = timestamp;
            previousThreadId = threadId;
            previousTrackId = trackId;
            previousSequence = sequence;
        }

        return result;
    }

    private static int ReadCount(Stream stream, int minimumBytesPerEntry)
    {
        var count = VarInt.ReadUInt64(stream);
        if (count > int.MaxValue)
            throw new InvalidDataException("Section entry count exceeds the supported range.");

        if (stream.CanSeek)
        {
            var remaining = stream.Length - stream.Position;
            if ((long)count * minimumBytesPerEntry > remaining)
                throw new InvalidDataException(
                    $"Section declares {count} entries but only {remaining} bytes remain.");
        }

        return (int)count;
    }

    private static void Skip(Stream stream, long count)
    {
        if (stream.CanSeek)
        {
            stream.Seek(count, SeekOrigin.Current);
            return;
        }

        var buffer = new byte[Math.Min(count, 4096)];
        while (count > 0)
        {
            var read = stream.Read(buffer, 0, (int)Math.Min(count, buffer.Length));
            if (read == 0)
                throw new EndOfStreamException("Unexpected end of stream while skipping header padding.");

            count -= read;
        }
    }
}
