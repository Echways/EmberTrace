using System.Text;

namespace EmberTrace.Format.Internal;

internal static class VarInt
{
    public static void WriteUInt64(Stream stream, ulong value)
    {
        Span<byte> buffer = stackalloc byte[10];
        var count = 0;

        while (value >= 0x80)
        {
            buffer[count++] = (byte)(value | 0x80);
            value >>= 7;
        }

        buffer[count++] = (byte)value;
        stream.Write(buffer[..count]);
    }

    public static ulong ReadUInt64(Stream stream)
    {
        ulong result = 0;
        var shift = 0;

        while (true)
        {
            var b = stream.ReadByte();
            if (b < 0)
                throw new EndOfStreamException("Unexpected end of stream while reading a varint.");

            result |= (ulong)(b & 0x7F) << shift;

            if ((b & 0x80) == 0)
                return result;

            shift += 7;
            if (shift > 63)
                throw new InvalidDataException("Varint is longer than 10 bytes.");
        }
    }

    public static void WriteInt64(Stream stream, long value)
    {
        WriteUInt64(stream, ZigZagEncode(value));
    }

    public static long ReadInt64(Stream stream)
    {
        return ZigZagDecode(ReadUInt64(stream));
    }

    public static void WriteString(Stream stream, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        WriteUInt64(stream, (ulong)bytes.Length);
        stream.Write(bytes, 0, bytes.Length);
    }

    public static string ReadString(Stream stream)
    {
        var length = ReadUInt64(stream);
        if (length > int.MaxValue)
            throw new InvalidDataException("String length exceeds the supported range.");

        if (length == 0)
            return string.Empty;

        var buffer = new byte[(int)length];
        stream.ReadExactly(buffer);
        return Encoding.UTF8.GetString(buffer);
    }

    private static ulong ZigZagEncode(long value)
    {
        return (ulong)((value << 1) ^ (value >> 63));
    }

    private static long ZigZagDecode(ulong value)
    {
        return (long)(value >> 1) ^ -(long)(value & 1);
    }
}
