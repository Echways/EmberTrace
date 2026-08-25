using System.Buffers.Binary;
using EmberTrace.Metadata;
using EmberTrace.Sessions;

namespace EmberTrace.Format.Internal;

internal static class TraceFormatWriter
{
    public static void WriteHeader(Stream stream, in SessionHeader header)
    {
        Span<byte> buffer = stackalloc byte[FormatConstants.HeaderSize];
        buffer.Clear();

        FormatConstants.Magic.CopyTo(buffer);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer[8..], header.Version);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer[10..],
            header.WasOverflow ? FormatConstants.FlagWasOverflow : (ushort)0);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[12..], FormatConstants.HeaderSize);
        BinaryPrimitives.WriteInt64LittleEndian(buffer[16..], header.TimestampFrequency);
        BinaryPrimitives.WriteInt64LittleEndian(buffer[24..], header.StartTimestamp);
        BinaryPrimitives.WriteInt64LittleEndian(buffer[32..], header.EndTimestamp);
        BinaryPrimitives.WriteInt64LittleEndian(buffer[40..], header.EventCount);
        BinaryPrimitives.WriteInt64LittleEndian(buffer[48..], header.DroppedEvents);
        BinaryPrimitives.WriteInt64LittleEndian(buffer[56..], header.DroppedChunks);
        BinaryPrimitives.WriteInt64LittleEndian(buffer[64..], header.SampledOutEvents);

        stream.Write(buffer);
    }

    public static void WriteThreadNames(Stream stream, IReadOnlyDictionary<int, string> threadNames)
    {
        stream.WriteByte(FormatConstants.Section.ThreadNames);
        VarInt.WriteUInt64(stream, (ulong)threadNames.Count);

        foreach (var pair in threadNames)
        {
            VarInt.WriteInt64(stream, pair.Key);
            VarInt.WriteString(stream, pair.Value ?? string.Empty);
        }
    }

    public static void WriteMetadata(Stream stream, IReadOnlyList<TraceMeta> entries)
    {
        stream.WriteByte(FormatConstants.Section.Metadata);
        VarInt.WriteUInt64(stream, (ulong)entries.Count);

        foreach (var entry in entries)
        {
            VarInt.WriteInt64(stream, entry.Id);
            VarInt.WriteString(stream, entry.Name ?? string.Empty);

            if (entry.Category is null)
            {
                stream.WriteByte(0);
            }
            else
            {
                stream.WriteByte(1);
                VarInt.WriteString(stream, entry.Category);
            }
        }
    }

    public static void WriteEvents(Stream stream, IEnumerable<TraceEventRecord> events, long eventCount)
    {
        stream.WriteByte(FormatConstants.Section.Events);
        VarInt.WriteUInt64(stream, (ulong)eventCount);

        long previousTimestamp = 0;
        var previousThreadId = 0;
        var previousTrackId = 0;
        long previousSequence = 0;

        foreach (var e in events)
        {
            var kind = (byte)e.Kind;
            var kindFitsInline = kind is > 0 and <= FormatConstants.EventFlags.KindMask;

            var flags = kindFitsInline ? kind : FormatConstants.EventFlags.KindExtended;

            if (e.FlowId != 0) flags |= FormatConstants.EventFlags.HasFlowId;
            if (e.Value != 0) flags |= FormatConstants.EventFlags.HasValue;
            if (e.ThreadId == previousThreadId) flags |= FormatConstants.EventFlags.SameThreadId;
            if (e.TrackId == previousTrackId) flags |= FormatConstants.EventFlags.SameTrackId;
            if (e.Sequence == previousSequence + 1) flags |= FormatConstants.EventFlags.SequenceIsPrevPlusOne;

            stream.WriteByte(flags);

            if (!kindFitsInline)
                stream.WriteByte(kind);

            var delta = e.Timestamp - previousTimestamp;
            if (delta < 0)
                throw new InvalidOperationException(
                    "Events must be written in non-decreasing timestamp order; use TraceSession.EnumerateEventsSorted().");

            VarInt.WriteUInt64(stream, (ulong)delta);
            VarInt.WriteInt64(stream, e.Id);

            if ((flags & FormatConstants.EventFlags.SameThreadId) == 0)
                VarInt.WriteInt64(stream, e.ThreadId);

            if ((flags & FormatConstants.EventFlags.SameTrackId) == 0)
                VarInt.WriteInt64(stream, e.TrackId);

            if ((flags & FormatConstants.EventFlags.HasFlowId) != 0)
                VarInt.WriteInt64(stream, e.FlowId);

            if ((flags & FormatConstants.EventFlags.HasValue) != 0)
                VarInt.WriteInt64(stream, e.Value);

            if ((flags & FormatConstants.EventFlags.SequenceIsPrevPlusOne) == 0)
                VarInt.WriteInt64(stream, e.Sequence);

            previousTimestamp = e.Timestamp;
            previousThreadId = e.ThreadId;
            previousTrackId = e.TrackId;
            previousSequence = e.Sequence;
        }
    }
}
