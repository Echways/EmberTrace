namespace EmberTrace.Format.Internal;

internal static class FormatConstants
{
    public const ushort Version = 1;
    public const int HeaderSize = 72;
    public const ushort FlagWasOverflow = 1;

    public static ReadOnlySpan<byte> Magic => "EMBRTRC\0"u8;

    public static class Section
    {
        public const byte EndOfFile = 0;
        public const byte ThreadNames = 1;
        public const byte Metadata = 2;
        public const byte Events = 3;
    }

    public static class EventFlags
    {
        public const byte KindMask = 0b0000_0111;
        public const byte KindExtended = 0;
        public const byte HasFlowId = 1 << 3;
        public const byte HasValue = 1 << 4;
        public const byte SameThreadId = 1 << 5;
        public const byte SameTrackId = 1 << 6;
        public const byte SequenceIsPrevPlusOne = 1 << 7;
    }
}
