namespace EmberTrace.Format.Internal;

internal readonly record struct SessionHeader(
    ushort Version,
    bool WasOverflow,
    long TimestampFrequency,
    long StartTimestamp,
    long EndTimestamp,
    long EventCount,
    long DroppedEvents,
    long DroppedChunks,
    long SampledOutEvents);
